---
id: BRIDGE-000003-FIX-004
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T16:05:08+03:00
updated: 2026-11-24T16:05:08+03:00
base: source
branch: task/BRIDGE-000003-altium-board-adapter
commit: 910467d64b93f8d94cc0ade07993586ba13a0f15
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
related: [BRIDGE-000003, BRIDGE-000003-FIX-001, BRIDGE-000003-FIX-002, BRIDGE-000003-FIX-003, BRIDGE-000003-FIX-004]
---

# BRIDGE-000003-FIX-004 — реальный Board Cutout через IPCB_Region и ClipWithPolyRegShapes

## 1. Итог

**FIX-004 выполнен: реальный Board Cutout найден, извлечён и проверен. V-22 закрыт.**

- В `board with cutout.PcbDoc` найдены 5 `IPCB_Region` с
  `TRegionKind.eRegionKind_BoardCutout`.
- `IPCB_BoardOutline.ClipWithPolyRegShapes(false, false)` вернул 2 cutout-контура
  (16 сегментов).
- Оба clip-контура сопоставлены с Board Cutout регионами по bounds:
  shape 0 → region 1, shape 1 → region 0.
- В `BoardSnapshot` экспортированы 2 hole-контура:
  - 7 line segments;
  - 6 line segments + 3 arc segments.
- Все вершины обоих hole-контуров находятся внутри outer-контура (14/14 и 18/18).
- `ClipWithPolyRegShapes(false, false)` не включил pad/via holes: на `MiniPC.PcbDoc`
  с 6004 падами и 382 регионами получено 0 cutout-контуров.
- `OutlineContourMatcher` усилен: проверяются середины дуг/линий **самого analytic loop**,
  поэтому дуга не может «выпучиваться» за polyline и получать ложный exact.

`V-22` переведён в закрытые; `Q-014` закрыт.

## 2. Исходная плата и рабочая копия

| Параметр | Значение |
|---|---|
| Исходный файл | `C:\Users\Anton\Desktop\board with cutout.PcbDoc` |
| Размер | 18 525 184 байт |
| mtime | `2026-11-24T14:09:36.7750000+03:00` |
| SHA256 до/после | `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753` |
| Рабочая копия | `%TEMP%\EcadMcadBridge\smoke-000004\board-with-cutout.PcbDoc` |
| SHA256 копии до/после | `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753` |

Исходный файл не редактировался и не сохранялся. Рабочая копия после экспорта
осталась byte-identical; mtime не изменился.

## 3. Доказательства Altium API

Altium Designer Enterprise `26.12.2.9`, Add-in `0.2.0`.

### 3.1 Инвентаризация регионов

`board with cutout.PcbDoc`, 17 регионов:

| Kind | Кол-во |
|---|---:|
| `eRegionKind_BoardCutout` | 5 |
| `eRegionKind_Copper` | 8 |
| `eRegionKind_Cutout` | 4 |

Board Cutout регионы:

| Index | Имя | Точек | Area | Bounds (IU) |
|---:|---|---:|---:|---|
| 0 | — | 36 | 1 558 906 369 660 | (607 862 528, 402 521 330) – (610 018 040, 403 604 007) |
| 1 | — | 7 | 1 403 331 671 678 | (601 663 387, 400 423 228) – (602 539 373, 402 066 928) |
| 5 | Layer Stack Region 2 | 66 | 81 296 988 078 430 | (600 931 076, 395 551 177) – (611 175 225, 405 393 706) |
| 6 | Layer Stack Region 1 | 66 | 81 295 953 870 040 | (600 931 076, 383 740 152) – (611 175 225, 393 582 680) |
| 7 | Layer Stack Region 3 | 13 | 51 151 492 708 628 | (590 305 115, 393 582 146) – (621 801 187, 395 551 186) |

Только регионы 0 и 1 дали clip cutout. Регионы 5–7 — layer stack regions;
`ClipWithPolyRegShapes` их вырезами не считает, поэтому они остаются диагностикой
и не попадают в контур.

### 3.2 Clip result

```text
clipCutouts=2
clipNewPrims=1
clipInoperable=0
clipCutoutContours=2
clipCutoutSegments=16
clipCutoutErrors=0
clipMatchedRegions=2
clipUnmatchedRegions=0
```

Лог сопоставления:

```text
Clipped cutout shape 0 matched Board Cutout region 1 (area=1403331671678).
Clipped cutout shape 1 matched Board Cutout region 0 (area=1558906369660).
```

### 3.3 Изоляция pad/via holes

`ClipWithPolyRegShapes(false, false)` вызван с `useHolesFromViaPad=false`,
`useEmbedded=false`. Проверка на плате с большим числом отверстий:

| Плата | Падов | Регионов | BoardCutout-регионов | clipCutouts | Экспортировано cutout |
|---|---:|---:|---:|---:|---:|
| `MiniPC.PcbDoc` | 6004 | 382 | 1 | 0 | 0 |
| `Bluetooth_Sentinel.PcbDoc` | 224 | 15 | 3 | 0 | 0 |

`MiniPC.PcbDoc` содержит `eRegionKind_BoardCutout` («Layer Stack Region 1»), но
клиппер вернул 0. Это доказывает, что одного kind региона недостаточно и что
pad/via holes не подмешиваются. Adapter не экспортирует BoardCutout-регион без
подтверждения клиппером.

## 4. Результат в JSON

Файл: `%TEMP%\EcadMcadBridge\board-snapshot-20261124T130130783Z.json`
(315 229 байт). Валидатор: 0 ошибок.

`board.outline.contours`:

| Index | Role | Сегменты | Типы | Bounds (IU) | Closed |
|---:|---|---:|---|---|---|
| 0 | `outer` | 140 | 140 line | (590 305 120, 383 740 157) – (621 801 182, 405 393 701) | да |
| 1 | `hole` | 7 | 7 line | (601 663 387, 400 423 228) – (602 539 373, 402 066 928) | да |
| 2 | `hole` | 9 | 6 line + 3 arc | (607 862 528, 402 521 330) – (610 018 040, 403 604 007) | да |

Дуги hole 2:

| Start | End | Center | Radius IU | Radius мм | Sweep |
|---|---|---|---:|---:|---|
| (607 862 528, 403 161 093) | (608 502 291, 402 521 330) | (608 502 291, 403 161 093) | 639 763 | 1.624998 | CCW |
| (609 742 449, 402 521 330) | (610 018 040, 402 796 921) | (609 742 449, 402 796 921) | 275 591 | 0.700001 | CW |
| (608 147 961, 403 604 007) | (607 862 528, 403 318 574) | (608 147 961, 403 318 574) | 285 433 | 0.724999 | CCW |

Проверка вложенности: все 14 вершин hole 1 и все 18 вершин hole 2 находятся
внутри outer; 0 вершин снаружи.

## 5. Изменённые файлы

| Файл | Изменение |
|---|---|
| `src/EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter.cs` | Инвентаризация `IPCB_Region`, диагностика kind/name/area/contours/bounds; геометрия читается только для BoardCutout |
| `src/EcadMcadBridge.Altium/Adapter/AltiumOutlineReader.cs` | `ClipWithPolyRegShapes(false,false)`, разбор `IPCB_Shapes`/`IPCB_RegionShape`, сопоставление с BoardCutout по bounds, экспорт hole-контуров |
| `src/EcadMcadBridge.Altium/Adapter/OutlineContourMatcher.cs` | Проверка середин дуг/линий самого loop против geometric polyline |
| `tests/EcadMcadBridge.Altium.Tests/OutlineContourMatcherTests.cs` | +2 теста: arc bulge отклоняется; та же polyline без дуги принимается |
| `.agent/SDK_API_LEDGER.md` | C-85…C-88; V-22 закрыт; V-24 уточнён |
| `.agent/ARCHITECTURE.md`, `.agent/PROJECT_CONTEXT.md`, `AGENTS.md`, `README.md` | Актуализирован текущий этап и источник Board Cutout |

Core, TFM и публичные контракты не менялись.

## 6. Принятые решения и обоснование

1. **Источник cutout — clip result, а не kind региона.**
   `eRegionKind_BoardCutout` может быть layer stack region (`MiniPC.PcbDoc`).
   Поэтому регион без clip cutout не экспортируется как hole.
2. **`GeometricPolygon.GetState_IsHole` не является источником cutout.**
   На `board with cutout.PcbDoc` он по-прежнему даёт 0 holes, хотя реальные cutout
   есть. Это зафиксировано в ledger и документации.
3. **`ClipWithPolyRegShapes(false,false)` — единственное подтверждённое средство
   получить cutout-геометрию с дугами.**
   Параметры `false,false` исключают pad/via holes и embedded cutouts; это проверено
   на `MiniPC.PcbDoc` и `Bluetooth_Sentinel.PcbDoc`.
4. **Сопоставление clip shape ↔ BoardCutout region по bounds.**
   Допуск 10 IU. Оба clip-контура оператора совпали с регионами 0 и 1; несовпавший
   shape не экспортируется, а получает warning.
5. **Matcher не принимает дугу, выпуклую за polyline.**
   Ранее проверялись только вершины и середины geometric-сегментов. Для дуги,
   соединяющей вершины polyline, этого недостаточно: добавлена проверка середины
   дуги/линии самого loop. Exact стал консервативнее, без ложноположительных
   результатов.
6. **Line-fallback для outer сохранён.**
   Для `board with cutout.PcbDoc` outer остаётся fallback (140 line segments), так как
   geometric polyline не совпадает с analytic loop. Это честно диагностируется.

## 7. Неподтверждённые вопросы и риски

- **Вложенные острова внутри cutout.** `IPCB_RegionShape` может вернуть несколько
  замкнутых loops; adapter экспортирует каждый как `hole`. Core 1.0.0 не описывает
  вложенность hole-в-hole, поэтому остров внутри выреза может быть представлен
  неоднозначно. На проверенной плате таких случаев нет.
- **`GetNewPrims` / `GetInoperablePrims`.** Счётчики читаются для диагностики, но
  геометрия `GetNewPrims` не используется. Семантика подтверждена только косвенно.
- **Layer stack regions.** `eRegionKind_BoardCutout` с именем `Layer Stack Region *`
  не являются вырезами на проверенных платах. Нельзя строить cutout только по kind.
- **Pad/via holes.** Core по-прежнему не содержит pad holes (V-19); adapter их не
  экспортирует и не смешивает с Board Cutout.
- **Outer exact.** Для сложных плат outer может оставаться line-fallback; это
  ограничение исходного `GeometricPolygon`, а не cutout-логики.

## 8. Проверки

### Build / tests

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release — 41/41 Core.Tests, 26/26 Altium.Tests, всего 67/67
```

### Smoke

| Плата | SHA256 до/после | JSON | Контуры | Cutout |
|---|---|---|---:|---|
| `board-with-cutout.PcbDoc` | `ED624C68…` | `board-snapshot-20261124T130130783Z.json` | 1 outer + 2 holes | 2 (7 line; 6 line + 3 arc) |
| `minipc.PcbDoc` | `50547E5E…` | `board-snapshot-20261124T130256761Z.json` | 1 outer | 0 |
| `bluetooth-sentinel.PcbDoc` | `1E18C3EF…` | `board-snapshot-20261124T130352120Z.json` | 1 outer | 0 |

Все SHA256 до/после совпали; исходный файл оператора и рабочие копии не изменены.
Smoke выполнен через меню `PCB → Tools → Export Board Snapshot` в AD26.12.2.9.

## 9. Commit / PR

- Commit кода и документации: `910467d64b93f8d94cc0ade07993586ba13a0f15`.
- Ветка: `task/BRIDGE-000003-altium-board-adapter`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5 (обновлён, не слит).
- `source` не изменялся; force-push не выполнялся.

## 10. История изменений

| Итерация | Результат |
|---|---|
| FIX-001 | Все geometric contours сохраняются; per-contour fallback; PadShape exact/simplified/unsupported |
| FIX-002 | Exact matcher отвергает partial/ambiguous; V-22 открыт |
| FIX-003 | Arc angular range + geometric segment midpoints; V-22 открыт |
| FIX-004 | IPCB_Region + ClipWithPolyRegShapes; 2 реальных cutout-контура; V-22 закрыт |
