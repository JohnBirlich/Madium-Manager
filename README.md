<p align="center">
  <img src="assets/branding/madium-manager-logo-256.png" width="112" alt="Madium Manager logo" />
</p>

<h1 align="center">Madium Manager</h1>

Windows desktop-менеджер аккаунтов и Medium-процессов. Проект работает только на уровне ОС и публичных интеграционных интерфейсов: запуск Medium, наблюдение за процессами, HTTP, чтение логов/config. Он не внедряется в процессы, не изменяет память и не реализует обход античита.

## Что уже заложено

- C# / .NET 10, полноценный WPF Control Center и разделение Domain / Application / Infrastructure.
- Dashboard, account CRUD, поиск/фильтры, live-статусы, журнал, экспорт и настройки.
- Интерактивный **явно обозначенный demo-runtime**: start/stop, server-side check, crash/reconnect без процессов ОС.
- Async `MediumInjectorBridge` с default startup timeout **20 секунд**.
- Автоматический reconnect **только через Medium.exe**, без запуска game executable.
- Persistent `ServerStateSnapshot`: IP, port, account/session, source и timestamp.
- Защита от stale reconnect: endpoint должен принадлежать текущей `SessionId`.
- Cooperative cancellation, ограничение числа restart, bounded terminate и гарантированный `DisposeAsync`.
- Атомарное файловое хранение последнего подтверждённого server state.
- Unit-тесты crash -> saved IP -> Medium reconnect и startup timeout -> cleanup.
- GitHub Actions CI, SemVer tag release, portable ZIP, per-user WiX MSI и SHA-256.
- Единый логотип для exe, окна, «Пуска» и установщика; светлая упрощённая тема.
- MSI upgrade с удалением предыдущей версии и удаление через Windows или меню «Пуск».

## Важное ограничение каркаса

Интерфейс полностью интерактивен, но текущий runtime демонстрационный и не создаёт процессы ОС — это постоянно обозначено в окне. Для реальной интеграции параметры `MediumCliCommandBuilder` (`--account-id`, `--reconnect-ip`, `--reconnect-port`) необходимо заменить документированными параметрами Medium, а `IMediumReadinessProbe` и `IAccountStatusChecker` — provider-specific реализациями. Демонстрационные статусы не выдаются за реальные readiness/ban verdicts.

## Документация

- [Архитектура, Watchdog flow, server capture и OS-риски](docs/architecture.md)
- [Локальная разработка, CI/CD, packaging и релизы](docs/development.md)
- [Интерфейс и интерактивные сценарии](docs/user-interface.md)
- [Установка, обновление и удаление](docs/installation.md)

## Быстрый старт

```powershell
dotnet restore MadiumManager.slnx
dotnet build MadiumManager.slnx --configuration Release
dotnet test MadiumManager.slnx --configuration Release
```

WPF-сборка предназначена для Windows. SDK-версия закреплена в `global.json`.
