# Архитектура Madium Manager

## 1. Границы и допущения

Madium Manager — Windows desktop-менеджер аккаунтов и ОС-процессов. Он **не внедряется в процессы, не изменяет память и не обходит античит**. Единственный разрешённый сценарий запуска/перезапуска сессии:

```text
AccountCoordinator -> MediumInjectorBridge -> Medium.exe
```

Исполняемый файл игры не передаётся в `IProcessLauncher` и отсутствует в `MediumCliCommandBuilder`. Если Medium сам создаёт целевой процесс, Manager наблюдает его только через публичные ОС/API/лог-сигналы.

Предполагаются Windows 10/11 x64, одна активная сессия на аккаунт и документированный интерфейс Medium. Конкретные CLI-сwitches и правила определения ban/readiness нельзя выдумывать: они выносятся в адаптеры.

## 2. Стек

| Слой | Выбор | Причина |
|---|---|---|
| Язык/runtime | **C# 14, .NET 10 LTS** | Строгая типизация, зрелые `async`/`Task`, `Channel`, `CancellationToken`, `Process`, `HttpClient`, хорошая Windows-интеграция |
| Desktop UI | **WPF + MVVM** | Зрелость для утилит/таблиц/форм, меньше deployment-сложностей, чем у WinUI 3; не требует MSIX |
| MVVM | `CommunityToolkit.Mvvm` при реализации UI | Source generators, команды и observable-модели без ручного шаблонного кода |
| Процессы | `System.Diagnostics.Process`, `WaitForExitAsync`, `Kill(entireProcessTree: true)` | Наблюдение за конкретным handle/PID, кооперативная отмена, контроль дерева только запущенных процессов |
| Фоновая оркестрация | `System.Threading.Channels` | Actor/очередь команд на аккаунт; исключает restart из UI и `Exited` одновременно |
| HTTP | `HttpClientFactory`, `System.Text.Json`; resilience-поверхность для timeout/retry | Пулы handler, корректные DNS/TLS, централизованные политики |
| Хранение | SQLite (`Microsoft.Data.Sqlite`) для аккаунтов/заданий, JSON для малого reconnect-snapshot | Транзакции и миграции для основной БД; атомарный файл для последней точки reconnect |
| Секреты | Windows Credential Manager или DPAPI `CurrentUser` | Токены не лежат открытыми в SQLite/JSON; БД хранит ссылку на secret |
| Логи | `Microsoft.Extensions.Logging` + Serilog rolling JSON | Structured events, correlation IDs, ротация, redaction |
| Тесты | xUnit, fake process/probe/store | Детерминированные crash/timeout/race-тесты без реального Medium |
| Упаковка | self-contained single-file ZIP + **WiX MSI** | ZIP не требует установки; per-user MSI не требует admin для штатной установки |

Python + psutil подходит для прототипа, но даёт более слабую типизацию интеграционных контрактов и усложняет аккуратную поставку Windows desktop UI. C++ здесь не даёт преимуществ, а стоимость безопасной async/HTTP/UI-разработки заметно выше.

## 3. Структура и зависимости

```text
Madium-Manager/
├─ .github/
│  ├─ workflows/ci.yml
│  ├─ workflows/release.yml
│  └─ dependabot.yml
├─ docs/
│  ├─ architecture.md
│  └─ development.md
├─ packaging/MadiumManager.Installer/
│  ├─ MadiumManager.Installer.wixproj
│  └─ Package.wxs
├─ src/
│  ├─ MadiumManager.Domain/          # AccountStatus, ServerEndpoint, snapshots
│  ├─ MadiumManager.Application/     # use cases, ports, Watchdog state machine
│  ├─ MadiumManager.Infrastructure/  # Process, файлы, HTTP, SQLite, Medium adapters
│  └─ MadiumManager.Desktop/         # WPF, Views/ViewModels, DI composition root
├─ tests/MadiumManager.Tests/
├─ Directory.Build.props
├─ Directory.Packages.props
├─ global.json
└─ MadiumManager.slnx
```

Зависимости направлены внутрь:

```text
Desktop ───────────────> Application ───> Domain
   │                         ↑
   └────> Infrastructure ────┘
             (реализует ports Application)
```

- **Domain** не знает о WPF, HTTP и `Process`.
- **Application** владеет сценариями и состоянием; `MediumInjectorBridge` зависит только от интерфейсов.
- **Infrastructure** содержит системные адаптеры. Провайдеры API Medium/аккаунтов отделены друг от друга.
- **Desktop** содержит только MVVM и composition root. `async void` допустим лишь для UI event handler; Watchdog работает вне UI synchronization context.

### Модули

1. **Account Manager**
   - CRUD, lease/блокировка аккаунта, очередь, состояния `Ready -> Checking -> Leased -> Running -> Cooldown/Quarantined`.
   - SQLite-транзакция для lease предотвращает запуск одного аккаунта двумя workers.
   - Токены хранятся отдельно; логин/токен не попадают в command line без необходимости.

2. **Account Status / Ban Checker**
   - `IAccountStatusChecker` возвращает `Active`, `Banned`, `InvalidCredentials`, `RateLimited`, `ServiceUnavailable`, `Unknown`.
   - `Unknown` не равен `Banned`. По умолчанию безопасный workflow — пропустить запуск и повторить проверку позже по политике.
   - Официальный API предпочтительнее HTML-парсинга. HTTP timeout, `Retry-After`, jitter и circuit breaker настраиваются отдельно от 20-секундного process timeout.

3. **Server State Service**
   - Принимает только подтверждённые события `Connected/Disconnected` и публикует монотонную ревизию.
   - Сохраняет `AccountId + SessionId + IP + Port + CapturedAt + Source`.
   - Автоматический reconnect не использует snapshot другой сессии. При отсутствии актуального адреса аккаунт получает `ServerStateUnavailable`, а не подключается к случайному серверу.

4. **Medium Injector Bridge / Watchdog**
   - Единственный владелец запуска Medium и его cleanup.
   - Не знает о SQLite/UI и не принимает решение о следующем аккаунте; возвращает `WatchdogResult` координатору.

5. **Network / Logging**
   - Провайдерские `HttpMessageHandler`, короткие timeout, ограничение параллелизма и redaction.
   - Correlation: `AccountId` (или pseudonymous ID), `SessionId`, `Attempt`, `MediumPid`, `ExitCode`, `Reason`.
   - Authorization headers, cookies, токены и содержимое configs маскируются.

## 4. Захват состояния сервера

Приоритет источников:

1. **Документированный read-only API Medium**: лучший вариант. Нужны событие/статус `connected`, endpoint и session/revision. Опрос только через локальный authenticated API/named pipe.
2. **Structured log Medium**: хороший fallback. Читатель открывает файл с `FileShare.ReadWrite | FileShare.Delete`, поддерживает rotation/truncate, UTF-8, неполную последнюю строку и дедупликацию. Endpoint подтверждается только событием успешного подключения, а не строкой `connecting`.
3. **Config Medium**: пригоден только если Medium действительно записывает туда *текущее* подключение. Часто config содержит лишь желаемый/последний сервер; тогда это `unconfirmed`, а не основание для reconnect.
4. **ОС-таблица TCP/UDP по PID**: дополнительная эвристика. Можно сопоставить remote endpoint с PID, но NAT, несколько соединений, UDP, UDP без `connect`, proxies и служебные endpoints делают результат неоднозначным. Перехват/расшифровка трафика не нужны и не рекомендуются.

Каждый provider возвращает `Confirmed` либо `Unconfirmed`. Сетевой endpoint и значение из предположительного config нельзя молча повышать до `Confirmed`.

### Возврат endpoint в Medium

Порядок предпочтения:

1. **CLI** — `ProcessStartInfo.ArgumentList`, отдельные аргументы IP/port, без конкатенации shell-строки.
2. **Локальный API/named pipe** — команда с `SessionId`, `RequestId`, endpoint и timeout; требуется ack. Повтор команды должен быть идемпотентным.
3. **Session-specific config** — отдельный файл, запись `tmp -> flush -> atomic rename`, только пока Medium остановлен; backup и schema version обязательны. Не редактировать живой общий config.
4. **UI automation** — только как осознанный последний fallback: хрупко при смене фокуса, DPI, локализации и dialog windows.

`MediumCliCommandBuilder` в репозитории показывает механику, но его `--account-id`, `--reconnect-ip`, `--reconnect-port` — **placeholder-контракт**, который надо заменить документированными параметрами Medium.

## 5. Watchdog: пошаговый алгоритм

Состояния:

```text
Stopped -> CheckingAccount -> Starting -> Ready/Running
                              │  │             │
                              │  │             └─ UnexpectedExit -> WaitingToRestart -> Starting(Medium, reconnect)
                              │  ├─ ExitedBeforeReady -> AccountFailed
                              │  └─ 20 s timeout -> Cleanup -> AccountFailed
                              └─ StartException -> AccountFailed
Any state + user stop -> Stopping -> Stopped
```

1. Координатор атомарно берёт lease аккаунта и выполняет server-side status check.
2. Создаются новые `SessionId` и cancellation scope. Watchdog получает один аккаунт; параллельный `RunAsync` на том же instance отклоняется.
3. `IMediumCommandBuilder` создаёт **только Medium command**. `IProcessLauncher` использует `UseShellExecute=false` и `ArgumentList`.
4. Сразу после launch начинаются три ожидания: `MediumExit`, `ReadinessProbe`, общий startup deadline. По умолчанию deadline — **20 секунд**, включая cooperative launch и readiness.
5. `Readiness` означает не `Process.Start()`, а подтверждение Medium API/log handshake и, когда настроено, связь с target PID. Победивший результат обрабатывает только основной async loop.
6. При readiness startup timer выключается. Подтверждённый server snapshot сохраняется до перехода в `Running`.
7. При неожиданном завершении Medium текущий attempt закрывается и освобождается. Увеличивается generation/restart counter, выдерживается отменяемая пауза.
8. Перед reconnect Watchdog заново читает последний snapshot и проверяет `SessionId`. Затем создаёт новую команду **Medium + reconnect IP/port**. Game exe не вызывается.
9. Если 20 секунд истекли:
   - отменить readiness/launch scope;
   - попытаться orderly close в течение `StopTimeout` (по умолчанию 5 с);
   - при необходимости завершить только owned process tree и дождаться exit;
   - освободить handles в `finally`;
   - записать structured error `StartupTimeout`;
   - вернуть результат координатору. Координатор помечает аккаунт `Quarantined/StartTimeout`, ставит cooldown, снимает lease и переходит к следующему.
10. Ранний exit до readiness и launch exception тоже являются terminal failure для этого аккаунта, без быстрого бесконечного цикла.
11. Для crashes после readiness есть `MaxAutomaticRestarts`. Счётчик сбрасывается после `StableRunDuration`, чтобы старая ошибка много часов назад не суммировалась с новой серией.

### Мониторинг target process

В production-адаптере readiness возвращает target PID. Manager привязывается к этому конкретному PID/handle и добавляет `TargetExit` в тот же `WhenAny` основного цикла:

- **TargetExit первым**: остановить/договориться с Medium по его протоколу, cleanup attempt, затем reconnect через Medium.
- **MediumExit первым**: не искать процесс по имени и не запускать game exe. Проверить по протоколу Medium, можно ли привязаться к существующему target; если нет — завершать только однозначно связанный owned target по политике.
- Нельзя убивать процесс по имени: PID reuse и несколько экземпляров делают это опасным.

Текущий пример намеренно реализует Medium-lifecycle и возвращает `TargetProcessId` как readiness metadata; `TargetExit` добавляется provider-specific tracker без изменения Account Coordinator.

## 6. Как исключаются race conditions

- **Single writer / actor**: только один async loop меняет состояние и владеет handles. UI-команды `Stop/Restart` поступают через `Channel`, а не вызывают `Kill/Start` напрямую.
- **Generation**: каждый attempt имеет `SessionId + Attempt`. Отложенные события старого attempt игнорируются. В примере старые probes получают отдельный CTS и не могут сами запустить reconnect.
- **Нет restart из `Exited`**: событие лишь завершает `Task`; победу между exit/readiness/timeout определяет один `WhenAny` consumer.
- **Cooperative cancellation**: внешний stop отличается от внутреннего startup timeout. Cleanup использует отдельный bounded timeout и всё равно вызывает dispose.
- **Process identity**: хранится `Process`/handle конкретного запуска, а не результат `GetProcessesByName`.
- **Snapshot revision/session**: запись подтверждённого состояния атомарна; stale session отклоняется.
- **Ожидаемый stop**: пользовательская отмена не классифицируется как crash и не запускает reconnect.
- **Backoff/circuit breaker**: startup timeout — terminal для аккаунта; crashes после running ограничены счётчиком и паузой.

## 7. Подводные камни

| Область | Риск | Решение |
|---|---|---|
| UAC/integrity | Elevated Medium нельзя запустить из обычного процесса с `UseShellExecute=false`; `runas` даст интерактивный prompt | Штатно `asInvoker`; согласованный integrity level. Не скрывать UAC. При реальной необходимости — отдельно спроектированный elevated broker, а не prompt на каждый restart |
| Desktop session | Служба/session 0 не имеет интерактивного UI | Manager и GUI Medium запускать в пользовательской сессии |
| Handle/process leaks | Повторные `Process`, CTS, FileStream и stdout handlers не освобождены | `await using`/`finally`, bounded terminate + await exit + `DisposeAsync`, периодический soak-test |
| Зависший launcher | Shell association/UAC/dialog может блокировать ожидание | `UseShellExecute=false`, прямой абсолютный exe path; не считать `Task.Run(Process.Start)` отменяемым решением |
| Stdout/stderr | Переполненный pipe блокирует Medium | Если включить redirect — постоянно дренировать оба потока и ограничивать размер buffered lines |
| Medium single instance | Второй процесс сразу завершается из-за mutex/single-instance UI | Дождаться полного exit старого Medium, учитывать mutex и single-instance ack в readiness |
| File locks/rotation | Log/config занят, rotation в момент чтения | Share flags, retry только на файловых операциях, rotation-aware reader, atomic writes |
| Порт/сокет | `TIME_WAIT`, IPv6 formatting, DNS возвращает другой адрес | Хранить IP и port раздельно; не путать LISTEN endpoint с game server; не включать бездумно `SO_REUSEADDR` |
| PID reuse | Поздний event относится к другому процессу | Handle + SessionId/Attempt; никаких решений по имени/PID без связи с launch |
| Medium UI | Splash, auto-update, modal dialog, собственный reconnect меняют состояние | Versioned adapter contract; Manager является единственным owner автоматизации, UI Medium не дублирует reconnect policy |
| Антивирус | Частые процессы/unsigned binary вызывают репутационные блокировки | Code signing, прозрачные файлы/логи, минимум привилегий; не отключать защиту и не просить blanket exclusions |
| HTTP/status | Captcha, rate-limit, изменённый HTML принимаются за ban | Отдельные `Unknown/RateLimited`, schema validation, официальный API, наблюдаемость и ручной review |
| UI | Длительный await/блокировка зависает на dispatcher | Все операции async вне UI; в ViewModel только прогресс/DTO через dispatcher |

## 8. Тестовая стратегия

- Unit: `WhenAny` outcomes, 20-second timeout, exit-before-ready, missing/stale server, restart limit, cancellation during every state.
- Contract: каждый Medium CLI/API/log provider на общем наборе fixtures.
- Integration Windows: реальный harmless helper process вместо Medium; exit/kill/stdout/log rotation/UAC negative cases.
- Soak: тысячи циклов start/exit/restart, контроль handles, working set, файловых locks и orphan processes.
- Packaging: чистая Windows VM, установка/обновление/удаление MSI, запуск ZIP без установленного .NET, проверка SHA-256 и подписи.
