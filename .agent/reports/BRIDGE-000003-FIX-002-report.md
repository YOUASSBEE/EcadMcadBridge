---
id: BRIDGE-000003-FIX-002
type: report
status: blocked
from: local-agent
to: operator
created: 2026-11-24T14:00:24+03:00
updated: 2026-11-24T14:00:24+03:00
base: source
branch: task/BRIDGE-000003-altium-board-adapter
commit: pending
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
related: [BRIDGE-000003, BRIDGE-000003-FIX-001, BRIDGE-000003-FIX-002]
---

# BRIDGE-000003-FIX-002 — Board Cutout и Outline Matching

## 1. Итог и статус

**FIX-002-B (Outline Matcher): выполнен.** Exact matching больше не принимает
частичное совпадение за точное.

**FIX-002-A (реальный Board Cutout): заблокирован входным файлом.** Файл оператора
`minipc.PcbDoc` побайтно совпал с vendor-копией; Altium API вернул только один
outer-контур, 6 точек, 0 hole-контуров. В файле нет Board Cutout, поэтому проверить
извлечение вырезов невозможно. V-22 остаётся открытым.

## 2. Исходный файл и рабочая копия

| Параметр | Значение |
|---|---|
| Исходный файл | `%TEMP%\EcadMcadBridge\smoke-000003\minipc.PcbDoc` |
| Размер | 51,684,352 байт |
| mtime | `2026-08-12T20:53:30+03:00` |
| SHA256 до/после | `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93` |
| Рабочая копия | `%TEMP%\EcadMcadBridge\smoke-000003-fix-002\minipc-cutout-test.PcbDoc` |
| SHA256 рабочей копии до/после | `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93` |

Исходный файл не редактировался и не сохранялся. Рабочая копия также не изменилась
после экспорта. SHA256 совпал с vendor-копией `MiniPC.PcbDoc` из примеров AD26;
это указывает, что подготовленные вырезы в указанный файл фактически не попали.

## 3. Altium и API-геометрия

Altium Designer Enterprise `26.12.2.9`.

Для рабочей копии `BoardOutline_GeometricPolygon()` вернул:

| Index | Role | Point count | Closed | Примечание |
|---|---|---:|---|---|
| 0 | Outer | 6 | Yes (analytic loop matched) | Внешний контур платы |

`GeometricHoleCount = 0`. Внутренних контуров/вырезов в файле нет.
Обычные pad holes не превращаются в контуры Board Outline: adapter обрабатывает
пады отдельно, а `BoardOutline` строится только из `GeometricPolygon`.

JSON рабочей копии:

- contours: 1;
- role: `outer`;
- segments: 6 × `LineSegment`;
- arcs: 0;
- `outlineExact=1`, `outlineFallback=0`, `outlineOmitted=0`;
- validation errors: 0.

## 4. Аналитические дуги и fallback

На рабочей копии дуг нет: контур состоит из 6 прямых сегментов.
Поэтому проверить дуговые cutouts на этом файле невозможно. Для сравнения,
на `Bluetooth_Sentinel.PcbDoc` новый matcher обнаружил, что старый exact был
ложным: `uniqueLoop=12`, `uniqueContour=140`, `maxLoopToContour=1.00 IU`,
`maxContourToLoop=4718.66 IU`. Результат:

- `outlineExact=0`;
- `outlineFallback=1`;
- `outlinePartial=1`;
- `outlineUnmatchedLoops=1`;
- JSON: 140 line segments, warning о частичном совпадении и fallback.

Это ожидаемое безопасное поведение: приблизительная/частичная геометрия не
маркируется как exact. Точная дуговая геометрия для этого контура не подтверждена.

## 5. Новый Outline Matcher

`OutlineContourMatcher` больше не использует порог `score > 0`. Exact допускается
только если:

1. аналитический цикл замкнут и непрерывен;
2. уникальные вершины аналитического цикла лежат на geometric polyline в пределах
   целочисленного допуска `2 IU`;
3. все уникальные точки geometric contour лежат на аналитических линиях/дугах
   в пределах того же допуска;
4. повторяющиеся Start/End соседних сегментов считаются одной уникальной точкой;
5. порядок обхода/начальная точка не важны.

Если exact-кандидатов несколько — `outlineAmbiguous`, fallback и warning.
Если совпадение только частичное — `outlinePartial`, fallback и warning.
Если цикл не замкнут/не непрерывен — он не используется.

## 6. Диагностика

`AltiumAdapterDiagnostics` теперь содержит:

- `outlineExact`, `outlineFallback`, `outlineOmitted`, `outlineLoopErrors`,
  `outlineUnmatchedLoops`, `outlineAmbiguous`, `outlinePartial`;
- `geometricContours`, `geometricPoints`, `geometricHoles`;
- `padShapesExact`, `padShapesSimplified`, `padShapesUnsupported`, `padStacksEmpty`;
- счётчики по `TShape`.

Сводка пишется в лог как `ADAPTER_SUMMARY`.

## 7. Unit tests

Добавлены/обновлены тесты `OutlineContourMatcherTests`:

1. полное совпадение с другим начальным положением цикла;
2. одна общая точка не даёт exact;
3. частичное совпадение не даёт exact;
4. два одинаковых exact-кандидата обнаруживаются как неоднозначность;
5. повторяющиеся Start/End не завышают число вершин;
6. сдвиг начального сегмента не ломает exact;
7. незамкнутый цикл не даёт exact.

Итог:

```text
Core.Tests: 41/41
Altium.Tests: 19/19
Всего: 60/60
```

## 8. Smoke

| Плата | Contours API | Contours JSON | Outline status | SHA256 |
|---|---:|---:|---|---|
| `minipc-cutout-test.PcbDoc` | 1 outer / 0 holes | 1 outer, 6 lines | exact | `50547E5E…` |
| `Bluetooth_Sentinel.PcbDoc` | 1 contour / 140 points | 1 outer, 140 lines | fallback/partial | `1E18C3EF…` |
| `MiniPC.PcbDoc` | 1 outer / 0 holes | 1 outer | exact | `50547E5E…` |

Все SHA256 до/после экспорта совпали. Внутренних Board Cutout в предоставленном
оператором файле нет; критерий cutout smoke не выполнен.

## 9. Build / test

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release — 41/41 Core.Tests, 19/19 Altium.Tests
```

## 10. V-22

V-22 **не закрыт**. Проверенный `minipc.PcbDoc` содержит 0 hole-контуров и
побайтно совпадает с vendor-копией. Для закрытия нужен реально изменённый файл
с внутренним вырезом и повторный smoke в AD26.12.2.9.

## 11. Commit / HEAD / PR

- Commit: `pending`.
- HEAD: `pending`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5 (не слит).

## 12. Оставшиеся вопросы

- Оператору нужно подтвердить/передать файл с реально сохранёнными Board Cutout.
- После этого повторить smoke, проверить роли/координаты/дуги и обновить V-22.
- Для `Bluetooth_Sentinel` exact не подтверждён; текущий fallback корректен, но
  означает потерю аналитических дуг в JSON. Требуется отдельное исследование
  сопоставления 12 analytic segments с 140-point geometric contour.
