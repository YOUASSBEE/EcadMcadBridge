---
id: BRIDGE-000015
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T16:30:00+03:00
updated: 2026-11-25T22:00:00+03:00
base: source
branch: agent/BRIDGE-000015-solidworks-step-import
commit: TBD
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/14
related:
  - BRIDGE-000004
  - BRIDGE-000013
  - BRIDGE-000014
  - Q-004
---

# BRIDGE-000015 — SOLIDWORKS STEP import backend (R1)

## 1. Итог

Добавлен и доведён до R1 отдельный SOLIDWORKS backend:

- читает Core Exchange Package через `ExchangePackageReader`;
- строит детерминированный план уникальных STEP assets и body references;
- импортирует STEP AP214 через typed SOLIDWORKS API;
- сохраняет native `.SLDPRT` и проверяет повторное открытие/body count;
- сохраняет связь `ComponentId → BodyOrdinal → AssetPath → SLDPRT`;
- проверяет assembly API на двух native parts;
- имеет отдельный STA/ROT smoke harness.

R1 исправлен жизненный цикл документов и проверки SaveAs:

- baseline всех открытых документов снимается через `ISldWorks.GetDocuments()`;
- пользовательские документы, включая STEP assembly components и документы с
  `swFileLoadWarning_AlreadyOpen`, не закрываются;
- закрываются только документы, появившиеся после baseline, с повторным чтением
  текущего `GetTitle()`/`GetPathName()` после `SaveAs3`;
- успех cleanup подтверждается повторным `GetDocuments()`, а не возвратом API;
- assembly→part выполняется через временную настройку
  `swSaveAssemblyAsPartOptions = swSaveAsmAsPart_AllComponents` с restores в
  `finally`; отказ restore — Error;
- существующий/open target `.SLDPRT`/`.SLDASM` отклоняется без записи и удаления;
- `SaveAs3` считается успешным только при `true`, `Errors=0`, существующем файле
  ненулевого размера;
- warning-биты декодируются по именам, `128` — известный warning, а не ошибка;
- временный output и уникальные копии для повторного открытия устраняют отказ
  SOLIDWORKS на совпадающие basename.

Это backend/прототип, не SOLIDWORKS Add-in. Add-in registration, Registry,
installer, UI, ECAD placement и `T_B_S` не реализованы.

## 2. Окружение

| Параметр | Значение |
|---|---|
| SOLIDWORKS | 2026 SP3.2, `34.3.2.0003` (revision `34.3.2`, build `sw2026_SP03/d260702.008/`) |
| SOLIDWORKS interop | `SolidWorks.Interop.sldworks` / `swconst` 34.3.2.3 |
| .NET SDK | 10 |
| TFM backend/smoke/tests | `net10.0-windows`, x64 |
| Core | `net10.0` |
| Package Reader | Core `ExchangePackageReader` |

Interop подключается локально через `SolidWorksApiDir` (`Private=false`); DLL не
коммитятся и не добавляются NuGet-пакетом.

## 3. Изменённые файлы

```text
src/EcadMcadBridge.SolidWorks/
  Importing/SolidWorksImportPlan.cs
  Importing/SolidWorksImportService.cs
  Assemblies/SolidWorksAssemblyService.cs
  Interop/SolidWorksConnection.cs
  Interop/SolidWorksDocumentTracker.cs
  Interop/SolidWorksFileGuard.cs
  Interop/SolidWorksPath.cs
  Interop/SolidWorksWarningDecoder.cs
tools/EcadMcadBridge.SolidWorks.Smoke/Program.cs
tests/EcadMcadBridge.SolidWorks.Tests/
  EcadMcadBridge.SolidWorks.Tests.csproj
  SolidWorksImportPlanTests.cs
  SolidWorksInteropTests.cs
.agent/AGENTS.md
.agent/ARCHITECTURE.md
.agent/DECISIONS.md
.agent/DEVELOPMENT_RULES.md
.agent/PROJECT_CONTEXT.md
.agent/SDK_API_LEDGER.md
.agent/reports/BRIDGE-000015-report.md
```

Core schema, Altium adapter, Registry, Add-in registration, placement, `T_B_S`,
Bottom и Euler не изменены.

## 4. Package plan

`SolidWorksImportPlanBuilder`:

- проверяет `PackageReadResult.IsSuccess`, snapshot и models;
- собирает уникальные `AssetPath` и все body references;
- NotAcquired bodies (`AssetPath=null`) пропускает с warning;
- поддерживает только `step-ap214`; другой формат — ошибка;
- проверяет безопасный относительный путь, существование и ненулевой размер asset;
- вычисляет native path как `assets/3d/<sha256>.step` → `<sha256>.SLDPRT`;
- один content-addressed asset импортируется один раз; несколько body references
  используют одну native part.

## 5. Import pipeline и жизненный цикл

- `GetImportFileData` → `IImportStepData.MapConfigurationData=false`.
- `LoadFile4`; фактический тип может быть `swDocPART` или `swDocASSEMBLY`.
- `PartDoc.GetBodies2(swSolidBody,false)` для part; для assembly — сумма solid
  bodies component instances.
- До импорта target проверяется на существование/open состояние; существующий
  файл не перезаписывается и не удаляется.
- `SaveAs3(swSaveAsCurrentVersion, swSaveAsOptions_Silent)` пишет сначала во
  временный `.SLDPRT` в выходном каталоге; после успешной проверки и закрытия
  Bridge-документов файл перемещается в content-addressed native path.
- Для повторного открытия создаётся уникальная байтовая копия: SOLIDWORKS
  отклоняет второй документ с тем же basename, а пользовательские документы
  не должны закрываться. Body count сравнивается с импортированным значением.
- Assembly STEP сохраняется как native part через временную настройку
  `swSaveAssemblyAsPartOptions`; исходное значение восстанавливается в `finally`.
- `SolidWorksDocumentTracker` закрывает только новые документы, используя
  актуальные имена; счётчики `Created/Closed/Remaining` фиксируются в lifecycle.
- Диагностика warnings использует `SolidWorksWarningDecoder`; 128 decode как
  `swFileSaveWarning_AnimatorCameraViews` или `swFileLoadWarning_AlreadyOpen`.

## 6. Assembly smoke

`SolidWorksAssemblyService`:

1. `NewDocument` по явному `.asmdot` шаблону;
2. уникальные временные копии native parts загружаются в память через `OpenDoc6`;
3. assembly активируется, `AddComponent5(...,0,0,0)`;
4. `GetComponents` count;
5. `SaveAs3` `.SLDASM`, close, `OpenDoc6`, повторный count.

`AddComponent5(0,0,0)` — только API smoke, не ECAD placement.

## 7. Smoke A — representative STEP

Package: `3c363021-5b00-4cde-ac20-c33ad383b20a` (multi, 73 components,
152 body refs, 152 unique assets).

Выбраны 3 representative assets:

| Body | Representation/source | Imported bodies | Reopened | Save/reopen |
|---|---|---|---:|---|
| R14/0 | Native/Native | 1 | 1 | OK |
| R14/1 | Native/Native | 1 | 1 | OK |
| L1/0 | Generic/Embedded | 3 | 3 | OK |

R1 revalidation (`smoke-r27-baseline`):

- imported 3/3 assets, mapped 3/3 body refs, assembly 2/2 components;
- `success=True`, `baselinePreserved=True`, `createdDocumentsRemaining=0`;
- пользовательский `.SLDPRT` был открыт до smoke и остался открытым;
- lifecycle import: baseline 1, created 16, closed 16, remaining 0;
- lifecycle assembly: baseline 1, created 6, closed 6, remaining 0;
- 0 warnings/errors в диагностике, все SW-IMP-019 сообщения — Info о restore
  preference;
- входной package и исходные STEP assets не изменялись.

Проверка отказа (`smoke-r25-error`): в целевой native path заранее записан
sentinel; smoke завершился `SW-IMP-020 Target file already exists`, файл не
перезаписан (SHA-256 до/после совпал), Bridge-документов не осталось.

## 8. Smoke B — MiniPC / полный один run

Package: `b9a6f099-7866-417c-b4f4-17f8265af3ef` (MiniPC, 1 046 components,
1 055 body refs, 155 unique STEP assets).

R1 result (`smoke-r26-minipc`, один SOLIDWORKS run):

- `success=True`;
- 155/155 unique assets imported; 155/155 native parts reopened;
- 1 055/1 055 body refs mapped;
- 1 629 imported solid bodies, 1 629 reopened solid bodies;
- J20 asset `6b5d104ed…5f04.step`: 395 imported, 395 reopened, `SaveErrors=0`,
  `OpenErrors=0`;
- `bodyCountMatches=155`;
- `baselineDocuments=0`, `remainingDocuments=0`, `createdDocumentsRemaining=0`,
  `baselinePreserved=True`;
- warnings/errors: 0; 151 Info `SW-IMP-019` о восстановлении preference;
- import 1 870 407 ms, полный smoke 1 883 118 ms;
- входной package не изменялся; source assets не редактировались.

Предыдущий baseline отчёт фиксировал split-run (154/155 + isolated retry); R1
устранил причину и полный MiniPC импорт подтверждён в одном run без substitute.

## 9. API ledger

Подтверждены S-001…S-015 в `.agent/SDK_API_LEDGER.md`:
`GetImportFileData`/`MapConfigurationData`, `LoadFile4`,
`PartDoc.GetBodies2`, `ModelDocExtension.SaveAs3`, `OpenDoc6`,
`NewDocument`, `AddComponent5`, `GetComponents`, `ActivateDoc3`/`CloseDoc`,
`RevisionNumber`/`GetBuildNumbers2`, `GetDocuments`, `GetTitle`/`GetPathName`/
`GetType`, preference set/restore, `GetOpenDocument`, warning decode.

`IModelDoc2.Close`/`Quit` в прямом probe дали `NotImplementedException
0x80004001`; production cleanup использует `ActivateDoc3`/`CloseDoc` с fallback
`QuitDoc` и проверяет результат через `GetDocuments()`. Отдельный успех `QuitDoc`
не изолирован. `ISwAddin`/`ConnectToSW`/`DisconnectFromSW` остаются
неподтверждёнными в хосте.

## 10. Tests / build

- `dotnet build -c Release` — 0 warnings, 0 errors.
- `dotnet test -c Release --no-build`:
  - Core 140/140;
  - Altium 82/82;
  - SolidWorks 16/16;
  - всего 238/238.
- `git diff --check` — см. финальную запись истории.
- GitHub CI не запускался.

SolidWorks unit tests не требуют запущенного SOLIDWORKS: package read, missing
models, unique assets, dedup, NotAcquired, unsupported format, safe names,
missing/empty assets, `SelectAssets`, warning decode, path normalization,
no-overwrite guard, save-result guard.

## 11. Ограничения и риски

- SOLIDWORKS interop DLL и CAD-бинарники не входят в Git.
- `IModelDoc2.Close`/`Quit` недоступны в этой версии interop; cleanup использует
  `ISldWorks.CloseDoc`/`QuitDoc` и проверку через `GetDocuments()`.
- Реальное размещение ECAD-компонентов, `T_B_S`, Bottom, Euler, Add-in
  registration, Registry, installer, UI и двусторонняя синхронизация не
  реализованы.
- Assembly smoke не доказывает корректность ECAD-координат.
- Для assembly STEP сохраняется геометрия и body count; соответствие отдельных
  assembly instances/позиций не доказывается.

## 12. Commit и PR

- Commit: `TBD`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/14, ветка
  `agent/BRIDGE-000015-solidworks-step-import`, base `source`, merge запрещён.
- PR #13/#9/#10/#11 не изменялись.

## 13. История изменений

- 2026-11-25: создан SolidWorks backend, plan/import/assembly smoke, tests,
  S-001…S-010, D-020, документация и smoke. Первый commit `476e318`; PR #14
  открыт без merge.
- 2026-11-25 (R1): исправлены document lifecycle/cleanup, assembly-as-part
  preference restore, no-overwrite/save guards, decode warnings; добавлены
  `SolidWorksDocumentTracker`/`SolidWorksFileGuard`/`SolidWorksPath`/
  `SolidWorksWarningDecoder`, S-011…S-015; подтверждены representative smoke с
  пользовательскими документами, forced-error sentinel и полный MiniPC в одном
  run.
