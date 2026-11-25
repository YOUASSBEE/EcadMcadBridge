---
id: BRIDGE-000013
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T13:20:00+03:00
updated: 2026-11-25T13:20:00+03:00
base: source
branch: agent/BRIDGE-000013-core-3d-assets
commit: ef3b1b7
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/12
related:
  - BRIDGE-000004
  - BRIDGE-000007
  - BRIDGE-000009
  - BRIDGE-000010
  - BRIDGE-000011
  - BRIDGE-000012
---

# BRIDGE-000013 — Core: 3D Asset Exchange Package

## 1. Итог

Реализован контракт Core для необязательного каталога 3D-моделей компонентов и
content-addressed файлов STEP/X_T. Manifest остаётся `1.0.0`; каталог имеет
отдельную схему `ecad-mcad-bridge/component-models` `1.0.0`.

Что сделано:

- типизированные модели `ComponentModelCatalog`, `ComponentModelEntry`,
  `ComponentBodyEntry`, `ModelOrigin`, `ModelState` и enum-значения
  representation/sourceOrigin/assetAcquisition/assetFormat;
- детерминированный JSON каталога: UTF-8 без BOM, LF, invariant numbers,
  фиксированный порядок полей, сортировка компонентов по `componentId`, тел по
  `bodyOrdinal`;
- типизированный write-request: компоненты/тела и исходные файлы, связанные по
  `assetKey`; публичный 3-аргументный `Write` сохранён;
- Writer сам вычисляет SHA-256/размер фактически записанных байтов, формирует
  `assets/3d/<lowercase-sha256>.step|.x_t`, дедуплицирует assets, добавляет
  каталог и файлы в manifest/checksums, пишет во временный каталог, проверяет и
  публикует через `Directory.Move`;
- Reader возвращает nullable `Models` и проверяет каталог, ссылки, manifest,
  checksums, размеры, SHA-256, расширения, обратные ссылки и case-insensitive
  конфликты;
- `PackageChecksums.ComputeFileSha256` и `PackageFileHasher.ComputeStableSha256`
  переведены на потоковое чтение;
- новые стабильные коды `EMB-PKG-022`…`EMB-PKG-026`;
- обязательные тесты раздела 9 добавлены; старые тесты Writer/Reader не менялись
  по смыслу и проходят;
- R1: добавлена численная сверка IU/мм (X/Y/Z/dz), строгое различие
  `assetPath == null` и пустой/whitespace строки, обязательное присутствие всех
  nullable-полей JSON.

Не сделано и не входило в scope: извлечение моделей из Altium, `IPCB_ComponentBody`
enumeration, `ExportToStep`, `SaveModelToFile`, SOLIDWORKS Add-in, построение
полной матрицы `T_B_S`, ZIP-архивирование.

## 2. Изменённые файлы

Новые:

- `src/EcadMcadBridge.Core/Model/ComponentModels.cs`
- `src/EcadMcadBridge.Core/Packaging/ComponentModelsJson.cs`
- `src/EcadMcadBridge.Core/Packaging/ComponentModelsValidator.cs`
- `src/EcadMcadBridge.Core/Packaging/ComponentModelsWriteRequest.cs`
- `tests/EcadMcadBridge.Core.Tests/ComponentModelsPackageTests.cs`

Изменённые:

- `src/EcadMcadBridge.Core/Packaging/ExchangePackageWriter.cs`
- `src/EcadMcadBridge.Core/Packaging/ExchangePackageReader.cs`
- `src/EcadMcadBridge.Core/Packaging/ExchangePackageValidator.cs`
- `src/EcadMcadBridge.Core/Packaging/PackageManifest.cs`
- `src/EcadMcadBridge.Core/Packaging/PackageChecksums.cs`
- `src/EcadMcadBridge.Core/Packaging/PackageFileHasher.cs`
- `src/EcadMcadBridge.Core/Packaging/PackageDiagnostic.cs`
- `.agent/DECISIONS.md`
- `.agent/ARCHITECTURE.md`
- `.agent/PROJECT_CONTEXT.md`
- `.agent/tasks/BRIDGE-000013.md`
- `.agent/reports/BRIDGE-000013-report.md`

Не изменялись: `BoardSnapshot`, `BoardSnapshotJson`, `BoardSnapshotValidator`,
`SDK_API_LEDGER.md`, Altium adapter/commands, Core TFM, Altium TFM.

## 3. Принятые решения и обоснование

### 3.1 Версия manifest

Manifest остаётся `ecad-mcad-bridge/package` `1.0.0`: каталог необязателен, а
старые пакеты должны читаться без изменений. Capability `component-3d-models`
включается Writer-ом автоматически при наличии каталога и обязательна при
чтении. Каталог без capability, capability без каталога и расхождение manifest
дают `EMB-PKG-026`.

### 3.2 Схема каталога

Каталог версионируется отдельно (`ecad-mcad-bridge/component-models` `1.0.0`),
чтобы не повышать версию manifest из-за дополнительного файла. Поля записываются
в фиксированном порядке; `assetPath`/`assetFormat` и `sdkOrigin`/`sdkState`
всегда присутствуют, неизвестные значения — `null`. Углы сохраняются как
получены, включая 360°; `dz` хранится парой IU/мм.

### 3.3 Связь component/body/asset

- `componentId` — `ComponentInstance.Id` из snapshot; `designator` сверяется.
- `bodyOrdinal` — локальный нулевой ordinal; повтор внутри компонента запрещён.
- `assetKey` существует только на границе Writer-а и не сериализуется.
  Writer заменяет его на `assetFormat` и относительный
  `assets/3d/<sha256>.<ext>`.
- Один asset может использоваться несколькими телами и компонентами; Writer
  хранит его один раз, Reader разрешает повторные ссылки.

### 3.4 Сырые данные вместо догадок

Каталог хранит `representation`, `sourceOrigin`, `assetAcquisition`,
`assetFormat`, `sdkOrigin`, `sdkState`, `modelRotationDegRaw`. Он **не**
содержит и не вычисляет полную матрицу `T_B_S`, Z/Bottom, порядок Euler,
зеркальное отражение или преобразование компонента. `GetEmbed=false` не
превращается в `linked`; при отсутствии доказательств используется `unknown`.

### 3.5 Content-addressed assets и целостность

- Имя файла — lowercase SHA-256 фактически записанных байтов.
- Расширение: `.step` для `step-ap214`, `.x_t` для `parasolid-x_t`.
- Все assets и `models/models.json` входят в `manifest.files` и
  `checksums.sha256`; `manifest.json` входит только в checksums.
- Reader проверяет прямые и обратные ссылки: нет потерянных и неиспользуемых
  файлов в `assets/3d/`; повторные ссылки допустимы; case-insensitive конфликты
  путей запрещены.

### 3.6 Потоковая обработка

Writer читает source блоками по 80 КиБ, одновременно копируя в staging и
обновляя `IncrementalHash`; хеш относится к тем же байтам, которые записаны.
До и после копирования сравниваются длина и `LastWriteTimeUtc`; изменение
отклоняется с `EMB-PKG-025`. Финальный asset проверяется повторным потоковым
SHA-256. Reader хеширует файлы потоково и не загружает STEP/X_T в память.

### 3.7 Диагностика

Добавлены коды:

| Код | Назначение |
|---|---|
| `EMB-PKG-022` | некорректный каталог: JSON/поля/enum/`NaN`/`Infinity`/парность IU-мм |
| `EMB-PKG-023` | неподдерживаемая schema name или major version каталога |
| `EMB-PKG-024` | неизвестный/повторный `componentId`, designator, `bodyOrdinal` |
| `EMB-PKG-025` | путь/формат/расширение/наличие/обратная ссылка asset |
| `EMB-PKG-026` | несовпадение capability и `models/models.json` |

Сообщения не содержат абсолютных путей исходных моделей.

### 3.8 R1 — усиление строгости валидации

**R1-1. Численная согласованность IU/мм.** `ValidatePair` теперь проверяет не
только одновременное наличие IU и мм, но и значение:

```text
Math.Abs(mm - AltiumUnits.IuToMm(iu)) <= 1e-9
```

Допуск `1e-9` совпадает с `BoardSnapshotValidator.UnitToleranceMm`. Проверяются
`sdkOrigin.x`, `sdkOrigin.y`, `sdkOrigin.z` и `sdkState.dz`. Оба `null` допустимы;
только одно значение, несовпадение или `NaN`/`Infinity` дают `EMB-PKG-022`.
Значения не пересчитываются и не исправляются автоматически: Writer отклоняет
некорректный вход, Reader — некорректный каталог.

**R1-2. `assetPath`: null vs пустая строка.** Отсутствие файла представляется
только `assetPath == null`. Пустая и whitespace-only строка — `EMB-PKG-025`.
`assetPath` и `assetFormat` обязательны вместе: `null` + формат или путь + `null`
формат отклоняются. `ValidateAssetLinks` пропускает только `null`, а не пустую
строку, поэтому некорректная ссылка не может выглядеть как отсутствие asset.

**R1-3. Обязательные nullable-поля JSON.** Для схемы 1.0.0 Reader требует
физического присутствия `assetFormat`, `assetPath`, всех полей `sdkOrigin`
(`xIu/xMm/yIu/yMm/zIu/zMm`), всех полей `sdkState`
(`rotationXDegRaw/rotationYDegRaw/rotationZDegRaw/dzIu/dzMm`) и
`modelRotationDegRaw`. Явный JSON `null` по-прежнему допустим; отсутствующее
свойство даёт `JsonException`, которую Reader преобразует в `EMB-PKG-022`.
Writer всегда сериализует эти поля, поэтому созданные им пакеты проходят
round-trip.

## 4. Неподтверждённые вопросы и риски

- Полная матрица `T_B_S`, Z-origin, Bottom и порядок Euler остаются
  неподтверждёнными. Каталог намеренно хранит только сырые значения API.
  V-27/V-30 не закрываются.
- `source-copy-verified` — утверждение вызывающей стороны о побайтовой
  идентичности; Writer проверяет только переданный файл и его стабильность во
  время копирования, но не доказывает происхождение копии.
- Проверка «файл не изменился» использует длину и `LastWriteTimeUtc`. Если
  содержимое изменено с сохранением размера и mtime, это не будет обнаружено;
  записанный asset при этом остаётся целостным относительно собственного
  SHA-256.
- `bodyOrdinal` не является глобально стабильным идентификатором между
  сессиями; это зафиксировано в контракте.
- `componentId` опирается на `ComponentInstance.Id` snapshot; стабильность
  `SourceUniqueId` по-прежнему не предполагается.
- Тест case-insensitive конфликта путей использует файловую систему Windows,
  где такие имена не могут сосуществовать как разные файлы; проверка manifest
  выполняется до обращения к диску.
- Извлечение геометрии из Altium и SOLIDWORKS-интеграция не реализованы и
  требуют `BRIDGE-000014`.

## 5. Проверки

Окружение:

- .NET SDK 10;
- `EcadMcadBridge.Core` — `net10.0`;
- `EcadMcadBridge.Altium` — `net10.0-windows`;
- Altium и SOLIDWORKS для тестов не требуются.

Команды и результаты:

- `dotnet build -c Release` — 0 warnings, 0 errors.
- `dotnet test -c Release --no-build`:
  - Core.Tests: 140/140;
  - Altium.Tests: 82/82;
  - всего 222/222.
- `git diff --check` — проверяется перед коммитом.
- Бинарные STEP/X_T/PCB в Git не добавлялись.
- Ручной smoke-тест в Altium/SOLIDWORKS не выполнялся: задача Core-only, Add-in и
  CAD API не изменялись; smoke-тест в CAD неприменим.

Покрытие обязательных тестов:

| Требование | Тест |
|---|---|
| старый пакет, `Models == null`, manifest 1.0.0 | `Reader_ReturnsNullCatalogForPackageWithoutModels`, существующие тесты |
| round-trip, несколько компонентов/тел | `WriterAndReader_RoundTripCatalog` |
| пустой `bodies` | `WriterAndReader_SupportEmptyBodies` |
| общий asset | `WriterAndReader_ShareAssetAcrossBodies` |
| STEP и X_T одновременно | `WriterAndReader_RoundTripCatalog` |
| 360° без нормализации, IU/мм | `WriterAndReader_RoundTripCatalog` |
| отсутствие каталога / capability | `Reader_RejectsMissingCatalogFile`, `Reader_RejectsCatalogWithoutCapability`, `Reader_RejectsCapabilityWithoutCatalog` |
| отсутствующий/изменённый asset | `Reader_RejectsMissingAssetFile`, `Reader_RejectsModifiedAsset` |
| неверный SHA/размер | `Reader_RejectsWrongManifestAssetSha`, `Reader_RejectsWrongManifestAssetSize` |
| неверное расширение | `Reader_RejectsAssetExtensionMismatch` |
| `../`, absolute, `\`, `:` | `Reader_RejectsUnsafeAssetPath` |
| ссылка вне manifest / неиспользуемый asset | `Reader_RejectsAssetNotInManifest`, `Reader_RejectsUnreferencedAssetFile` |
| некорректный JSON / major version / enum | `Reader_RejectsCorruptCatalogJson`, `Reader_RejectsUnsupportedCatalogMajorVersion`, `Reader_RejectsUnknownEnumValue` |
| `componentId`, designator, ordinal, duplicate | `Reader_RejectsUnknownComponentId`, `Reader_RejectsDesignatorMismatch`, `Reader_RejectsDuplicateBodyOrdinal`, `Reader_RejectsDuplicateComponentId`, `Writer_RejectsCatalogMissingComponent` |
| `NaN`/`Infinity` | `Reader_RejectsNonFiniteNumber`, `Writer_RejectsNonFiniteIuMmPair` |
| IU/мм: несовпадение X/Y/Z/dz | `Reader_RejectsMismatchedIuMmValue` |
| IU/мм: граница допуска и null-пара | `Reader_AcceptsIuMmValueAtToleranceBoundary`, `Reader_RejectsIuMmValueOutsideTolerance`, `Reader_AcceptsNullIuMmPair`, `Reader_RejectsUnpairedIuMmValue` |
| Writer отклоняет неверную IU/мм пару | `Writer_RejectsMismatchedIuMmPair` |
| `assetPath` null/empty/whitespace и формат | `Reader_RejectsInvalidAssetPathFormatCombination` |
| обязательные nullable-поля JSON | `Reader_RejectsMissingNullableProperty`, `Reader_AcceptsExplicitNullNullableFields` |
| case-insensitive конфликт | `Reader_RejectsCaseInsensitivePathConflict` |
| потоковый большой файл | `WriterAndReader_SupportLargeStreamedAsset` |
| нет частичного пакета/временного каталога | `Writer_RejectsMissingAssetSource`, `Writer_RejectsUnreferencedAsset`, `Writer_RejectsCatalogMissingComponent` |
| нет перезаписи package-id | `Writer_DoesNotOverwriteExistingPackage` |
| изменение файла во время копирования | `Writer_RejectsAssetChangedDuringCopy` |
| детерминизм | `Writer_IsDeterministicWithModels`, `ComponentModelsJson_SortsComponentsAndBodies` |

## 6. Commit SHA и ссылка на PR

- Commit: `ef3b1b7` (`fix(core): tighten 3D catalog validation`) — R1 implementation
  head на момент обновления отчёта; `0c4d45f`, `de06851`, `8f96886`, `c050ebe` —
  предыдущие коммиты ветки.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/12, ветка
  `agent/BRIDGE-000013-core-3d-assets`, base `source`, merge запрещён.

## 7. История изменений

- 2026-11-25: реализован контракт каталога, Writer/Reader, потоковая обработка,
  тесты и документация. Первый commit `0c4d45f`; PR #12 открыт без merge.
- 2026-11-25: добавлен тест сортировки каталога (`8f96886`); duplicate/invalid
  asset keys больше не staging-уются (`c050ebe`).
- 2026-11-25 (R1): `ValidatePair` проверяет численное значение IU/мм; пустой
  `assetPath` отличается от `null`; nullable-поля JSON обязательны. Добавлены
  тесты; `ef3b1b7`.
