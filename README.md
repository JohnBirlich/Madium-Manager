# Madium Manager

Windows desktop-менеджер аккаунтов и Medium-процессов. Проект работает только на уровне ОС и публичных интеграционных интерфейсов: запуск Medium, наблюдение за процессами, HTTP, чтение логов/config. Он не внедряется в процессы, не изменяет память и не реализует обход античита.

## Что уже заложено

- C# / .NET 10, WPF Desktop shell, разделение Domain / Application / Infrastructure.
- Async `MediumInjectorBridge` с default startup timeout **20 секунд**.
- Автоматический reconnect **только через Medium.exe**, без запуска game executable.
- Persistent `ServerStateSnapshot`: IP, port, account/session, source и timestamp.
- Защита от stale reconnect: endpoint должен принадлежать текущей `SessionId`.
- Cooperative cancellation, ограничение числа restart, bounded terminate и гарантированный `DisposeAsync`.
- Атомарное файловое хранение последнего подтверждённого server state.
- Unit-тесты crash -> saved IP -> Medium reconnect и startup timeout -> cleanup.
- GitHub Actions CI, SemVer tag release, portable ZIP, per-user WiX MSI и SHA-256.

## Важное ограничение каркаса

Это архитектурный/тестируемый skeleton, а не готовая интеграция с конкретной версией Medium. Параметры в `MediumCliCommandBuilder` (`--account-id`, `--reconnect-ip`, `--reconnect-port`) являются примером контракта. Их необходимо заменить документированными параметрами Medium. `IMediumReadinessProbe` и `IAccountStatusChecker` требуют provider-specific реализаций; без них приложение не выдаёт ложных readiness/ban verdicts.

## Документация

- [Архитектура, Watchdog flow, server capture и OS-риски](docs/architecture.md)
- [Локальная разработка, CI/CD, packaging и релизы](docs/development.md)

## Быстрый старт

```powershell
dotnet restore MadiumManager.slnx
dotnet build MadiumManager.slnx --configuration Release
dotnet test MadiumManager.slnx --configuration Release
```

WPF-сборка предназначена для Windows. SDK-версия закреплена в `global.json`.
