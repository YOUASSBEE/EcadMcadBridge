---
id: BRIDGE-000003-FIX-003
type: report
status: blocked
from: local-agent
to: operator
created: 2026-11-24T14:20:35+03:00
updated: 2026-11-24T14:20:35+03:00
base: source
branch: task/BRIDGE-000003-altium-board-adapter
commit: pending
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
related: [BRIDGE-000003, BRIDGE-000003-FIX-001, BRIDGE-000003-FIX-002, BRIDGE-000003-FIX-003]
---

# BRIDGE-000003-FIX-003 — Board Cutout и arc matcher

## 1. Итог

**FIX-003-B (arc/outline matching): выполнен.**
**FIX-003-A (реальный Board Cutout): заблокирован входной платой.** API новой платы
оператора вернул 1 outer-контур, 140 точек и **0 hole-контуров**. Внутренних
Board Cutout в файле нет, поэтому V-22 не закрывается.

## 2. Исходная плата и рабочая копия

| Параметр | Значение |
|---|---|
| Исходный файл | `C:\Users\Anton\Desktop\board with cutout.PcbDoc` |
| Размер | 18,525,184 байт |
| mtime | `2026-11-24T14:09:36.7750000+03:00` |
| SHA256 до/после | `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753` |
| Рабочая копия | `%TEMP%\EcadMcadBridge\smoke-000003-fix-003\board-with-cutout.PcbDoc` |
| SHA256 копии до/после | `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753` |

Исходный файл не редактировался и не сохранялся; рабочая копия после экспорта
не изменилась.

## 3. Altium и API-геометрия

Altium Designer Enterprise `26.12.2.9`.

`BoardOutline_GeometricPolygon()`:

| Index | Role | Points | Closed | Holes |
|---|---|---:|---|---:|
| 0 | Outer | 140 | да (после fallback) | 0 |

`GetState_IsHole(0) = false`. В файле **нет внутренних контуров**, поэтому
проверить извлечение Board Cutout невозможно. Обычные pad holes отдельно
обрабатываются адаптером и не входят в `BoardOutline`.

JSON:

- contours: 1;
- role: `outer`;
- segments: 140 × `LineSegment`;
- `outlineExact=0`, `outlineFallback=1`, `outlinePartial=1`,
  `outlineUnmatchedLoops=1`;
- validation errors: 0.

Это корректный fallback: analytic loop имеет 12 уникальных вершин, geometric
polygon — 140 точек, максимальное отклонение `maxContourToLoop=4718.66 IU`.
Геометрия не выдаётся за точную.

## 4. FIX-003-B — arc matcher

`OutlineContourMatcher` теперь:

- проверяет принадлежность точки **ограниченной дуге**, а не полной окружности;
- учитывает `SweepDirection` (CW/CCW);
- корректно обрабатывает переход через 0°/360°;
- для точки вне углового диапазона считает расстояние до конечных точек;
- проверяет середины сегментов geometric polyline, а не только вершины;
- требует не менее трёх уникальных вершин и замкнутый/непрерывный цикл;
- не принимает частичное/неоднозначное совпадение за exact.

Добавлены тесты:

1. точка на окружности вне углового диапазона;
2. точка внутри диапазона;
3. CW;
4. CCW;
5. переход через 0°/360°;
6. конечные точки дуги;
7. отклонение polyline между вершинами;
8. корректная дуга с допустимой погрешностью;
9. разрыв/частичное совпадение → fallback.

## 5. Smoke и SHA256

| Плата | API contours | JSON | Exact/Fallback | SHA256 до/после |
|---|---:|---|---|---|
| `board-with-cutout.PcbDoc` | 1 outer / 0 holes | 1 outer, 140 lines | 0 / 1 | `ED624C68…` |
| `MiniPC.PcbDoc` | 1 outer / 0 holes | 1 outer, 6 lines | 1 / 0 | `50547E5E…` |
| `Bluetooth_Sentinel.PcbDoc` | 1 contour / 140 points | 1 outer, 140 lines | 0 / 1 | `1E18C3EF…` |

Все SHA256 до/после совпали. Внутренних вырезов в новой плате оператора нет;
V-22 остаётся открытым.

## 6. Unit tests

```text
Core.Tests: 41/41
Altium.Tests: 24/24
Всего: 65/65
```

Новые тесты arc/midpoint входят в `EcadMcadBridge.Altium.Tests`.

## 7. Build / test

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release — 41/41 Core.Tests, 24/24 Altium.Tests
```

## 8. V-22

V-22 **не закрыт**. Файл оператора снова содержит 1 outer-контур и 0 hole-контуров,
хотя его SHA256 отличается от vendor-копии. Для закрытия V-22 нужен файл, в котором
`GeometricPolygon.GetState_IsHole()` действительно возвращает hole-контур.

## 9. Commit / HEAD / PR

- Commit: `pending`.
- HEAD: `pending`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5 (не слит).

## 10. Оставшиеся вопросы

- Нужен реально сохранённый Board Cutout с hole-контуром; визуального вида платы
  недостаточно.
- Для `Bluetooth_Sentinel`/`board-with-cutout` exact не подтверждён; текущий
  line-fallback корректен, но означает потерю аналитических дуг в JSON.
- После предоставления корректного cutout-файла повторить smoke и обновить V-22
  только для проверенной конфигурации AD26.12.2.9.
