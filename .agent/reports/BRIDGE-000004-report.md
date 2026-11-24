---
id: BRIDGE-000004
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T22:05:00+03:00
updated: 2026-11-24T22:05:00+03:00
base: source
branch: task/BRIDGE-000004-exchange-package
commit: 44d584216ff9c44e530a08359dbd688f342aa8a3
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/6
related: [BRIDGE-000003, BRIDGE-000004]
---

# BRIDGE-000004 — Exchange Package Core

## 1. Итог

**BRIDGE-000004 выполнен.**

В `EcadMcadBridge.Core` добавлен независимый от CAD Exchange Package:

- deterministic `PackageManifest` `ecad-mcad-bridge/package` 1.0.0;
- `manifest.json`, `board.snapshot.json`, `checksums.sha256`;
- `ExchangePackageWriter` с валидацией, атомарной записью и запретом overwrite;
- `ExchangePackageReader` с проверкой schema/units/путей/checksums/размеров/SHA-256
  и `BoardSnapshotValidator`;
- `ExchangePackageValidator` и `PackageDiagnostic` (`EMB-PKG-*`);
- unit tests на Writer, Reader, безопасность путей и determinism.

Публичный `BoardSnapshot` API не изменялся. Core не получил зависимостей от Altium SDK,
SOLIDWORKS Interop, конфигурации CAD или UI.

## 2. Модель manifest

`PackageManifest`:

```text
schema            ecad-mcad-bridge/package
schemaVersion     1.0.0
packageId         UUID
createdAtUtc      UTC timestamp
producer          component, version
source            cad, documentSha256
sourceUnits       altium-iu
units             mm
files[]           path, sha256, size
capabilities[]    unique strings
```

`PackageManifestJson`:

- пишет поля в фиксированном порядке;
- UTF-8 без BOM, LF, invariant culture;
- `packageId` — UUID в lowercase `D`-формате;
- `createdAtUtc` — `yyyy-MM-ddTHH:mm:ss.FFFFFFFZ`;
- `files` сортируется по ordinal path;
- `capabilities` уникальны и сортируются ordinal;
- неизвестные поля при чтении игнорируются;
- отсутствующие обязательные поля дают `InvalidManifest`;
- несовместимый major `schemaVersion` даёт `UnsupportedSchemaVersion`.

Snapshot сериализуется только существующим `BoardSnapshotJson`; второго формата нет.
`documentSha256` передаётся вызывающей стороной как metadata, Core не открывает PCB.

## 3. Контрольные суммы

`checksums.sha256`:

```text
<lowercase-sha256>  <relative-path>
```

- LF, UTF-8 без BOM;
- пути только relative POSIX (`/`);
- сортировка по ordinal path;
- покрывает `manifest.json` и `board.snapshot.json`;
- не покрывает сам `checksums.sha256`;
- циклическая зависимость отсутствует;
- в `manifest.files` входит `board.snapshot.json` с SHA-256 и размером.

## 4. Public API

### Writer

```csharp
public static PackageWriteResult ExchangePackageWriter.Write(
    string outputRoot,
    BoardSnapshot snapshot,
    PackageWriteOptions options);
```

`PackageWriteOptions`: `PackageId`, `CreatedAtUtc`, `Producer`, `Source`, `Capabilities`.
`PackageWriteResult`: `IsSuccess`, `PackageDirectory`, `Manifest`, `Diagnostics`,
`Errors`, `Warnings`.

Порядок записи:

1. `BoardSnapshotValidator.Validate`; при errors пакет не создаётся.
2. Сериализация snapshot через `BoardSnapshotJson`.
3. Вычисление SHA-256 и размера snapshot.
4. Формирование и валидация manifest.
5. Вычисление SHA-256 manifest.
6. Формирование checksums.
7. Запись во временный каталог `.tmp-<id>-<random>` рядом с target.
8. Проверка записанных байтов snapshot/manifest/checksums.
9. Атомарный `Directory.Move` в `<output-root>/<package-id>`.

Существующий package-id не перезаписывается. При ошибке временный каталог удаляется;
частично опубликованный пакет не остаётся.

### Reader

```csharp
public static PackageReadResult ExchangePackageReader.Read(string packageDirectory);
```

`PackageReadResult`: `IsSuccess`, `Manifest`, `Snapshot`, `Diagnostics`, `Errors`, `Warnings`.

Reader:

1. Читает manifest.
2. Проверяет schema, major version, metadata, units, capabilities.
3. Проверяет безопасность всех relative paths.
4. Читает checksums и сверяет набор файлов.
5. Проверяет отсутствие лишних файлов и наличие заявленных.
6. Проверяет размеры и SHA-256.
7. Десериализует snapshot через `BoardSnapshotJson`.
8. Выполняет `BoardSnapshotValidator`.
9. Не изменяет пакет и не обращается к CAD.

### Validator

```csharp
public static IReadOnlyList<PackageDiagnostic> ExchangePackageValidator.ValidateManifest(
    PackageManifest manifest);
```

## 5. Безопасность путей

`PackagePath`:

- отвергает absolute paths, `..`, `.`, пустые сегменты;
- отвергает `\`, `:` и пути с ведущим `/`;
- нормализует `/` и проверяет, что полный путь остаётся внутри корня пакета;
- не допускает `checksums.sha256` в списке покрываемых файлов;
- дублирующиеся пути — ошибка.

Это защищает от выхода за корень пакета и path traversal в manifest/checksums.

## 6. Тесты

Добавлен `ExchangePackageTests` в `EcadMcadBridge.Core.Tests`.

Покрыто:

**Writer**
- создание полного пакета и структура файлов;
- manifest metadata, capabilities и files;
- SHA-256 и размер snapshot;
- checksums manifest+snapshot;
- отказ при ошибках `BoardSnapshotValidator`;
- запрет overwrite существующего package-id;
- отсутствие частичного пакета при ошибке записи;
- невалидная metadata;
- determinism для одинаковых snapshot/metadata.

**Reader**
- успешное чтение и round-trip snapshot;
- отсутствующий manifest/checksums/snapshot;
- checksum mismatch и size mismatch;
- неверная schema и major version;
- неверные units;
- неверный формат SHA-256;
- absolute path, `../`, duplicate paths;
- повреждённый manifest JSON и повреждённый snapshot JSON;
- extra file;
- snapshot с ошибками валидации;
- unknown manifest fields игнорируются.

**Determinism**
- `board.snapshot.json`, `manifest.json`, `checksums.sha256` byte-identical при
  одинаковых входных данных.

## 7. Build и тесты

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release — Core.Tests 66/66, Altium.Tests 65/65, всего 131/131
```

Существующие 41 Core-теста и 65 Altium-тестов не регрессировали.
Новых Core-тестов: 25.

## 8. Изменённые файлы

Новые:

```text
src/EcadMcadBridge.Core/Packaging/PackageDiagnostic.cs
src/EcadMcadBridge.Core/Packaging/PackageManifest.cs
src/EcadMcadBridge.Core/Packaging/PackageManifestJson.cs
src/EcadMcadBridge.Core/Packaging/PackageChecksums.cs
src/EcadMcadBridge.Core/Packaging/PackagePath.cs
src/EcadMcadBridge.Core/Packaging/ExchangePackageValidator.cs
src/EcadMcadBridge.Core/Packaging/ExchangePackageWriter.cs
src/EcadMcadBridge.Core/Packaging/ExchangePackageReader.cs
tests/EcadMcadBridge.Core.Tests/ExchangePackageTests.cs
```

Изменены:

```text
.agent/ARCHITECTURE.md
.agent/PROJECT_CONTEXT.md
.agent/reports/BRIDGE-000004-report.md
AGENTS.md
README.md
```

## 9. Ограничения

- `attachments/`, ZIP, ChangeSet/reverse import не реализованы;
- Altium export command не изменялся и не вызывает Writer;
- SOLIDWORKS Add-in не создавался;
- Reader проверяет только пакет и snapshot, но не создаёт/не меняет CAD-модель;
- `documentSha256` — caller metadata, Core не читает `.PcbDoc`;
- package-id и время передаются вызывающей стороной для тестируемости;
- manifest допускает unknown fields, но несовместимый major и неверные units
  отклоняются, а не autocast.

## 10. Commit / PR

- Commit реализации и документации:
  `44d584216ff9c44e530a08359dbd688f342aa8a3`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/6
  (открыт, не слит).
- Ветка: `task/BRIDGE-000004-exchange-package`, база `source`.
- Force-push, rebase и merge не выполнялись.
