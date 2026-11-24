---
id: BRIDGE-000003
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T12:45:00+03:00
updated: 2026-11-24T17:00:00+03:00
base: source
branch: task/BRIDGE-000003-altium-board-adapter
commit: cb509bc62b01609abe74d46d83236b0691a5f7bc
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
related: [BRIDGE-000003, BRIDGE-000002, D-005, D-009, D-010, D-015]
---

# BRIDGE-000003 — Altium BoardSnapshot Adapter

## 1. Итог

Реализован первый вертикальный срез экспорта из Altium Designer 26 в нейтральную
`BoardSnapshot`:

- слои платы;
- компоненты и placement, Top/Bottom, rotation;
- пады и per-layer `PadStack`/`PadShape`;
- nets;
- контур платы с линиями и аналитическими дугами, с поддержкой ролей outer/hole.

Добавлена read-only команда `EcadMcadBridge:ExportBoardSnapshot`; существующая
`ExportSmokeSnapshot` сохранена без изменения назначения. Adapter не содержит JSON,
не зависит от SOLIDWORKS и не меняет PCB.

Не реализовано в этой задаче: tracks, vias, regions, text items, Exchange Package,
3D-модели компонентов, автоматическая установка Add-in и стабильная стратегия ID.

## 2. Файлы

Новые:

```text
src/EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter.cs
src/EcadMcadBridge.Altium/Adapter/AltiumOutlineReader.cs
src/EcadMcadBridge.Altium/Application/ExportBoardSnapshotHandler.cs
src/EcadMcadBridge.Core/Diagnostics/AngleNormalizer.cs
tests/EcadMcadBridge.Core.Tests/AngleNormalizerTests.cs
.agent/reports/BRIDGE-000003-report.md
```

Изменены:

```text
src/EcadMcadBridge.Altium/Bootstrap/BridgeServerModule.cs
src/EcadMcadBridge.Altium/Bootstrap/PluginFactory.cs
src/EcadMcadBridge.Altium/Application/ExportSmokeSnapshotHandler.cs
src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.ins
src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.rcs
.agent/SDK_API_LEDGER.md
.agent/ARCHITECTURE.md
.agent/PROJECT_CONTEXT.md
README.md
```

Новых проектов, TFM, зависимостей и изменений Core-модели (кроме neutral
`AngleNormalizer`) нет. Бинарные платы и пользовательские данные в Git не добавлены.

## 3. Архитектура adapter

`AltiumBoardSnapshotAdapter.Read(view)`:

1. получает активную PCB через существующий `PCB.GlobalVars.PCBServer`;
2. читает слои через `LayerIterator` (`LayerRef`, V6-ID, имя, kind, side);
3. читает nets через board iterator `eNetObject`;
4. читает компоненты через board iterator `eComponentObject`, placement и высоту;
5. для каждого компонента читает пады через `GroupIterator` и создаёт `PadStack`
   с shape на каждом copper-слое, где пад имеет геометрию;
6. читает контур через `BoardOutline_GeometricPolygon` (роли) и
   `IPCB_BoardOutline` как `IPCB_Polygon` (`GetState_Segments`) для сохранения дуг;
7. возвращает `BoardSnapshot` + список предупреждений adapter.

`ExportBoardSnapshotHandler`:

- вызывает adapter;
- пишет adapter warnings и diagnostics в `%TEMP%\EcadMcadBridge\logs\bridge.log`;
- валидирует `BoardSnapshotValidator`;
- при ошибках не пишет JSON и показывает предупреждение;
- при успехе пишет `%TEMP%\EcadMcadBridge\board-snapshot-<UTC>.json` через
  `BoardSnapshotJson.SerializeToUtf8`;
- показывает путь и счётчики.

## 4. Altium API → Core

| Altium | Core / назначение |
|---|---|
| `IPCB_Board.LayerIterator()` + `IPCB_LayerObject` | `LayerRef`: V6-ID, имя, kind, side |
| `IPCB_Board.GetState_MasterStack()` + `Get_ZTop/Get_ZBottom` | `BoardProperties.Thickness` |
| `IPCB_Component` | `ComponentInstance` + `Transform` + `SourceUniqueId` |
| `IPCB_Group.GroupIterator_Create` (`ePadObject`) | пады компонента |
| `IPCB_Pad` + copper layer shapes | `Pad`, `PadStack`, `PadShape` |
| `IPCB_Net` | `Net` |
| `IPCB_BoardOutline.BoardOutline_GeometricPolygon()` | роли контуров outer/hole |
| `IPCB_BoardOutline` как `IPCB_Polygon.GetState_Segments()` | `LineSegment`/`ArcSegment` |
| `PolySegment.Kind/Vx/Vy/Cx/Cy/Radius/Angle1/Angle2` | сохранение дуг без аппроксимации |

Подтверждение сигнатур: метаданные `Altium.SDK.Interfaces.dll` AD26.12.2.9 (`ildasm`)
и успешный smoke-тест. Подробности — `SDK_API_LEDGER.md`, C-73…C-84.

## 5. Что заполняется в Core

- `Source`: cad/version/file name/time/tool version;
- `BoardProperties`: id, name, thickness (если stack вернул ненулевые Z);
- `BoardOutline`: контуры, outer/hole, линии и дуги;
- `Layers`: Top/Mid/Bottom/Plane/Overlay/Paste/Solder/Mechanical/Multi-Layer;
- `Components`: designator/comment/footprint/sourceUniqueId/layer/placement/height;
- `PadStacks`: shape per copper layer, kind/width/height/cornerRadius для oval;
- `Pads`: component/net/layer/padStack/global position/rotation;
- `Nets`: name и source id.

Не заполняется:

- tracks/vias/regions/texts — вне scope;
- pad holes/plating — в Core 1.0.0 нет полей;
- pin designator и локальные координаты пада — в Core нет полей;
- stack order и признак 3D-модели — в Core нет полей;
- fitted/DNP — API не подтверждено для PCB-компонента;
- внутренние Board Cutout — поддержаны и подтверждены на `board with cutout.PcbDoc`.

## 6. Тестовые платы и smoke

Использованы vendor-примеры AD26, скопированные в `%TEMP%\EcadMcadBridge\smoke-000003\`
и не добавленные в Git:

1. `MiniPC.PcbDoc` — 16 copper-слоёв (signal/plane), Top/Bottom, 1046 компонентов,
   6004 пада, 837 сетей, multi-layer пады, rotation `135°`.
2. `Bluetooth_Sentinel.PcbDoc` — контур с 2 дугами и 10 линиями, Top/Bottom,
   73 компонента, 224 пада, 57 сетей.

Smoke выполнен в AD26.12.2.9 через реальный Add-in и команду `Export Board Snapshot`.

### MiniPC.PcbDoc

- layers: 31;
- components: 1046 (Top 368, Bottom 678);
- pads: 6004;
- nets: 837;
- padStacks: 6004, shapes: 9669;
- shapes: rectangular 3690, circular 5237, roundedRectangle 742;
- rotations: 0° — 244, 90° — 251, −90° — 228, −180° — 322, 135° — 1;
- outline: 1 контур, 6 линий (дуг на этой плате нет);
- thickness: 790552 IU;
- adapter warnings: 12 (pad holes, oval mapping, пустые MH PadStacks);
- validation warnings: 1029 (в основном пады без net и необязательные поля);
- validation errors: 0;
- SHA256 до и после:
  `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93`;
- JSON: `%TEMP%\EcadMcadBridge\board-snapshot-20261124T094155045Z.json`.

### Bluetooth_Sentinel.PcbDoc

- layers: 17;
- components: 73 (Top 72, Bottom 1);
- pads: 224;
- nets: 57;
- padStacks: 224, shapes: 224;
- rotations: 0°, ±90°, 180°;
- outline: 1 outer-контур, 12 сегментов (10 линий + 2 аналитические дуги),
  radius 5118110 IU, sweep `clockwise`;
- thickness: 313581 IU;
- adapter warnings: 1 (oval pads mapped to rounded rectangle);
- validation warnings: 28;
- validation errors: 0;
- SHA256 до и после:
  `1E18C3EFA9D6C3245AA09CAB7268B2A908078E50ED2D09DC9D6DFD3BC36B95F3`;
- JSON: `%TEMP%\EcadMcadBridge\board-snapshot-20261124T094350604Z.json`.

На этих vendor-платах внутренних вырезов нет. Реальный Board Cutout подтверждён
отдельно на `board with cutout.PcbDoc` через `IPCB_Region` +
`ClipWithPolyRegShapes(false,false)`; см. разделы 7.4 и 7.5.

## 7. Ограничения и вопросы

| Ограничение / вопрос | Статус |
|---|---|
| Core `LayerKind` не различает signal и internal plane | Оба `Copper`; V-18/Q-013 |
| Core `Pad` не содержит hole/plating, pin designator, local coords | Adapter не подменяет; V-19/Q-012 |
| Core `PadShape` не содержит oval; `eRounded` с разными размерами | Rounded rectangle radius=min/2 + warning; V-20 |
| `eCustomShape` не имеет вершин в API | PadShape пропускается с warning; V-20 |
| Core не содержит stack order и 3D-model flag | Не заполняется; V-21 |
| Внутренние cutouts | Подтверждены на `board with cutout.PcbDoc`; V-22 закрыт, см. FIX-004/FIX-005 |
| `DefinitionLayerIterator` иногда отдаёт V7-ID, не совпадающие с V6 | Adapter перебирает copper-слои board layer iterator; V-23 |
| Стабильность `SourceUniqueId` | Не доказана; ID используются как provenance, D-010 остаётся Proposed |
| Vendor-платы содержат 1000+ validation warnings | В основном unconnected pads/optional fields; errors=0, JSON валиден |

Публичные контракты Core в этой задаче не расширялись. Вопросы требуют отдельного
архитектурного решения до добавления pad holes, plane kind, stack order и локальных
координат падов.

## 7.1 FIX-001 — достоверность outline и PadShape

- Board Outline теперь использует `GeometricPolygon` как источник **всех** контуров и
  ролей `Outer`/`Hole`; каждый geometric contour отдельно сопоставляется с аналитическим
  циклом. Несопоставленный контур не удаляется: он экспортируется line-fallback с
  предупреждением о возможной потере дуг. На `PC4-SODIMM` это дало
  `outlineFallback=1`, `outlineLoopErrors=6` и явные warning вместо потери контура.
- `TShape` разделён на точные, упрощённые и неподдерживаемые:
  - точные: `eRectangular`, `eCircleShape`, `eRounded` при равных размерах;
  - упрощённые с warning: `eOctagonal` (chamfered rectangle → Octagonal),
    rounded rectangles без подтверждённого corner radius, oval через rounded rectangle
    radius=min/2;
  - неподдерживаемые и не подменяемые: `eRotatedRectShape`, `eArcShape`, `eTerminator`,
    `eCustomShape`, `eNoShape`.
- Adapter diagnostics теперь явно различает `outlineExact`, `outlineFallback`,
  `outlineOmitted`, `outlineLoopErrors`, `padShapesExact`, `padShapesSimplified`,
  `padShapesUnsupported`, пустые PadStacks и счётчики по `TShape`.
- Добавлен отдельный тестовый проект `EcadMcadBridge.Altium.Tests` (net10.0-windows,
  без новой зависимости Core от Altium): 19 тестов PadShape/Outline matcher.
- Итоговые smoke-тесты current code:
  - MiniPC: 31 слой, 1046 компонентов, 6004 пада, 837 сетей, 9669 форм,
    `outlineExact=1`, `padShapesExact=8927`, `padShapesSimplified=742`,
    `padShapesUnsupported=0`, validation errors 0, SHA256
    `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93`;
  - Bluetooth: 17 слоёв, 73 компонента, 224 пада, 57 сетей, контур 12 сегментов
    (10 линий + 2 дуги), `outlineExact=1`, `padShapesExact=132`,
    `padShapesSimplified=92`, validation errors 0, SHA256
    `1E18C3EFA9D6C3245AA09CAB7268B2A908078E50ED2D09DC9D6DFD3BC36B95F3`.
- **Синтетический cutout smoke не выполнен**: не удалось безопасно создать/найти
  vendor-плату с внутренним вырезом без риска изменить исходную геометрию. Код использует
  `GetState_IsHole`, но критерий подтверждения cutout остаётся открытым (V-22).

## 7.2 FIX-002 — cutout и exact matcher

- Исходный файл оператора `%TEMP%\EcadMcadBridge\smoke-000003\minipc.PcbDoc`
  имеет размер 51,684,352 байт, mtime `2026-08-12T20:53:30+03:00`, SHA256
  `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93`.
  Этот SHA256 совпал с vendor-копией; по API `GeometricPolygon` содержит
  1 outer-контур, 6 точек, 0 hole-контуров. Вырезов в файле не обнаружено.
- Рабочая копия `%TEMP%\EcadMcadBridge\smoke-000003-fix-002\minipc-cutout-test.PcbDoc`
  сохранила тот же SHA256 до/после экспорта.
- Exact matcher теперь требует замкнутый/непрерывный цикл, покрытие уникальных
  аналитических вершин geometric polyline и покрытие всех geometric points
  аналитическими линиями/дугами. Одной общей точки недостаточно.
- На `Bluetooth_Sentinel.PcbDoc` старый false-exact заменён на fallback:
  `outlineExact=0`, `outlineFallback=1`, `outlinePartial=1`,
  `outlineUnmatchedLoops=1`; diagnostic: `uniqueLoop=12`, `uniqueContour=140`,
  `maxContourToLoop=4718.66 IU`. JSON содержит 140 line segments и warning;
  точная дуговая геометрия не выдаётся за подтверждённую.
- На рабочей копии MiniPC без cutouts: `outlineExact=1`, 1 контур, 6 линий,
  `geometricPoints=6`, `geometricHoles=0`.
- V-22 остаётся открытым: реальный cutout smoke не подтверждён.

## 7.3 FIX-003 — arc matching и новый cutout-файл

- Файл `C:\Users\Anton\Desktop\board with cutout.PcbDoc`: 18,525,184 байт,
  mtime `2026-11-24T14:09:36+03:00`, SHA256
  `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753`.
  Рабочая копия: `%TEMP%\EcadMcadBridge\smoke-000003-fix-003\board-with-cutout.PcbDoc`.
- API `GeometricPolygon`: **1 контур, 140 точек, 0 hole-контуров**. Внутренних
  Board Cutout в файле не обнаружено; V-22 остаётся открытым.
- JSON: 1 outer-контур, 140 line segments, `outlineExact=0`, `outlineFallback=1`,
  `outlinePartial=1`. SHA256 исходника и копии до/после совпал.
- Matcher теперь проверяет:
  - угловой диапазон дуги, а не полную окружность (CW/CCW, переход 0°/360°);
  - расстояние до конечных точек вне углового диапазона;
  - середины сегментов geometric polyline, а не только вершины;
  - уникальные вершины и замкнутость.
- Добавлены 5 arc/midpoint тестов; Altium.Tests: 24/24, Core.Tests: 41/41,
  всего 65/65.
- На `Bluetooth_Sentinel.PcbDoc` подтверждён fallback/partial (не false exact):
  `uniqueLoop=12`, `uniqueContour=140`, `maxContourToLoop=4718.66 IU`.

## 7.4 FIX-004 — реальный Board Cutout

- `board with cutout.PcbDoc`, SHA256 `ED624C68…`, 18 525 184 байт.
- 5 `IPCB_Region` с `eRegionKind_BoardCutout`; `ClipWithPolyRegShapes(false,false)`
  вернул 2 cutout-контура / 16 сегментов.
- В JSON экспортированы 2 hole-контура: 7 линий; 6 линий + 3 дуги. Все вершины
  внутри outer.
- `MiniPC.PcbDoc` содержит BoardCutout-регион, но клиппер вернул 0 вырезов;
  `Bluetooth_Sentinel.PcbDoc` — 3 BoardCutout-региона, 0 вырезов.
- SHA256 всех трёх плат до/после совпал. V-22 закрыт.

## 7.5 FIX-005 — дедупликация и Region matching

- `MatchesExistingGeometricHole` больше не сравнивает clip loop с самим собой:
  используются фактические точки `geometric.Points`; проверяются только
  `ContourRole.Hole`.
- Дубликат подавляется только при exact geometric match; partial overlap
  диагностируется и не скрывает cutout.
- Region matching: bounding box — broad phase (10 IU); затем вершины
  Region-контура (3500 IU), площадь (5%) и centroid (1000 IU). Результат:
  `Matched` / `Unmatched` / `Ambiguous`; связь хранится по
  `(RegionIndex, ContourIndex)`, повторное назначение запрещено.
- Исправлена трактовка `PolySegment.Angle1/Angle2`: дуга всегда CCW, `Angle2 < Angle1`
  означает переход через 0°. Ранее arc `270°→0°` ошибочно становился CW-дугой 270°.
- На операторской плате оба Hole сохранены; на MiniPC/Bluetooth ложных cutout нет.
  Altium.Tests: 43/43, Core.Tests: 41/41.

## 7.6 FIX-006 — analytic bounds и дуговой dedup

- Bounds аналитической дуги вычисляются по Start/End и кардинальным экстремумам,
  которые реально входят в sweep; sampling не используется. Поддержаны CW/CCW,
  переход 0°/360°, дуги менее 90°, более 180° и несколько дуг.
- Broad phase переведён в geometry-first: bounds остаются диагностикой/ранжированием
  и не могут отбросить корректный Region из-за пропущенного экстремума.
- Region matching: вершины 3500 IU, площадь 5%, centroid 1% диагонали с минимумом
  100 IU; это отделяет tessellation от переноса всей фигуры.
- Dedup дуговых Hole: exact 2 IU, затем отдельный approximation 3500 IU с
  двусторонним покрытием, площадью 5% и centroid 100 IU. Partial не подавляет export.
- Добавлены A6–A10 и B1–B12; Altium.Tests: 60/60, Core.Tests: 41/41.
- На операторской плате сохранены оба Hole; на MiniPC/Bluetooth ложных cutout нет.

## 8. Проверки

```text
dotnet build -c Release
Сборка успешно завершена.
Предупреждений: 0
Ошибок: 0

dotnet test -c Release
Core.Tests: 41/41
Altium.Tests: 60/60
```

Текущее состояние: 41 Core + 60 Altium = 101 тест. Добавлены FIX-004/FIX-005/FIX-006
тесты дедупликации, Region matching, analytic bounds, arc matching и arc-angle wrap.

## 9. Commit SHA и PR

- Commit (состояние PR на момент создания): `cb509bc62b01609abe74d46d83236b0691a5f7bc`.
- FIX-004: `910467d64b93f8d94cc0ade07993586ba13a0f15` (реализация),
  `d1f67ef86b2dbc42449bd4715108a7f3f14a46bd` (отчёт).
- FIX-005: коммит и фактический HEAD указаны в
  `.agent/reports/BRIDGE-000003-FIX-005-report.md` после выполнения.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5 — база `source`,
  ветка `task/BRIDGE-000003-altium-board-adapter`, открыт и не слит автором.

## 10. История изменений

| Дата | Изменение |
|---|---|
| 2026-11-24 | Первая версия: adapter, слои, компоненты, пады, nets, контур, JSON, smoke. |
| 2026-11-24 | Добавлены commit SHA и URL PR #5; PR создан, не слит. |
| 2026-11-24 | FIX-001: все geometric contours сохраняются, fallback и PadShape-преобразования диагностируются; tests 56. |
| 2026-11-24 | FIX-002: exact matcher отвергает partial/ambiguous; cutout-файл оператора без вырезов, V-22 открыт; tests 60. |
| 2026-11-24 | FIX-003: arc angular range/midpoint matching; новый cutout-файл оператора снова дал 0 holes; tests 65. |
