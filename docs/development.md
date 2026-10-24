# Разработка, CI/CD и выпуск релизов

## Локальная разработка

Требования: Windows 10/11 для WPF, .NET SDK из `global.json`, Git. Core и unit-тесты не зависят от реального Medium.

```powershell
dotnet restore MadiumManager.slnx
dotnet build MadiumManager.slnx --configuration Release
dotnet test MadiumManager.slnx --configuration Release
```

Portable build:

```powershell
dotnet publish src/MadiumManager.Desktop/MadiumManager.Desktop.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  --output artifacts/publish `
  -p:PublishSingleFile=true -p:PublishTrimmed=false `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

MSI собирается после `publish`, поскольку WiX упаковывает полученный `MadiumManager.exe`:

```powershell
dotnet build packaging/MadiumManager.Installer/MadiumManager.Installer.wixproj `
  --configuration Release -p:MsiVersion=0.3.0 `
  -p:PublishDir="$PWD/artifacts/publish"
```

Не помещайте в Git реальные аккаунты, токены, SQLite/JSON data, дампы, Medium.exe или конфиги с секретами. Соответствующие локальные пути уже добавлены в `.gitignore`.

## CI на push/PR

Workflow `.github/workflows/ci.yml` запускается на:

- `push` в `main`;
- Pull Request в `main`;
- ручной `workflow_dispatch` для любой выбранной ветки.

Windows job выполняет:

1. checkout и установку SDK строго по `global.json`;
2. NuGet restore с кешем;
3. `dotnet format --verify-no-changes`;
4. Release build всей solution;
5. unit-тесты с TRX и coverage;
6. self-contained `win-x64` publish;
7. ZIP packaging и сборку per-user MSI;
8. загрузку test results, ZIP и MSI как GitHub Actions artifacts.

Job имеет read-only repository permissions и timeout. Новый push в ту же ветку отменяет устаревший CI run.

Рекомендуемые настройки GitHub после первого успешного прогона:

- запретить direct push в `main`;
- требовать PR и минимум один review;
- требовать прохождение `Windows build, tests and portable package`;
- включить **Require branches to be up to date**;
- запретить force-push и удаление `main`;
- включить Dependabot security updates.

## CD по SemVer tag

Workflow `.github/workflows/release.yml` запускается на `v*.*.*` и дополнительно проверяет строгий stable SemVer: `v1.2.3`, но не `v01.2.3` и не `v1.2`.

Сценарий:

1. проверить tag и извлечь версию `1.2.3`;
2. restore/build/test;
3. опубликовать self-contained single-file `MadiumManager.exe` с версией сборки и commit SHA в informational version;
4. создать `MadiumManager-1.2.3-win-x64.zip`;
5. собрать per-user `MadiumManager-1.2.3-win-x64.msi` через WiX;
6. создать `SHA256SUMS.txt` для всех артефактов;
7. опубликовать файлы в GitHub Release с автоматически сгенерированными notes.

Пример выпуска после зелёного CI в `main`:

```powershell
git checkout main
git pull --ff-only
git tag v0.1.0
git push origin v0.1.0
```

Тег должен создаваться только от проверенного commit. Semantic Versioning:

- **MAJOR** — несовместимые config/storage/API изменения;
- **MINOR** — обратно совместимый функционал;
- **PATCH** — исправления без изменения контрактов.

Текущий release workflow намеренно принимает только стабильные версии: MSI не поддерживает SemVer prerelease-строку напрямую. Для `rc/beta` следует добавить отдельную политику версий и prerelease Release, а не подменять номер молча.

## Подпись и доверие к дистрибутиву

Сейчас артефакты имеют SHA-256, но **не Authenticode-подписаны**. Для публичной дистрибуции добавьте подпись EXE и MSI до расчёта checksums, используя Azure Trusted Signing, SignPath или корпоративный Authenticode certificate. Приватный ключ/сертификат нельзя хранить в репозитории или логировать. Без подписи Windows SmartScreen может показывать предупреждение.

SHA-256 подтверждает целостность файла, но не заменяет подпись издателя. Дополнительно рекомендуется публиковать provenance/SBOM и включить автоматическое обновление только после проверки подписи и хеша пакета.

## Поставка Medium

Medium.exe не входит в этот репозиторий и release artifacts. Путь к уже установленному Medium должен задавать пользователь через проверенную конфигурацию. Автоматическое скачивание/обновление Medium добавляется только при наличии права на распространение, доверенного HTTPS-источника, проверки подписи/хеша и атомарной установки обновления.
