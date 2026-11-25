---
id: BRIDGE-000015
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T16:30:00+03:00
updated: 2026-11-25T19:50:03+03:00
base: source
branch: agent/BRIDGE-000015-solidworks-step-import
commit: 9055bba
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

Primary R1 result (`smoke-r26-minipc`, один SOLIDWORKS run, сборка с тем же
импортом/cleanup, что и финальный commit; отличался только счётчик lifecycle):

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

Revalidation на точном финальном commit не подтвердила one-run результат:

- `smoke-r29-minipc`: 141/155 assets, 1 037/1 055 refs. First failing asset —
  `assets/3d/f4d68f300a223d9a0eeaf66e3d6132f3a8bf1e0fedcca346306d4c1c6c7aeb67.step`;
  stage — импорт/сохранение native part, `SW-IMP-012 SOLIDWORKS import failed:
  COMException` после `SW-IMP-019` (restore preference). Последующие 14 assets
  получили `SW-IMP-020 Target open state could not be verified: COMException`.
  Cleanup: `Open documents could not be enumerated; no Bridge documents were
  closed`; final `createdDocumentsRemaining=0`, `baselinePreserved=True`.
- `smoke-r30-minipc`: 134/155 assets, 928/1 055 refs. First failing asset —
  `assets/3d/e733492ebe79e724dc4c04d30447c40cd38413531d7d49170a0adeef1f532d72.step`;
  stage — импорт/сохранение native part, `SW-IMP-012 SOLIDWORKS import failed:
  COMException`. Перед этим 77 Bridge-документов `AS0A823H8SB-7H-*.step.sldprt`
  не закрылись: `CloseDoc`/`QuitDoc` → `COMException 0x800706BA Сервер RPC
  недоступен`. Последующие 20 assets получили `SW-IMP-020 Target open state
  could not be verified: COMException`. Diagnostics: 132 Info, 226 Warning,
  21 Error. Lifecycle import: baseline 0, created 1 594, closed 1 517,
  remaining 77; финальная проверка `GetDocuments()` недоступна из-за RPC,
  `baselinePreserved=True`.

Попытка `smoke-r28-minipc` была остановлена внешним timeout на 1 800 s при
149/155 созданных native parts; result-файл не был записан. Ни r28, ни r29, ни
r30 не используются как substitute: в отчёте сохранён полный успешный one-run
`smoke-r26-minipc` и отдельно зафиксированы сбои ревалидации. Причина сбоев —
COM/RPC-отключение SOLIDWORKS в длинной сессии, не расхождение package и не
ошибка mapping.

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
- `git diff --check` — 0 ошибок (только informational LF→CRLF warnings).
- GitHub CI не запускался.

SolidWorks unit tests не требуют запущенного SOLIDWORKS: package read, missing
models, unique assets, dedup, NotAcquired, unsupported format, safe names,
missing/empty assets, `SelectAssets`, warning decode, path normalization,
no-overwrite guard, save-result guard.

## 11. Ограничения и риски

- SOLIDWORKS interop DLL и CAD-бинарники не входят в Git.
- `IModelDoc2.Close`/`Quit` недоступны в этой версии interop; cleanup использует
  `ISldWorks.CloseDoc`/`QuitDoc` и проверку через `GetDocuments()`.
- Длинная MiniPC-сессия нестабильна на стенде: ревалидации на финальном commit
  прервались COM/RPC-отключением после 141/155 (`smoke-r29-minipc`) и 134/155
  (`smoke-r30-minipc`) assets. В r30 77 Bridge-документов остались незакрытыми
  из-за недоступности RPC-сервера. Полный one-run успех зафиксирован на
  `smoke-r26-minipc` и принят оператором как proof для PR #14; риск COM/RPC
  сохраняется. Объединение прогонов запрещено.
- Реальное размещение ECAD-компонентов, `T_B_S`, Bottom, Euler, Add-in
  registration, Registry, installer, UI и двусторонняя синхронизация не
  реализованы.
- Assembly smoke не доказывает корректность ECAD-координат.
- Для assembly STEP сохраняется геометрия и body count; соответствие отдельных
  assembly instances/позиций не доказывается.

## 12. Commit и PR

- Commit: `9055bba` (`fix(solidworks): harden document lifecycle and native save guards`).
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
  run (`smoke-r26-minipc`). Ревалидации финального commit (`smoke-r29-minipc`,
  `smoke-r30-minipc`) зафиксировали COM/RPC-отключение после 141/155 и 134/155
  assets; вопрос вынесен на решение оператора.

## 14. Решение оператора

2026-11-25 выбран вариант 1: `smoke-r26-minipc` принят как proof для PR #14.
Основание: от `smoke-r26-minipc` до финального code commit `9055bba` менялся
только счётчик lifecycle; import/save/cleanup не менялись. Риск COM/RPC
нестабильности сохранён в разделе 11. Перед production-gate рекомендуется один
полный one-run на стабильном стенде. Объединять r26/r28/r29/r30 или подменять
неудачные прогоны частичными результатами запрещено. PR #14 остаётся открытым
и не слит.
