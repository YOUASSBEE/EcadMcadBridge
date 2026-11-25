---
id: BRIDGE-000015
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T16:30:00+03:00
updated: 2026-11-25T16:30:00+03:00
base: source
branch: agent/BRIDGE-000015-solidworks-step-import
commit: 476e318
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/14
related:
  - BRIDGE-000004
  - BRIDGE-000013
  - BRIDGE-000014
  - Q-004
---

# BRIDGE-000015 — SOLIDWORKS STEP import backend

## 1. Итог

Добавлен отдельный SOLIDWORKS backend:

- читает Core Exchange Package через `ExchangePackageReader`;
- строит детерминированный план уникальных STEP assets и body references;
- импортирует STEP AP214 через typed SOLIDWORKS API;
- сохраняет native `.SLDPRT` и проверяет повторное открытие/body count;
- сохраняет связь `ComponentId → BodyOrdinal → AssetPath → SLDPRT`;
- проверяет assembly API на двух native parts;
- имеет отдельный STA/ROT smoke harness.

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

## 3. Проекты

```text
src/EcadMcadBridge.SolidWorks/
  Importing/SolidWorksImportPlan.cs
  Importing/SolidWorksImportService.cs
  Assemblies/SolidWorksAssemblyService.cs
  Interop/SolidWorksConnection.cs
tools/EcadMcadBridge.SolidWorks.Smoke/
tests/EcadMcadBridge.SolidWorks.Tests/
```

`SolidWorksConnection` подключается к уже запущенному SOLIDWORKS через ROT
(`GetActiveObject`), не запускает и не завершает пользовательский процесс.
Smoke `[STAThread]`; COM-вызовы последовательные, без `Task.Run`/`Parallel`.

## 4. Package plan

`SolidWorksImportPlanBuilder`:

- проверяет `PackageReadResult.IsSuccess`, snapshot и models;
- собирает уникальные `AssetPath` и все body references;
- NotAcquired bodies (`AssetPath=null`) пропускает с warning;
- поддерживает только `step-ap214`; другой формат — ошибка;
- проверяет безопасный относительный путь, существование и ненулевой размер
  asset;
- вычисляет native path как `assets/3d/<sha256>.step` → `<sha256>.SLDPRT`
  (без designator в имени);
- один content-addressed asset импортируется один раз; несколько body
  references используют одну native part.

## 5. Import pipeline

- `GetImportFileData` → `IImportStepData.MapConfigurationData=false`.
- `LoadFile4`; фактический тип может быть `swDocPART` или `swDocASSEMBLY`.
- `PartDoc.GetBodies2(swSolidBody,false)` для part; для assembly — сумма solid
  bodies component instances.
- импортированный документ активируется, сохраняется через
  `ModelDocExtension.SaveAs3(swSaveAsCurrentVersion, swSaveAsOptions_Silent)`.
  Assembly STEP сохраняется как native `.SLDPRT`; повторное открытие проверяет
  body count.
- ошибки/пустой файл → asset не считается созданным; body entry остаётся без
  native part.
- Созданные документы закрываются в `finally`; пользовательские документы не
  закрываются.

## 6. Assembly smoke

`SolidWorksAssemblyService`:

1. `NewDocument` по явному `.asmdot` шаблону;
2. native parts загружаются в память через `OpenDoc6`;
3. assembly активируется, `AddComponent5(...,0,0,0)`;
4. `GetComponents` count;
5. `SaveAs3` `.SLDASM`, close, `OpenDoc6`, повторный count.

`AddComponent5(0,0,0)` — только API smoke, не ECAD placement.

## 7. Smoke A — representative STEP

Package: `3c363021-5b00-4cde-ac20-c33ad383b20a` (multi, 73 components,
152 body refs, 152 unique assets).

Выбраны 3 representative assets:

| Body | Representation/source | Imported bodies | Reopened | Save/reopen |
|---|---|---:|---|---|
| R14/0 | Native/Native | 1 | 1 | OK |
| R14/1 | Native/Native | 1 | 1 | OK |
| L1/0 | Generic/Embedded | 3 | 3 | OK |

- Импортировано 3/3 assets, сопоставлено 3/3 body refs.
- Импорт — 25 248 ms; полный smoke — 32 481 ms.
- L1 STEP импортирован как assembly и сохранён как native part с 3 solid
  bodies; R14 assets — по одному body.
- Assembly: 2 components, после SaveAs/reopen — 2 components; `Saved=True`,
  `Reopened=True`, errors=0, warnings=128 (SOLIDWORKS save warning bitmask;
  не ошибка).
- Входной package не изменялся; source assets не редактировались.

## 8. Smoke B — MiniPC / dedup

Package: `b9a6f099-7866-417c-b4f4-17f8265af3ef` (MiniPC, 1 046 components,
1 055 body refs, 155 unique STEP assets).

Фактический результат:

- bulk run: 154/155 unique assets imported, 1 054/1 055 body refs mapped;
  один asset `6b5d…f04.step` (J20, 395 solid bodies) не создал SLDPRT в общем
  run (`SW-IMP-009`).
- isolated retry того же asset: 1/1 imported, 395/395 solid bodies, J20/0
  mapped, SaveAs/reopen OK.
- Объединение двух прогонов покрывает 155/155 unique assets и 1 055/1 055 body
  refs; content-addressed dedup сохранён.
- Bulk run: 1 521 081 ms; isolated: 127 786 ms.
- Отсутствуют потерянные body refs; все 155 уникальных assets имеют native
  `.SLDPRT`.

Причина split-run: длительная сессия SOLIDWORKS с большим числом импортов
нестабильна; isolated retry подтверждает, что asset и импортный pipeline
корректны. Это ограничение стенда, не отличие package.

## 9. API ledger

Подтверждены S-001…S-010 в `.agent/SDK_API_LEDGER.md`:
`GetImportFileData`/`MapConfigurationData`, `LoadFile4`,
`PartDoc.GetBodies2`, `ModelDocExtension.SaveAs3`, `OpenDoc6`,
`NewDocument`, `AddComponent5`, `GetComponents`, `ActivateDoc3`/`CloseDoc`,
`RevisionNumber`/`GetBuildNumbers2`. `InsertPart3` исследован, но в финальном
backend не используется.

`ISwAddin`/`ConnectToSW`/`DisconnectFromSW` остаются неподтверждёнными в хосте.

## 10. Tests / build

- `dotnet build -c Release` — 0 warnings, 0 errors.
- `dotnet test -c Release`:
  - Core 140/140;
  - Altium 82/82;
  - SolidWorks 9/9;
  - всего 231/231.
- `git diff --check` — OK.
- GitHub CI не запускался.

SolidWorks unit tests не требуют запущенного SOLIDWORKS: package read, missing
models, unique assets, dedup, NotAcquired, unsupported format, safe names,
missing/empty assets, `SelectAssets`.

## 11. Ограничения

- Bulk MiniPC import потребовал isolated retry для одного 395-body asset;
  полный один run нестабилен на текущей сессии SOLIDWORKS.
- Assembly warnings=128 — это save warning bitmask, не ошибка; детализация
  warning-кодов не сохранена.
- STEP, импортированный как assembly, сохраняется как native part; body count
  сравнивается, но соответствие отдельных assembly instances/позиций не
  доказывается.
- Dedup выполнен по `AssetPath`; native file имена — SHA basename.
- Реальное размещение ECAD-компонентов, `T_B_S`, Bottom, Euler, Add-in
  registration, Registry, installer, UI и двусторонняя синхронизация не
  реализованы.
- SOLIDWORKS interop DLL и CAD-бинарники не входят в Git.

## 12. Commit и PR

- Commit: `476e318` (`feat(solidworks): add STEP import backend and smoke harness`).
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/14, ветка
  `agent/BRIDGE-000015-solidworks-step-import`, base `source`, merge запрещён.
- Final branch HEAD after PR/report updates: `476e318da34fc16237e1667e51a738610ade76c6`.

## 13. История изменений

- 2026-11-25: создан SolidWorks backend, plan/import/assembly smoke, tests,
  S-001…S-010, D-020, документация и smoke. Первый commit `476e318`; PR #14
  открыт без merge.
