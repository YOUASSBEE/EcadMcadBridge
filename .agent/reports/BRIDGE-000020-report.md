---
id: BRIDGE-000020
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T13:25:00+03:00
updated: 2026-11-26T14:45:00+03:00
base: source
branch: agent/BRIDGE-000020-board-outline-completeness
commit: 75e7779fdbca55062563c34dfbf15e455a6480a2
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/19
related: [BRIDGE-000020, BRIDGE-000019, BRIDGE-000018]
---

# BRIDGE-000020 — полная проверка контура платы и вырезов

## 1. Итог

Реализована и проверена в CAD-хостах полная цепочка доказательства геометрии платы:

- Altium извлекает Outer/Hole и все источники `eRegionKind_BoardCutout`, классифицирует
  их по геометрии и вычисляет `board.outline.completeness`.
- Core хранит completeness и origin платы, сериализует их и запрещает
  `board-3d-model` для package с неполным или неизвестным контуром (`EMB-PKG-031`).
- SOLIDWORKS сравнивает STEP/native part с контуром в board-local frame, проверяет
  каждый solid body, каждую Outer/Hole control point и строго читает tessellation.
- Smoke gating требует `outlineComplete=true`, поэтому desktop cutout board больше
  не проходит с частичным/непроверенным контуром.

Runtime-результат на копии `C:\Users\Anton\Desktop\board with cutout.PcbDoc`:
`success=true`, `outlineCompleteness=Complete`, 1 Outer + 2 Hole probes, 3 solid body,
save/reopen, transforms и cleanup `Verified`. MiniPC smoke: `success=true`, 21/21
occurrences, cleanup `Verified`. PR не сливался; `source` не изменялся.

Отдельного task-файла `.agent/tasks/BRIDGE-000020.md` в репозитории нет; scope взят
из постановки Оператора и зафиксирован в этом отчёте.

## 2. Изменённые файлы

| Файл | Изменение |
|---|---|
| `src/EcadMcadBridge.Core/Model/Board.cs`, `Model/Values.cs` | `OutlineCompleteness`, `BoardCutoutRole`, `BoardOutline.Completeness`, `BoardProperties.OriginX/Y` |
| `src/EcadMcadBridge.Core/Model/BoardOutlineTopology.cs` (новый) | геометрическая выборка дуг, отсечение по box, площадь, inside, классификация region-контуров |
| `src/EcadMcadBridge.Core/Serialization/BoardSnapshotJson.cs` | `completeness` и `originX/originY` в JSON; старый package без поля → `Unknown` |
| `src/EcadMcadBridge.Core/Packaging/ExchangePackageReader.cs`, `ExchangePackageWriter.cs`, `PackageDiagnostic.cs` | `board-3d-model` только при `Complete`; код `EMB-PKG-031` |
| `src/EcadMcadBridge.Altium/Adapter/AltiumOutlineReader.cs` | полнота, provenance, классификация Board Cutout, arc sweep из geometric traversal, sagitta-derived tolerance |
| `src/EcadMcadBridge.Altium/Adapter/OutlineContourMatcher.cs` | точное совпадение с учётом sample-сагитты дуг; отказ от грубой замены дуги двумя точками |
| `src/EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter.cs` | чтение origin через C-121; расширенные счётчики и сверка всех cutout source |
| `src/EcadMcadBridge.SolidWorks/Importing/SolidWorksBoardGeometryInspector.cs` | frame normalization, per-body box/Z/volume, строгая tessellation, probe по каждому Outer/Hole с точным `ContourId` |
| `src/EcadMcadBridge.SolidWorks/Importing/TriangleMesh.cs` | append нескольких body mesh |
| `tools/EcadMcadBridge.SolidWorks.Smoke/Program.cs` | поля `boardModelRequested`, `outlineCompleteness`, `outlineComplete` и gating `success` |
| `tests/EcadMcadBridge.Core.Tests/BoardOutlineTopologyTests.cs` (новый), `BoardModelPackageTests.cs`, `BoardSnapshotJsonTests.cs`, `BoardSnapshotFixture.cs` | синтетические тесты topology, writer/reader gate, legacy `Unknown` |
| `tests/EcadMcadBridge.Altium.Tests/ArcExactMatchTests.cs` |-large-radius sampled arc и отказ coarse replacement |
| `tests/EcadMcadBridge.SolidWorks.Tests/SolidWorksBoardGeometryInspectorTests.cs`, `BoardPlacementTransformTests.cs`, `PlanTestFixture.cs` | строгая mesh-ингестия, probe height, fin count, несколько Outer |
| `.agent/ARCHITECTURE.md`, `.agent/DEVELOPMENT_RULES.md`, `.agent/PROJECT_CONTEXT.md`, `.agent/SDK_API_LEDGER.md`, `AGENTS.md`, `README.md` | актуальный контракт, правила, этап и реестр C-121 |
| `.agent/reports/BRIDGE-000020-report.md` (этот файл) | отчёт |

В Git не добавлены PCB, STEP, SLDPRT/SLDASM, DLL, лицензии, данные заказчика или
временные smoke-артефакты.

## 3. Принятые решения и обоснование

1. **Completeness.fail-closed.** `Complete` выставляется только если exact Outer,
   все Board Cutout-контуры и все clip-циклы учтены, а счётчики errors/unmatched/
   ambiguous/partial/fallback/omitted равны нулю. Всё остальное — `Incomplete`;
  package без поля — `Unknown`. `board-3d-model` требует `Complete` и при записи,
   и при чтении. Это устраняет ситуацию BRIDGE-000019, когда `success=false` от
   partial outline не был частью контракта пакета.
2. **Board Cutout — доказательство, а не готовый Hole.** Пять region-контуров на тестовой
   плате дали 3 board-shape и 2 hole; 2 clip-цикла сопоставлены с region источниками
   один-в-один, дубликатов и неклассифицированных контуров нет. Сопоставление
   выполняется по геометрии пакета, без выдуманных координат и без совпадения по
   центрам/generic bounds.
3. **Дуги.** Направление дуги выводится из geometric contour; tolerance для exact
   matching вычисляется из измеренной chord sagitta и требует достаточно плотной
   sample-цепочки. Крупная дуга AD принимается, а замена дуги двумя далёкими
   точками по-прежнему отклоняется.
4. **Frame.** `GetState_XOrigin/YOrigin` (C-121) сохраняются парой IU/мм. STEP body
   boxes сравниваются с outline только после добавления origin. Причина старого
   остатка `34.4 mm` — frame mismatch, а не реальная разница геометрии; масштаб,
   подстановка границ и выравнивание по центру не применялись.
5. **Per-body expectation.** Каждый imported solid body использует свою измеренную
   Z-толщину; площадь Outer минус Hole отсекается measured box этого body. Пороги
   `SW-BRD-008` (0.05 mm) и `SW-BRD-010` (`max(5 mm³, 2%)`) не изменились.
6. **Tessellation.fail-closed.** Любая ошибка `GetTessellation`, `null`,
   `Tessellate()==false`, отсутствие triangle, неверный vertex/fin/facet/index —
   Error; частичный mesh не используется. Несколько Outer дают независимые probes
   с их собственными `ContourId` (`SW-BRD-019`).
7. **Архитектура не менялась.** Formal ADR не потребовался: добавлены контрактные
   поля и геометрические проверки внутри существующих компонентов. Зависимости
   остались: Core → BCL, Altium → Core + Altium SDK, SOLIDWORKS → Core + interop.

## 4. Неподтверждённые вопросы и риски

- Stage A `inventory.json` был удалён последующим запуском `run-export-b.ps1`
  (значение `ResultName` по умолчанию). Сохранённый SHA-256 и полные счётчики из
  `ADAPTER_SUMMARY` приведены ниже; после изменения алгоритма probe-инвентаризация
  намеренно не повторялась.
- Аналитические дуги region-полигонов по-прежнему представляются как line segments из
  `GetGeometricPolygon()` (уже существующий Q-010). В отчёте не утверждается
  analytic-точность region arcs.
- `SW-BRD-010` проверяет суммарный объём платы; per-body expected/measured объёмы
   и остатки выводятся в JSON для ревью, но не являются отдельным gate. На тестовой
   плате суммарный остаток укладывается в неизменённый 2%-ный допуск.
- `SW-BRD-005` Warning о трёх solid body ожидаем: STEP cutout board consists of
  two lobes and a thin strip; они импортируются в один native substrate part.
- Полноценный SOLIDWORKS Add-in host, Registry, installer и persistent CAD
  preferences не изменялись и не входили в scope.
- В репозитории нет task-файла BRIDGE-000020; приёмка должна опираться на этот
  отчёт и на PR-описание.
- Smoke-тесты выполнялись локально на стенде Оператора; расхождение с другим
  стендом возможно только при иной версии CAD/interop, что фиксируется в JSON.

## 5. Проверки

### 5.1 Среда и база

- Base `source` до начала и после работы: `7b1efa6dcb0fd1e4e96803b08a63fefa9255c8ff`.
- Branch: `agent/BRIDGE-000020-board-outline-completeness`.
- Implementation commit: `7aab96a18ab3f5e1a9fc43275fb66c576cdba303`.
- Altium Designer 26.12.2.9; SOLIDWORKS 2026 SP3.2, revision `34.3.2`,
  build `sw2026_SP03/d260702.008/`.
- Финальная установленная в AD test extension сборка совпадает с Release build:
  `EcadMcadBridge.dll` SHA-256 `65C37A9C78B6D1DAFB5277516107B0B16ACFD5E3F9CE0E86A057E383FEDC779F`,
  `EcadMcadBridge.Core.dll` SHA-256 `B21829204916D324BB54AF038A7C1AB2B24F46EBE6C79345D99FDEAE28B98E19`.
- `dotnet build -c Release`: 0 warnings, 0 errors.
- `dotnet test -c Release`: Core 229/229, Altium 184/184, SOLIDWORKS 94/94,
  всего 507/507, 0 failures.
- `git diff --check`: без ошибок.

### 5.2 Stage A — инвентаризация до изменения алгоритма

Копия: `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\board-with-cutout.PcbDoc`.
SHA-256 исходного и копии до экспорта:
`ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753`.

Записанный `inventory.json` SHA-256:
`6A64017A6256341AF1DD47ED906F2A5A21AB1D7847D58B2A89CDBD8F93C7433E`.

Ключевые счётчики `ADAPTER_SUMMARY` (2026-11-26T09:25:47Z):

```text
outlineExact=0 outlineFallback=1 outlineOmitted=0 outlineLoopErrors=0
outlineUnmatchedLoops=1 outlineAmbiguous=0 outlinePartial=1
geometricContours=1 geometricPoints=140 geometricHoles=0
boardCutoutRegions=5 boardCutoutContours=5 boardCutoutHoles=2
boardCutoutShapes=3 boardCutoutUnclassified=0
clipCutouts=2 clipCutoutContours=2 clipCutoutSegments=16 clipCutoutErrors=0
clipMatchedRegions=2 clipUnmatchedRegions=0 clipAmbiguousRegions=0
duplicateCutoutContours=0 exportedCutoutContours=2
```

Топология: 1 Outer, 2 реальных Hole contour; region r5/r6/r7 — board-shape,
r0/r1 — hole. Предварительные оценки инвентаризации: board area
`1378.9894678932255 mm²`, ожидаемая полная area `1359.88 mm²` и volume
`1083.14487678 mm³`.

Разложение STEP: top lobe x `-7.41..18.61`, y `18..43`, z `0..0.7965`;
bottom lobe x `-7.41..18.61`, y `-12..13`, z `0..0.7965`; strip
x `-34.4..45.6`, y `13..18`, z `0..0.0845`. Остаток `34.4 mm` в
`SW-BRD-008` и несовпадение `SW-BRD-010` в baseline объяснены frame mismatch
и тонким третьим телом, а не отсутствием вырезов.

Baseline package cutout: `ef26ea5d-8aee-4d7d-99d4-28aac362caab`,
board STEP SHA `FFF15249CE86FE87A58207206D3890C285BDFBBE576AECDEC378A37E2610A4465`,
`smoke-b4` diagnostics `success=false` из-за partial outline. Baseline MiniPC:
package `1804735d-7c31-47cb-98e3-e50950283c2e`, `smoke-a5` 21/21, cleanup
`Verified`.

### 5.3 AD26 runtime после исправления

Package: `98c67b80-77c4-4f79-bcbf-392583db55bf`, каталог
`C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\packages-b\98c67b80-77c4-4f79-bcbf-392583db55bf`.

`ADAPTER_SUMMARY` (2026-11-26T09:52:48Z):

```text
outlineExact=1 outlineFallback=0 outlineOmitted=0 outlineLoopErrors=0
outlineUnmatchedLoops=0 outlineAmbiguous=0 outlinePartial=0
boardCutoutRegions=5 boardCutoutContours=5 boardCutoutHoles=2
boardCutoutShapes=3 boardCutoutUnclassified=0
clipCutouts=2 clipCutoutContours=2 clipCutoutSegments=16 clipCutoutErrors=0
clipMatchedRegions=2 clipUnmatchedRegions=0 clipAmbiguousRegions=0
duplicateCutoutContours=0 exportedCutoutContours=2
```

`PACKAGE_EXPORT_OK addin=0.4.0 cad=26 doc=board-with-cutout.PcbDoc
packageId=98c67b80-77c4-4f79-bcbf-392583db55bf layers=17 components=73
pads=224 nets=57 bodies=152 stepAssets=152 exportFailures=0 ...
boardStepSize=232025
boardStepSha=06ef126dcd072d4be2a6d0716a2e0a76e8b3599729cabf3d5ec69248913398e0
adapterWarnings=14 modelWarnings=0 packageWarnings=28`.

Snapshot `board.snapshot.json` SHA-256:
`FCF8692AFB5D4766AB7445DA6687B1A0D5565EA732A781229926B1523DCAED45`;
`board.outline.completeness=complete`. Контуры:

| ContourId | Role | Сегменты |
|---|---|---|
| `altium:contour:0` | Outer | 12 (2 arc) |
| `altium:contour:cutout:clip:1` | Hole | 7 line |
| `altium:contour:cutout:clip:2` | Hole | 9 (3 arc) |

Origin: `originXMm=1533.77500204`, `originYMm=986.6999976399999`;
capability `board-3d-model` присутствует в manifest.

MiniPC export: package `4a4fb429-c8b5-434f-8653-fd60de329fd6`,
`outlineExact=1`, 1 Outer, 1 board-shape region, `clipCutouts=0`,
`exportedCutoutContours=0`; board STEP SHA
`5D94B900B4F5F60AA23D237E597167E10C86003E22A433A03A558201AC7A29AA`.
SHA-256 `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000019\minipc.PcbDoc`
до и после экспорта: `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93`.

### 5.4 Desktop Smoke B — cutout board

Команда:

```powershell
dotnet tools\EcadMcadBridge.SolidWorks.Smoke\bin\Release\net10.0-windows\EcadMcadBridge.SolidWorks.Smoke.dll `
  --package 'C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\packages-b\98c67b80-77c4-4f79-bcbf-392583db55bf' `
  --output 'C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\smoke-b9' `
  --placed-assembly --template 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gost-assy.asmdot' `
  --max-components 1
```

`smoke-b9\smoke-result.json` SHA-256:
`99E4437A4A089E77914F132252C431D3353EF25AEBBF1BA8AD4B72BDA8F335FE`.
`success=true`, `boardModelRequested=true`, `outlineCompleteness=Complete`,
`outlineComplete=true`, `geometryValid=true`.

| Проверка | Значение | Допуск |
|---|---|---|
| `SW-BRD-008` XY residual | `2.9000002541579306e-06 mm` | `0.05 mm` |
| Z top residual | `0 mm` | `0.05 mm` |
| Thickness delta | `1.1102230246251565e-16 mm` | измеренная `0.79649574 mm`, не nominal |
| `SW-BRD-010` volume | measured `848.1995991811364 mm³`, expected `841.2919500463648 mm³`, residual `6.907649134771532 mm³` | `max(5, 2%) = 16.825839 mm³` |

Per-body измерения:

| Body | Область | Thickness mm | Measured mm³ | Expected mm³ | Residual mm³ | Triangles |
|---|---|---:|---:|---:|---:|---:|
| 1 strip | `y=999.7..1004.7` | `0.08449818` | `27.8843994` | `27.88441127` | `-1.19e-05` | 44 |
| 2 lobe | `y=1004.7..1029.7` | `0.79649574` | `402.5628560` | `399.09730701` | `3.46554498` | 392 |
| 3 lobe | `y=974.7..999.7` | `0.79649574` | `417.7523438` | `414.31023176` | `3.44211202` | 260 |

Probes (все совпадают):

- `board`, `altium:contour:0`, expected `true`, actual `true`;
- `cutout`, `altium:contour:cutout:clip:1`, expected `false`, actual `false`;
- `cutout`, `altium:contour:cutout:clip:2`, expected `false`, actual `false`.

Единственная geometry diagnostics — ожидаемый Warning `SW-BRD-005` о 3 solid body
в одном substrate part. Error-диагностик нет.

Placed assembly:
`smoke-b9\assembly\smoke-placed-assembly.SLDASM`, SHA-256
`68CDB3A5BD231A4CC3569E3BED2CA94912F04C937D1FBBB606867904C2DF8FCD`;
2/2 occurrences, save/reopen=true, transformsVerified=true; board occurrence
`board-1` fixed=true, verification=true, translation residual `0`, rotation basis
residual `0`; cleanup import `Verified`, placedAssembly `Verified`,
createdDocumentsRemaining=0. SHA-256 исходного и рабочей копии `.PcbDoc` после
smoke: `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753`.

### 5.5 MiniPC Smoke A

Команда:

```powershell
dotnet tools\EcadMcadBridge.SolidWorks.Smoke\bin\Release\net10.0-windows\EcadMcadBridge.SolidWorks.Smoke.dll `
  --package 'C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\packages-minipc\4a4fb429-c8b5-434f-8653-fd60de329fd6' `
  --output 'C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\smoke-a7' `
  --placed-assembly --template 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gost-assy.asmdot'
```

`smoke-a7\smoke-result.json` SHA-256:
`C0F57A4469B33D91EDB7C26FDFEDAA6C7326D4CB41F25F1FF619673B04EF009C`.
`success=true`, `outlineCompleteness=Complete`, `outlineComplete=true`,
`geometryValid=true`; 1 body, bounds `175.00000000000003 x 155 x 2.00800208 mm`,
volume `54097.58403728001 mm³`, expected `54097.58403888641 mm³`, residual
`-1.6064e-06 mm³`, XY residual `5.2e-07 mm`. Board probe true, diagnostics пусты.
21/21 occurrences, save/reopen, transformsVerified=true, board fixed=true,
translation residual `0`, cleanup `Verified/Verified`, createdDocumentsRemaining=0.
Assembly `smoke-a7\assembly\smoke-placed-assembly.SLDASM` SHA-256
`A6973635062EDDC873831E1348FDC8E6A8F6CF39EE42874037A42C5B9B556EE1`.

## 6. Commit SHA и Pull Request

- Финальный commit кода: `7aab96a18ab3f5e1a9fc43275fb66c576cdba303`.
- Report commit: `94ade6552d3f319e7aa2ca6f244600020731f328`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/19, base `source`, branch
  `agent/BRIDGE-000020-board-outline-completeness`, state `open`; merge не выполнялся.
- PR URL добавлен в report отдельным docs-коммитом после создания PR.

## 7. История изменений

1. Stage A: инвентаризация до алгоритмических изменений; получены 1 Outer, 2 Hole,
   5 Board Cutout region, 2 clip cutout и сохранённый inventory SHA.
2. Root cause: outline arc sweep/sagitta, region role accounting, origin/frame
   normalization и per-body volume вместо «весь board = full-thickness rectangle».
3. Smoke B итерации `smoke-b6` → `smoke-b9`: последовательно найдены и устранены
   неверная индексация fin, выбор probe height без учёта тонкого body и анализ
   probe в global coordinates; финальный JSON — `success=true`.
4. Smoke A итерации `smoke-a6` (SOLIDWORKS not running) → `smoke-a7` (успех); host
   был запущен вручную, без изменения конфигурации.
5. Финальные `build`, `test`, `diff --check` и документы; PR открывается отдельно,
   без merge в `source`.

## 8. REVIEW-FIX-001 — fail-closed доработка PR #19

Доработка выполнена в том же branch и том же PR #19; новый PR и merge в `source` не
создавались. Разделы 5.2–5.5 сохранены как история Stage A и первой итерации
BRIDGE-000020; ниже — финальные доказательства после REVIEW-FIX-001.

### 8.1 Причины замечаний ревью и исправления

| ID | Причина | Исправление | Подтверждение |
|---|---|---|---|
| FIX-A | Ошибки чтения геометрического polygon/contour не имели структурированной диагностики; пустой или нечитаемый Board Cutout region мог быть молча пропущен; отказ clipper, `InoperablePrims` и `null`-результаты не блокировали `Complete`. | Введены счётчики `readIssues`, `boardCutoutEmpty`, `boardCutoutErrors`, `boardCutoutUnprovenSkipped`, `clipInoperable`, `clipResultUnavailable`, `clipShapesUnavailable`, `clipCutoutErrors`. Пустая/нечитаемая region и любая необъяснённая ошибка дают `Incomplete`; `null`-результат clipper и `null`-коллекция shapes обрабатываются fail-closed. Критерии Desktop Smoke не ослаблены. | Финальный `ADAPTER_SUMMARY`: все перечисленные счётчики равны 0; smoke B `success=true`, `geometryValid=true`. |
| FIX-B | Источниковый контур и clipped loop не имели явного конечного статуса; источник мог повторно использоваться без доказательства many-to-many; частичное совпадение подавляло экспорт; dedup мог скрыть реальный Hole; ID экспортируемых контуров не гарантировались уникальными. | Каждый source/loop получает статус `matched`, `duplicate` или `unresolved`; повторное использование разрешено только при явном подтверждении; partial/ambiguous match и `unresolvedLoops` блокируют `Complete`; dedup учитывает уже экспортированные loop и геометрические Hole; `NextCutoutId` гарантирует уникальные ID. | `unresolvedSources=0`, `clipReusedSources=0`, `unresolvedLoops=0`, `partialClipMatches=0`, `duplicateClipLoops=0`, `duplicateCutoutContours=0`, 2 уникальных exported contour. |
| FIX-C | Классификация `BoardShape`/`Hole` допускала доказательство по bounds/центру/средней вершине; игнорировался `AltiumRegionContour.IsHole`; грубая замена дуги двумя точками принималась; неподдерживаемая вложенность не блокировала экспорт. | `BoardShape` требуется полное двустороннее совпадение всей границы Outer с учётом внутренних точек линий и sweep дуги через sagitta sampler; `Hole` требуется полное нахождение внутри Outer без пересечения/выхода; vertex-average больше не используется; `IsHole=true` принимается только как region-local island внутри доказанного Hole, иначе `Unsupported`; насыщение sampling-cap тоже `Unsupported`. | 8 топологических тестов, включая concave polygon, coarse arc replacement и nested island; `boardCutoutUnsupported=0` в финальном export. |
| FIX-D | `OriginX/OriginY` были необязательны, downstream применял `?? 0`; отсутствие или рассогласование origin не блокировало package. | Введён `EMB-PKG-032`: origin обязателен, пары IU/MM конечны и согласованы в рамках существующего unit tolerance, отрицательные значения допустимы; SOLIDWORKS больше не подставляет ноль; старые package без board asset/read compatibility сохранены. | Package имеет `originXIu=603848426/originXMm=1533.77500204`, `originYIu=388464566/originYMm=986.6999976399999`; smoke использует эти значения как frame origin. |

Два дополнительных fail-closed случая были обнаружены и устранены в ходе повторных
прогонов:

1. Первый Desktop export отклонил `IsHole=False` у region contour 0/1 как
   `unsupported nested/IsHole semantics`. Это было неверное прочтение флага:
   `IsHole=false` у главного контура Board Cutout region внутри Outer означает Hole,
   а `IsHole=true` — region-local island. После исправления export выдал 2 Hole.
2. Второй export считал `boardCutoutUnclassified=3` причиной `Incomplete`, хотя
   region 5/6/7 были доказанно `Unrelated` и не требуют экспорта. Счётчик переименован
   в `boardCutoutUnrelated` и исключён из fail-closed критериев; `Unrelated` остаётся
   терминальным статусом, но необъяснённые source по-прежнему блокируют `Complete`.

Формального нового ADR не потребовалось: предыдущие решения BRIDGE-000020
(`Complete`/`Incomplete`/`Unknown`, `board-3d-model` только при `Complete`, Board Cutout
как доказательство, запрет выдуманных координат и centre/bounds matching) сохранены.
Новые SDK-вызовы не добавлялись; `SDK_API_LEDGER.md` не менялся.

### 8.2 Инвентаризация: source → classification → clip loop → export

Исходный `inventory.json` Stage A в рабочей директории отсутствует; сохранённый в
разделе 5.2 SHA-256 является только исторической записью и **не** означает, что файл
сохранён сейчас. Доступный финальный лог даёт следующую компактную таблицу без
координат заказчика:

| Источник | Что доказано | Статус | Clipped loop | Экспорт |
|---|---|---|---|---|
| Geometric contour 0, 140 points | полное совпадение всей границы Outer, line interior + arc sweep | Outer / BoardShape | — | `altium:contour:0` (Outer) |
| Board Cutout region 0, contour 0, 36 points | bounds analytic/sampled совпадают, контур внутри Outer | Hole | shape 1 loop 0 | `altium:contour:cutout:clip:2` (Hole) |
| Board Cutout region 1, contour 0, 7 points | bounds analytic/sampled совпадают, контур внутри Outer | Hole | shape 0 loop 0 | `altium:contour:cutout:clip:1` (Hole) |
| Board Cutout region 5, contour 0, 66 points | не на Outer и не полностью внутри платы | Unrelated (терминальный) | — | — |
| Board Cutout region 6, contour 0, 66 points | не на Outer и не полностью внутри платы | Unrelated (терминальный) | — | — |
| Board Cutout region 7, contour 0, 13 points | не на Outer и не полностью внутри платы | Unrelated (терминальный) | — | — |

Финальный `ADAPTER_SUMMARY` (2026-11-26T11:32:11Z):

```text
outlineExact=1 outlineFallback=0 outlineOmitted=0 outlineLoopErrors=0
outlineUnmatchedLoops=0 outlineAmbiguous=0 outlinePartial=0
geometricContours=1 geometricPoints=140 geometricHoles=0 readIssues=0
totalRegions=17 boardCutoutRegions=5 boardCutoutContours=5 boardCutoutHoles=2
boardCutoutEmpty=0 boardCutoutErrors=0 boardCutoutUnprovenSkipped=0
boardCutoutShapes=0 boardCutoutShapeFragments=0 boardCutoutNestedIslands=0
boardCutoutUnrelated=3 boardCutoutUnsupported=0 unresolvedSources=0
clipCutouts=2 clipNewPrims=1 clipInoperable=0 clipResultUnavailable=0
clipShapesUnavailable=0 clipCutoutContours=2 clipCutoutSegments=16
clipCutoutErrors=0 clipMatchedRegions=2 clipUnmatchedRegions=0
clipAmbiguousRegions=0 clipReusedSources=0 unresolvedLoops=0
duplicateClipLoops=0 partialClipMatches=0 duplicateCutoutContours=0
exportedCutoutContours=2
```

### 8.3 Финальный export в AD26

Package: `abcfd4e2-a42e-4343-9822-fd10419cfdc5`, каталог
`C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\packages-b-reviewfix-final\abcfd4e2-a42e-4343-9822-fd10419cfdc5`.

- `board.snapshot.json` SHA-256
  `F93B6453ED9C3C4449F34AA050340EBE01F0D8BE0901F84E6FCD7F509A4FAF1A`;
  `board.outline.completeness=complete`.
- `manifest.json` SHA-256 `C21E1F2A1755709B2B9C2527B293C285C414C599F81614C87BEF6D74DC269081`,
  capability `board-3d-model` присутствует.
- `checksums.sha256` SHA-256
  `FEF692EEEF3BB2A83419DEF6AA40FEBA5C568933BC02BC062862CCCE29DE21B1`.
- Board STEP `assets/board/40d9d7783e6837322fa1111c8f9f5f622758136a39b1757ac611a5fcf11f32a0.step`
  SHA-256 `40D9D7783E6837322FA1111C8F9F5F622758136A39B1757AC611A5FCF11F32A0`.
- `PACKAGE_EXPORT_OK`: layers=17, components=73, pads=224, nets=57, bodies=152,
  stepAssets=152, exportFailures=0, fallbacks=0, adapterWarnings=14,
  modelWarnings=0, packageWarnings=28.
- Экспортированные контуры: `altium:contour:0` Outer (12 segments),
  `altium:contour:cutout:clip:1` Hole (7 line segments),
  `altium:contour:cutout:clip:2` Hole (9 segments).
- Origin: `originXIu=603848426`, `originXMm=1533.77500204`,
  `originYIu=388464566`, `originYMm=986.6999976399999`.
- SHA-256 `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\board-with-cutout.PcbDoc`
  до и после export: `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753`.

### 8.4 Финальные smoke-прогоны после REVIEW-FIX-001

Перед прогонами инстанс SOLIDWORKS не был запущен (`HRESULT 0x800401E3`); хост был
запущен вручную из `E:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe`, без
изменения конфигурации. Оба прогона завершились успешно.

#### Desktop Smoke B — cutout board

```powershell
dotnet tools\EcadMcadBridge.SolidWorks.Smoke\bin\Release\net10.0-windows\EcadMcadBridge.SolidWorks.Smoke.dll `
  --package 'C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\packages-b-reviewfix-final\abcfd4e2-a42e-4343-9822-fd10419cfdc5' `
  --output 'C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\smoke-b10' `
  --placed-assembly --max-components 1 `
  --template 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gost-assy.asmdot'
```

`smoke-b10\smoke-result.json` SHA-256:
`8CBC4AB8825E8838AC2DF48BC8672B1F06BE8805920E40FCD6D75F2DD4CB032A`.
`success=true`, `boardModelRequested=true`, `outlineCompleteness=Complete`,
`outlineComplete=true`, `geometryValid=true`, 3 solid body, bounds
`80 x 54.999999999999886 x 0.79649574 mm`.

| Проверка | Значение | Допуск |
|---|---:|---|
| `SW-BRD-008` XY residual | `2.9000002541579306e-06 mm` | `0.05 mm` |
| Z top residual | `0 mm` | `0.05 mm` |
| Thickness delta | `1.1102230246251565e-16 mm` | nominal `0.7964957399999999 mm`, измеренная толщина body |
| `SW-BRD-010` volume | measured `848.1995991811367 mm³`, expected `847.0143327901454 mm³`, residual `1.1852663909912735 mm³` | `max(5 mm³, 2%) = 16.9402866558029 mm³` |

Per-body measurement (все три тела — solid, строгая tessellation прочитана):

| Body | Thickness mm | Measured mm³ | Expected mm³ | Residual mm³ | Triangles |
|---|---:|---:|---:|---:|---:|
| 1 strip | `0.08449818` | `27.8843994` | `27.88441127` | `-1.18703e-05` | 44 |
| 2 lobe | `0.79649574` | `402.5628560` | `401.96817871` | `0.59467728` | 392 |
| 3 lobe | `0.79649574` | `417.75234378` | `417.16174281` | `0.59060098` | 260 |

Probes: `board` / `altium:contour:0` expected=true actual=true; `cutout` /
`altium:contour:cutout:clip:1` expected=false actual=false; `cutout` /
`altium:contour:cutout:clip:2` expected=false actual=false. Geometry diagnostics —
только ожидаемый Warning `SW-BRD-005` о 3 solid body; Error-диагностик нет.

Placed assembly `smoke-b10\assembly\smoke-placed-assembly.SLDASM` SHA-256
`556652DD78C06A155875A50683FB4CCEB941AA6A4606D3553DCE6FA571060A75`; 2/2
occurrences, save/reopen=true, transformsVerified=true, board occurrence `board-1`
fixed=true и verified=true, translation/rotation residual `0`. Cleanup import и
placedAssembly `Verified`, `createdDocumentsRemaining=0`. SHA-256 исходного `.PcbDoc`
после smoke: `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753`.

#### MiniPC Smoke A

```powershell
dotnet tools\EcadMcadBridge.SolidWorks.Smoke\bin\Release\net10.0-windows\EcadMcadBridge.SolidWorks.Smoke.dll `
  --package 'C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\packages-minipc\4a4fb429-c8b5-434f-8653-fd60de329fd6' `
  --output 'C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000020\smoke-a8' `
  --placed-assembly --template 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gost-assy.asmdot'
```

`smoke-a8\smoke-result.json` SHA-256:
`093206173A6C17D1C8306ED966B89BCAF0DD7D0CC6F8CB86F8750CDE8A208C06`.
`success=true`, `outlineCompleteness=Complete`, `geometryValid=true`; 1 solid body,
bounds `175.00000000000003 x 155 x 2.0080020800000002 mm`, volume
`54097.58403728001 mm³`, expected `54097.58403888647 mm³`, residual
`-1.6064586816355586e-06 mm³`, XY residual `5.1999995775986463e-07 mm`.
Geometry diagnostics пусты. 21/21 occurrences, save/reopen, transformsVerified=true,
board fixed=true, translation residual `0`, cleanup `Verified/Verified`,
`createdDocumentsRemaining=0`. Assembly
`smoke-a8\assembly\smoke-placed-assembly.SLDASM` SHA-256
`626B905439251368EA2F52EAA5FBD8399008C0142A41D83015295A830AE77A60`.
SHA-256 `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000019\minipc.PcbDoc`
до и после smoke: `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93`.

### 8.5 Итоговая проверка и residual-риски

- `dotnet build -c Release --nologo`: succeeded, `Предупреждений: 0`, `Ошибок: 0`.
- `dotnet test -c Release --nologo --no-build`: Core 229/229, Altium 184/184,
  SOLIDWORKS 94/94, всего 507/507.
- `git diff --check`: без ошибок; рабочее дерево после финальных коммитов чистое.
- Регрессии BRIDGE-000018/000019 frame/placement, `SW-BRD-008` (`0.05 mm`) и
  `SW-BRD-010` (`max(5 mm³, 2%)`), strict all-solid-body checks, cleanup/save/reopen —
  без изменений.
- Остаются ранее зафиксированные риски: отсутствующий `inventory.json`,
  аналоговые дуги region аппроксимируются line segments из `GetGeometricPolygon`,
  per-body volume является диагностикой, а не отдельным gate, ожидается `SW-BRD-005`
  для трёх solid body, полноценный Add-in host/Registry/installer вне scope,
  task-файл BRIDGE-000020 в репозитории отсутствует.
- Ни proprietary DLL, ни лицензии, ни PCB/STEP/SLDASM/SLDPRT, ни данные заказчика в
  Git не добавлены. PR #19 остаётся открытым и неслитым; `source` не изменялся.
