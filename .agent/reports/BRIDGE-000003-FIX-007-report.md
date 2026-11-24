---
id: BRIDGE-000003-FIX-007
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T21:28:00+03:00
updated: 2026-11-24T21:28:00+03:00
base: source
branch: task/BRIDGE-000003-altium-board-adapter
commit: 1fd10294af6dcde6b09eac5b6680b00ffc649524
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
related: [BRIDGE-000003, BRIDGE-000003-FIX-001, BRIDGE-000003-FIX-002, BRIDGE-000003-FIX-003, BRIDGE-000003-FIX-004, BRIDGE-000003-FIX-005, BRIDGE-000003-FIX-006, BRIDGE-000003-FIX-007]
---

# BRIDGE-000003-FIX-007 — scale-independent arc deduplication

## 1. Итог

**FIX-007 выполнен.**

- Устранён ложный отрицательный результат approximation-dedup для дугового Hole
  при большой геометрии и разной плотности полилинии.
- Raw polygon centroid больше не является критерием подавления; сравниваются
  centroid характерных точек после проекции вершин Hole на clip loop.
- Перенос независимого контура по-прежнему отклоняется.
- A1–A15 проходят; Altium.Tests: **65/65**, Core.Tests: **41/41**, всего **106/106**.
- Три smoke-сценария сохранены; SHA256 PCB не изменились.

## 2. Причина ложного отказа

FIX-006 проверял approximation-dedup через raw polygon centroid. Для одной и той же
физической геометрии полигональное представление Hole и аналитический clip loop
имеют разные площади и centroid из-за разной плотности дискретизации.

Воспроизведение A11:

- четверть окружности, радиус `100 000 IU`;
- Hole polyline — шаг `10°`;
- clip loop — аналитическая дуга;
- `DedupArcSamples = 32`;
- разница raw centroid ~`140.4 IU` при допуске `100 IU`;
- разница площадей ~`0.47%`;
- максимальное отклонение sampled clip от Hole polyline ~`381 IU`;
- bounds совпадают.

Все проверки, кроме raw centroid, проходили. Один и тот же вырез мог быть
экспортирован повторно.

## 3. Новый алгоритм

Сохранены независимые допуски:

- exact dedup — `2 IU`;
- approximation distance — `3500 IU`;
- approximation area — `5%`;
- matched centroid — `100 IU`.

Добавлено:

1. Для каждой вершины Hole вычисляется ближайшая точка на аналитическом clip loop
   (`TryClosestPointOnLoop`):
   - для линии — проекция на сегмент;
   - для дуги — радиальная проекция, если угол входит в sweep, иначе ближайший
     конец дуги.
2. Сравниваются:
   - centroid Hole;
   - centroid спроецированных характерных точек.
3. Raw polygon centroid остаётся только диагностическим (`rawCentroidDistance`).
4. Двусторонняя проверка сохранена:
   - все Hole vertices на clip loop в пределах 3500 IU;
   - все sampled clip points на Hole polyline в пределах 3500 IU;
   - площадь 5%;
   - matched centroid 100 IU.

Свойство scale independence: при разной плотности полилинии вершины Hole всё равно
лежат на одной аналитической дуге, поэтому их проекции совпадают с вершинами, и
matched centroid не зависит от шага полилинии. При переносе независимого контура
проекции остаются на исходном clip loop, а centroid Hole смещается — разница
обнаруживается допуском 100 IU.

Начальная точка и направление обхода не влияют на результат: проекция и centroid
характерных точек не зависят от порядка обхода.

## 4. Тесты A1–A15

A1–A10 сохранены. Новые сценарии:

| Тест | Сценарий | Результат |
|---|---|---|
| A11 | радиус 100 000 IU, Hole 10°, clip 32 samples | `Duplicate`, `ShouldExport=false` |
| A12 | радиус 5 000 000 IU, Hole 1° | `Duplicate`, `ShouldExport=false` |
| A13 | контур A11 смещён на 500 IU | не `Duplicate`, export разрешён |
| A14 | промежуточная вершина Hole отклонена >3500 IU | не `Duplicate`, export разрешён |
| A15 | та же геометрия, другая начальная точка и обратный обход | `Duplicate`, `ShouldExport=false` |

A11 дополнительно проверяет, что сработал approximation-путь
(`matchedCentroidDistance` в reason), а не exact matcher.

A1–A5 (exact/partial/different Hole) и A6–A10 (дуговые сценарии, соседние вырезы,
противоположный sweep, несколько дуг) не изменены и проходят.

## 5. Regression и smoke

Все работы — на копиях в `%TEMP%\EcadMcadBridge\smoke-000004\`.

| Плата | SHA256 до/после | JSON | Контуры | Cutout |
|---|---|---|---|---:|
| `board-with-cutout.PcbDoc` | `ED624C68…` | `board-snapshot-20261124T182425619Z.json` | 1 outer + 2 holes | 2 |
| `minipc.PcbDoc` | `50547E5E…` | `board-snapshot-20261124T182600761Z.json` | 1 outer | 0 |
| `bluetooth-sentinel.PcbDoc` | `1E18C3EF…` | `board-snapshot-20261124T182700331Z.json` | 1 outer | 0 |

### 5.1 Операторская плата

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
| 0 | outer | 140 line | (590305120,383740157)-(621801182,405393701) |
| 1 | hole | 7 line | (601663387,400423228)-(602539373,402066928) |
| 2 | hole | 6 line + 3 arc | (607862528,402521330)-(610018040,403604007) |

Все 14 вершин hole 1 и 18 вершин hole 2 внутри outer; 0 снаружи.

### 5.2 MiniPC

```text
pads=6004
totalRegions=382
boardCutoutRegions=1
clipCutouts=0
exportedCutoutContours=0
outlineExact=1
```

Ложных Hole и layer stack cutouts нет.

### 5.3 Bluetooth Sentinel

```text
boardCutoutRegions=3
clipCutouts=0
exportedCutoutContours=0
outlineExact=0
outlineFallback=1
outlinePartial=1
```

Fallback outer сохранён; ложных cutout нет.

## 6. Build и тесты

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release — 41/41 Core.Tests, 65/65 Altium.Tests, всего 106/106
```

## 7. Изменённые файлы

| Файл | Изменение |
|---|---|
| `src/EcadMcadBridge.Altium/Adapter/CutoutMatching.cs` | matched-centroid по проекциям, raw centroid только диагностика |
| `src/EcadMcadBridge.Altium/Adapter/OutlineContourMatcher.cs` | `TryClosestPointOnLoop` для линий/дуг |
| `tests/EcadMcadBridge.Altium.Tests/CutoutDeduplicationTests.cs` | A11–A15 |
| `.agent/ARCHITECTURE.md`, `.agent/PROJECT_CONTEXT.md`, `.agent/SDK_API_LEDGER.md`, `.agent/reports/BRIDGE-000003-report.md`, `AGENTS.md` | Актуализация |

Core API, TFM, exact tolerance 2 IU и approximation tolerances не менялись.

## 8. Оставшиеся ограничения

- Dedup остаётся геометрической эвристикой с конечными допусками; это не
  математическое доказательство идентичности в произвольной точности.
- Вложенные Hole по-прежнему не представимы в Core 1.0.0.
- Реальная дедупликация на Hole из `GeometricPolygon` не наблюдалась на операторской
  плате (0 geometric holes); сценарий покрыт A1–A15.
- При большом числе BoardCutout sources geometry-first matching может быть дороже,
  но на проверенных платах их 1–5.

## 9. Commit / PR

- Commit реализации и документации:
  `1fd10294af6dcde6b09eac5b6680b00ffc649524`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
  (обновлён, не слит).
- Ветка: `task/BRIDGE-000003-altium-board-adapter`, база `source`.
- Force-push, rebase и merge не выполнялись.

## 10. История изменений

| Итерация | Результат |
|---|---|
| FIX-004 | Реальный Board Cutout через `IPCB_Region` + `ClipWithPolyRegShapes` |
| FIX-005 | Dedup по `geometric.Points`, uniqueness/association, CCW-wrap дуг |
| FIX-006 | Analytic arc bounds, geometry-first matching, arc-aware dedup |
| FIX-007 | Scale-independent matched-centroid dedup, A11–A15 |
