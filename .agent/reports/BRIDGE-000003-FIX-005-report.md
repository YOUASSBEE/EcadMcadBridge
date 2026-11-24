---
id: BRIDGE-000003-FIX-005
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T16:42:14+03:00
updated: 2026-11-24T16:42:14+03:00
base: source
branch: task/BRIDGE-000003-altium-board-adapter
commit: 0ec498dea04a568400ee9bc35abc87f0db13a76a
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
related: [BRIDGE-000003, BRIDGE-000003-FIX-001, BRIDGE-000003-FIX-002, BRIDGE-000003-FIX-003, BRIDGE-000003-FIX-004, BRIDGE-000003-FIX-005]
---

# BRIDGE-000003-FIX-005 — дедупликация Board Cutout и Region matching

## 1. Итог

**FIX-005 выполнен.**

- Исправлена дедупликация: `MatchesExistingGeometricHole` больше не сравнивает
  clip loop с самим собой; используются фактические точки `geometric.Points`.
- Region matching больше не выбирает первый Region по bounding box. Добавлены
  broad phase, геометрическая проверка, различение `Matched` / `Unmatched` /
  `Ambiguous` и association tracking по паре `(RegionIndex, ContourIndex)`.
- Добавлены тесты A1–A5, B1–B7, C1–C4 и regression-тест на дугу `270°→0°`.
- Найден и исправлен дополнительный дефект FIX-004: дуги трактовались как CW при
  `Angle2 < Angle1`, из-за чего arc `270°→0°` превращался в CW-дугу 270°. Теперь
  обход всегда CCW с переходом через 0°.
- На `board with cutout.PcbDoc` сохранены оба Hole; на MiniPC и Bluetooth ложных
  cutout нет.
- Build/test: 0 warnings/errors, 41 Core + 43 Altium = **84/84**.

## 2. FIX-005-A — дедупликация

### 2.1 Исправление

Старый код:

```csharp
var closed = EnsureClosed(points);          // точки clip loop
...
if (OutlineContourMatcher.TryExactMatch(closed, loop)) // тот же clip loop
    return true;
```

`geometric.Points` не участвовали в сравнении. Любой существующий Hole мог
подавить export.

Новая логика вынесена в `CutoutDeduplicator`:

```csharp
foreach (var hole in existingHoles)
{
    var closed = EnsureClosed(hole.Points); // именно geometric.Points
    if (OutlineContourMatcher.TryExactMatch(closed, clipLoop))
        return Duplicate;
}
```

- проверяются только `ContourRole.Hole`;
- outer-контур платы не считается дубликатом выреза;
- исходные коллекции не изменяются;
- partial overlap возвращает `Partial`, а не `Duplicate`;
- `ShouldExport == false` только для `Duplicate`.

Reader передаёт в helper `geometricContours.Where(Role == Hole)` с исходным
`GeometricContour.Index`; duplicate warning содержит shape index, loop index,
Hole index и clip bounds.

### 2.2 Тесты A1–A5

| Тест | Сценарий | Ожидание | Результат |
|---|---|---|---|
| A1 | Hole и clip loop геометрически совпадают | `Duplicate`, второй экземпляр не экспортируется | пройден |
| A2 | Hole — другой вырез | `NotDuplicate`, export разрешён | пройден |
| A3 | Посторонний Hole + валидный clip cutout | посторонний Hole не подавляет export | пройден |
| A4 | Частичное совпадение | `Partial`, export не подавляется | пройден |
| A5 | Несколько Hole, совпадает второй | `Duplicate` с `ExistingHoleIndex=1`, остальные не влияют | пройден |

A1 дополнительно проверяет `SelectExportableLoops`: два одинаковых clip loop дают
пустой список экспорта.

## 3. FIX-005-B — Region matching

### 3.1 Исследование API

- `IPCB_Region.GetGeometricPolygon()` может вернуть несколько контуров
  (outer + holes), поэтому source contour определяется парой
  `(RegionIndex, ContourIndex)`, а не только Region.
- `IPCB_RegionShape` содержит один или несколько замкнутых циклов; reader
  разбивает их через `BuildLoops` и обрабатывает каждый loop отдельно.
- `IPCB_Shapes` — коллекция `IPCB_RegionShape`; `Count()` и `Items(i)` позволяют
  обработать несколько shapes.
- Один Region не считается автоматически одним clip-контуром: association
  выполняется по конкретному contour.
- `GetNewPrims`/`GetInoperablePrims` использованы только как диагностические
  счётчики; их геометрия не экспортируется.

### 3.2 Алгоритм

`CutoutRegionMatcher.Match(clipLoop, sources, assigned)`:

1. **Broad phase** — bounding box с допуском `10 IU` (25.4 nm).
2. **Geometry verification** для каждого кандидата:
   - все точки Region-контура лежат на clip loop в пределах `3500 IU`;
   - площадь отличается не более чем на `5%`;
   - centroid отличается не более чем на `1000 IU`.
3. **Uniqueness**:
   - ровно один свободный кандидат → `Matched`;
   - ни одного → `Unmatched`;
   - больше одного → `Ambiguous`.
4. **Association**: после `Matched` ключ `(RegionIndex, ContourIndex)` добавляется
   в `HashSet`; повторное назначение того же source contour запрещено.
5. **Export policy**: экспортируется только `Matched` и не-дубликат;
   `Unmatched`/`Ambiguous` диагностируются и не экспортируются.

Допуски обоснованы измерением на операторской плате:

```text
region 0: area ratio 0.14%, centroid 692.61 IU, max vertex 3096.57 IU
region 1: area ratio 0.00%, centroid 0.00 IU, max vertex 0.00 IU
```

Порог `3500 IU` покрывает измеренные `3096.57 IU` с небольшим запасом; это
8.89 µm. `1000 IU` = 2.54 µm. `5%` площади — только дополнительный признак,
не самостоятельное доказательство.

### 3.3 Тесты B1–B7

| Тест | Сценарий | Ожидание | Результат |
|---|---|---|---|
| B1 | Один кандидат | `Matched` | пройден |
| B2 | Нет кандидатов | `Unmatched` | пройден |
| B3 | Одинаковые bounds, разная геометрия | выбран геометрически правильный Region | пройден |
| B4 | Два неразличимых Region | `Ambiguous`, произвольного выбора нет | пройден |
| B5 | Source contour уже назначен | повторное назначение запрещено | пройден |
| B6 | Два контура одного Region | оба сопоставлены по `(RegionIndex, ContourIndex)` | пройден |
| B7 | Отклонение вершины 3400/3600 IU | 3400 → `Matched`, 3600 → `Unmatched` | пройден |

B7 использует симметричные выемки при неизменных bounds, поэтому проверяется
именно vertex tolerance, а не broad-phase.

## 4. FIX-005-C — arc matcher

Добавлены тесты:

| Тест | Сценарий | Результат |
|---|---|---|
| C1 | Плотная polyline вдоль аналитической дуги | `TryExactMatch() == true` |
| C2 | Грубая polyline, отклонение больше 2 IU | `false` |
| C3 | Те же концы, противоположное направление дуги | `false` |
| C4 | Замкнутый контур с двумя дугами разных SweepDirection | exact `true`; грубая polyline `false` |

Дополнительно добавлен regression-тест `ArcAnglesCrossingZero_UseShortCounterClockwiseArc`:

- raw arc `Angle1=270`, `Angle2=0`, center `(609742449,402796921)`, radius `275591`;
- ожидается короткая CCW-дуга от `(609742449,402521330)` до
  `(610018040,402796921)`.

Именно эта ошибка мешала Region matching: до исправления arc `270°→0°`
становился CW-дугой 270°, а регион 0 не совпадал с clip loop.

`ToleranceIu` для `OutlineContourMatcher` оставлен **2.0**; Region matching
использует отдельные документированные допуски (10/3500/1000 IU и 5% площади).

## 5. Smoke и regression

Все работы выполнены на копиях в `%TEMP%\EcadMcadBridge\smoke-000004\`.
Исходные файлы не редактировались и не сохранялись.

| Плата | SHA256 до/после | JSON | Контуры | Cutout |
|---|---|---|---|---:|
| `board-with-cutout.PcbDoc` | `ED624C68…` | `board-snapshot-20261124T133651123Z.json` | 1 outer + 2 holes | 2 |
| `minipc.PcbDoc` | `50547E5E…` | `board-snapshot-20261124T133827917Z.json` | 1 outer | 0 |
| `bluetooth-sentinel.PcbDoc` | `1E18C3EF…` | `board-snapshot-20261124T133924041Z.json` | 1 outer | 0 |

### 5.1 `board with cutout.PcbDoc`

JSON: 315 236 байт, validation errors 0.

```text
boardCutoutRegions=5
clipCutouts=2
clipCutoutContours=2
clipCutoutSegments=16
clipCutoutErrors=0
clipMatchedRegions=2
clipUnmatchedRegions=0
clipAmbiguousRegions=0
duplicateCutoutContours=0
exportedCutoutContours=2
```

Контуры:

| Index | Role | Сегменты | Bounds |
|---:|---|---:|---|
| 0 | outer | 140 line | (590305120,383740157) – (621801182,405393701) |
| 1 | hole | 7 line | (601663387,400423228) – (602539373,402066928) |
| 2 | hole | 6 line + 3 arc | (607862528,402521330) – (610018040,403604007) |

Дуги hole 2 после исправления:

| Start | End | Center | Radius IU | Sweep |
|---|---|---|---:|---|
| (607862528,403161093) | (608502291,402521330) | (608502291,403161093) | 639763 | CCW |
| (609742449,402521330) | (610018040,402796921) | (609742449,402796921) | 275591 | CCW |
| (608147961,403604007) | (607862528,403318574) | (608147961,403318574) | 285433 | CCW |

Все 14 вершин hole 1 и 18 вершин hole 2 находятся внутри outer; 0 снаружи.
Оба clip loop имеют `Matched`; `Ambiguous`/`Unmatched`/`Duplicate` отсутствуют.

### 5.2 `MiniPC.PcbDoc`

```text
pads=6004
totalRegions=382
boardCutoutRegions=1
clipCutouts=0
clipMatchedRegions=0
clipUnmatchedRegions=0
clipAmbiguousRegions=0
exportedCutoutContours=0
```

Layer stack region не экспортирован как вырез. `outlineExact=1`, 1 outer, 6 line.

### 5.3 `Bluetooth_Sentinel.PcbDoc`

```text
boardCutoutRegions=3
clipCutouts=0
exportedCutoutContours=0
outlineExact=0
outlineFallback=1
outlinePartial=1
```

Fallback warning для outer сохранён; ложных cutout и повторных associations нет.

## 6. Build / tests

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release — 41/41 Core.Tests, 43/43 Altium.Tests, всего 84/84
```

Новые тесты:

- 5 dedup (A1–A5);
- 7 Region matching (B1–B7);
- 4 arc exact (C1–C4);
- 1 arc-angle wrap regression.

## 7. Изменённые файлы

| Файл | Изменение |
|---|---|
| `src/EcadMcadBridge.Altium/Adapter/CutoutMatching.cs` | Новый pure helper: dedup, Region matching, bounds/area/centroid, sample loop |
| `src/EcadMcadBridge.Altium/Adapter/AltiumOutlineReader.cs` | Использует helper; association tracking; диагностика; CCW-дуги с переходом через 0° |
| `src/EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter.cs` | `clipAmbiguousRegions` в diagnostics |
| `src/EcadMcadBridge.Altium/Adapter/OutlineContourMatcher.cs` | Перегрузка `TryExactMatch` с tolerance и `DistanceToLoop` |
| `tests/EcadMcadBridge.Altium.Tests/CutoutDeduplicationTests.cs` | A1–A5 |
| `tests/EcadMcadBridge.Altium.Tests/CutoutRegionMatcherTests.cs` | B1–B7 |
| `tests/EcadMcadBridge.Altium.Tests/ArcExactMatchTests.cs` | C1–C4 + arc wrap |
| `.agent/SDK_API_LEDGER.md` | Семантика CCW, dedup/matching, FIX-005 |
| `.agent/ARCHITECTURE.md`, `.agent/PROJECT_CONTEXT.md`, `.agent/reports/BRIDGE-000003-report.md`, `AGENTS.md` | Актуализация текущего состояния |

Публичные контракты Core, TFM и зависимости Core от Altium SDK не менялись.

## 8. Риски и ограничения

- Вложенные острова внутри cutout по-прежнему не имеют полноценного представления
  в Core 1.0.0; несколько loops одного shape экспортируются как отдельные Hole.
- Region matching использует эмпирические допуски для tessellated contour;
  они документированы и проверены на операторской плате, но не гарантируют
  произвольную геометрию без smoke.
- `Ambiguous` не разрешается произвольным выбором: такой cutout не экспортируется
  и требует ручного анализа.
- Pad/via holes остаются вне Core (V-19).

## 9. Commit / PR

- Commit реализации и документации:
  `0ec498dea04a568400ee9bc35abc87f0db13a76a`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
  (обновлён, не слит).
- Ветка: `task/BRIDGE-000003-altium-board-adapter`, база `source`.
- Force-push, rebase и merge не выполнялись.

## 10. История изменений

| Итерация | Результат |
|---|---|
| FIX-001 | Все geometric contours, PadShape exact/simplified/unsupported |
| FIX-002 | Exact matcher отвергает partial/ambiguous |
| FIX-003 | Arc angular range + geometric midpoints |
| FIX-004 | Реальный Board Cutout через `IPCB_Region` + `ClipWithPolyRegShapes` |
| FIX-005 | Dedup по `geometric.Points`, строгий Region matching, CCW-wrap дуг, 84 теста |
