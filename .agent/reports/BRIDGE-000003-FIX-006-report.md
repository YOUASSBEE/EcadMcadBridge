---
id: BRIDGE-000003-FIX-006
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T17:23:51+03:00
updated: 2026-11-24T17:23:51+03:00
base: source
branch: task/BRIDGE-000003-altium-board-adapter
commit: 5592622d5f95f52d3e16d040aae19b6f9c1eee7d
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
related: [BRIDGE-000003, BRIDGE-000003-FIX-001, BRIDGE-000003-FIX-002, BRIDGE-000003-FIX-003, BRIDGE-000003-FIX-004, BRIDGE-000003-FIX-005, BRIDGE-000003-FIX-006]
---

# BRIDGE-000003-FIX-006 — analytic bounds и дуговой dedup

## 1. Итог

**FIX-006 выполнен.**

- Bounds аналитических дуг вычисляются точно, без зависимости от `ArcSamples`.
- Broad phase больше не может отбросить корректный Region из-за пропущенного
  кардинального экстремума: matching стал geometry-first.
- Дедупликация дуговых Hole использует отдельный approximation tolerance,
  двустороннее покрытие, площадь и centroid.
- На операторской плате сохранены оба Hole; на MiniPC/Bluetooth ложных cutout нет.
- Build/test: 0 warnings/errors, 41 Core + 60 Altium = **101/101**.

## 2. FIX-006-A — analytic bounds

### 2.1 Алгоритм

Добавлен `AnalyticBounds.GetAnalyticBounds(loop)`:

1. Для каждого сегмента всегда учитываются Start и End.
2. Для arc дополнительно проверяются четыре кардинальных направления:
   - 0° — max X;
   - 90° — max Y;
   - 180° — min X;
   - 270° — min Y.
3. Кардинальная точка включается только если угол входит в sweep:
   - CCW: `Normalize(cardinal - start) <= sweep`;
   - CW: `Normalize(start - cardinal) <= sweep`.
4. `start=end` трактуется как полная окружность.
5. Bounds объединяются по всем линиям и дугам цикла.
6. `SampleLoop()` для bounds не используется.

Поддержаны:

- CW и CCW;
- переход 0°/360°;
- дуги менее 90°;
- дуги более 180°;
- несколько дуг в одном цикле;
- дуги без кардинальных экстремумов;
- большие радиусы (600 000 IU и более).

При некорректной геометрии (нет Center, radius <= 0, неполные координаты,
неизвестное направление) возвращается `AnalyticBoundsResult.IsExact=false`
и текст ошибки. Фиктивный «точный» bounds не выдаётся.

### 2.2 Broad phase

Принят **Подход B — geometry-first**:

- `BoundsToleranceIu = 10` оставлен для диагностики/ранжирования;
- bounds не отбрасывают source contour до geometry verification;
- проверяются все BoardCutout source contours;
- причина: tessellated Region может не содержать кардинальный экстремум, и его
  bounds будут меньше analytic bounds. Такое расхождение не должно приводить
  к false negative.

B11 подтверждает это: Region source с maxY на 366 IU ниже analytic bounds
(пропущен 90°) всё равно получает `Matched`, потому что геометрия проверена.

### 2.3 Измерения bounds

На `board with cutout.PcbDoc`:

| Cutout | Region bounds | Analytic bounds | Sampled bounds | Δ |
|---|---|---|---|---:|
| 1 (7 lines) | (601663387,400423228)-(602539373,402066928) | то же | то же | 0 |
| 2 (6 lines + 3 arcs) | (607862528,402521330)-(610018040,403604007) | то же | то же | 0 |

На операторской плате sampling совпал с analytic bounds, потому что дуги —
четверти, а Start/End попадают в выборку. Независимость от sampling доказана
синтетическим B11 с дугой 10°–160° и радиусом 600 000 IU.

## 3. Отдельные допуски

| Назначение | Допуск | Обоснование |
|---|---|---|
| Exact outline matcher | `2 IU` | Не менялся с FIX-003 |
| Region vertex | `3500 IU` | Измеренное отклонение Region-контура 3096.57 IU |
| Region area | `5%` | Дополнительный признак, не самостоятельное доказательство |
| Region centroid | `1%` диагонали, минимум `100 IU` | Отделяет tessellation от переноса всей фигуры; region 0: 692.61 IU при диагонали ~2.4M IU |
| Dedup exact | `2 IU` | Идентичная линия/полилиния |
| Dedup approximation | `3500 IU` | Отдельный допуск для полигонального Hole против аналитической дуги; тот же порядок измеренной точности Altium |
| Dedup area | `5%` | Защита от похожего, но другого контура |
| Dedup centroid | `100 IU` | Дубликат должен быть в том же месте; 100 IU = 0.254 µm |

`BoundsToleranceIu = 10` не используется как допуск идентичности и не
увеличивался.

## 4. FIX-006-B — дедупликация дуговых Hole

`CutoutDeduplicator` теперь выполняет два уровня:

1. `TryExactMatch(..., ExactToleranceIu = 2)`.
2. `TryApproximateDuplicate`:
   - exact analytic bounds clip loop;
   - bounds hole vs clip в пределах 3500 IU;
   - все точки Hole на clip loop в пределах 3500 IU;
   - все sampled-точки clip loop на Hole-полилинии в пределах 3500 IU;
   - площадь 5%;
   - centroid 100 IU.

Partial overlap не подавляет export. Разные соседние вырезы не считаются
дубликатами; A8 проверяет, что второй вырез экспортируется.

На операторской плате `GeometricPolygon` содержит **0 hole-контуров**, поэтому
реальная дедупликация в smoke не срабатывает (`duplicateCutoutContours=0`).
A6–A10 покрывают полигональное представление синтетическими данными, включая
несколько дуг и противоположный sweep.

## 5. Тесты

### 5.1 B1–B12 (analytic bounds)

| Тест | Результат |
|---|---|
| B1 0°–90° | пройден |
| B2 15°–75° без глобальных экстремумов | пройден |
| B3 10°–170°, внутренний 90° | пройден |
| B4 200°–340°, отрицательные экстремумы | пройден |
| B5 CCW 350°→10°, переход через 0° | пройден |
| B6 CW та же короткая дуга | пройден |
| B7 дуга >180° | пройден |
| B8 линии + дуги | пройден |
| B9 радиус 600 000 IU | пройден |
| B10 независимость от sample density | пройден |
| B11 broad phase не отбрасывает Region с пропущенным экстремумом | пройден |
| B12 невалидная дуга → диагностика | пройден |

### 5.2 A1–A10 (dedup)

A1–A5 сохранены и проходят. Новые:

| Тест | Результат |
|---|---|
| A6 плотная/умеренная полилиния дуги → `Duplicate` (approximation) | пройден |
| A7 отклонение больше tolerance → не `Duplicate` | пройден |
| A8 два соседних дуговых выреза не подавляют друг друга | пройден |
| A9 противоположный sweep → не `Duplicate` | пройден |
| A10 несколько дуг в одном Hole → `Duplicate` | пройден |

## 6. Smoke

Все работы — на копиях в `%TEMP%\EcadMcadBridge\smoke-000004\`.

| Плата | SHA256 до/после | JSON | Контуры | Cutout |
|---|---|---|---|---:|
| `board-with-cutout.PcbDoc` | `ED624C68…` | `board-snapshot-20261124T141947952Z.json` | 1 outer + 2 holes | 2 |
| `minipc.PcbDoc` | `50547E5E…` | `board-snapshot-20261124T142117744Z.json` | 1 outer | 0 |
| `bluetooth-sentinel.PcbDoc` | `1E18C3EF…` | `board-snapshot-20261124T142215361Z.json` | 1 outer | 0 |

### 6.1 Операторская плата

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

### 6.2 MiniPC

```text
pads=6004
totalRegions=382
boardCutoutRegions=1
clipCutouts=0
exportedCutoutContours=0
outlineExact=1
```

Ложных hole нет; layer stack region не экспортирован.

### 6.3 Bluetooth Sentinel

```text
boardCutoutRegions=3
clipCutouts=0
exportedCutoutContours=0
outlineExact=0
outlineFallback=1
outlinePartial=1
```

Fallback outer сохранён; ложных cutout и повторных associations нет.

## 7. Build и тесты

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release — 41/41 Core.Tests, 60/60 Altium.Tests, всего 101/101
```

## 8. Изменённые файлы

| Файл | Изменение |
|---|---|
| `src/EcadMcadBridge.Altium/Adapter/CutoutMatching.cs` | `AnalyticBounds`, geometry-first matching, относительный centroid, arc-aware dedup |
| `src/EcadMcadBridge.Altium/Adapter/AltiumOutlineReader.cs` | Диагностика analytic/sampled/region bounds |
| `src/EcadMcadBridge.Altium/Adapter/OutlineContourMatcher.cs` | `DistanceToPolyline` для dedup |
| `tests/EcadMcadBridge.Altium.Tests/AnalyticBoundsTests.cs` | B1–B12 |
| `tests/EcadMcadBridge.Altium.Tests/CutoutDeduplicationTests.cs` | A6–A10 |
| `.agent/SDK_API_LEDGER.md`, `.agent/ARCHITECTURE.md`, `.agent/PROJECT_CONTEXT.md`, `.agent/reports/BRIDGE-000003-report.md`, `AGENTS.md` | Актуализация |

Core public API, TFM и зависимости Core от Altium SDK не менялись.

## 9. Ограничения

- Вложенные острова Hole по-прежнему не представимы в Core 1.0.0.
- Approximation dedup — геометрическая эвристика с конечным допуском, а не
  доказательство математической идентичности в произвольной точности.
- Region matching geometry-first проверяет все BoardCutout sources; при большом
  числе регионов это может быть дороже, но на проверенных платах их 1–5.
- Реальная дедупликация на `GeometricPolygon` Hole не наблюдалась: у операторской
  платы 0 geometric holes; сценарий проверен unit-тестами A6–A10.

## 10. Commit / PR

- Commit реализации и документации:
  `5592622d5f95f52d3e16d040aae19b6f9c1eee7d`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
  (обновлён, не слит).
- Ветка: `task/BRIDGE-000003-altium-board-adapter`, база `source`.
- Force-push, rebase и merge не выполнялись.

## 11. История изменений

| Итерация | Результат |
|---|---|
| FIX-004 | Реальный Board Cutout через `IPCB_Region` + `ClipWithPolyRegShapes` |
| FIX-005 | Dedup по `geometric.Points`, uniqueness/association, CCW-wrap дуг |
| FIX-006 | Analytic arc bounds, geometry-first matching, arc-aware dedup |
