---
id: BRIDGE-000003-FIX-001
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T13:22:03+03:00
updated: 2026-11-24T13:22:03+03:00
base: source
branch: task/BRIDGE-000003-altium-board-adapter
commit: pending
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5
related: [BRIDGE-000003, BRIDGE-000003-FIX-001]
---

# BRIDGE-000003-FIX-001 — достоверность Board Outline и PadShape

## 1. Статус замечаний

| Замечание | Статус |
|---|---|
| A. Все контуры Board Outline, включая вырезы | Исправлено в коде; synthetic cutout smoke не выполнен |
| B. Незаметная потеря аналитической геометрии | Исправлено: per-contour fallback + warning |
| C. Некорректное преобразование `TShape` | Исправлено: exact/simplified/unsupported разделены |

## 2. Изменённые файлы

```text
src/EcadMcadBridge.Altium/Adapter/AltiumOutlineReader.cs
src/EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter.cs
src/EcadMcadBridge.Altium/Adapter/OutlineContourMatcher.cs
src/EcadMcadBridge.Altium/Application/ExportBoardSnapshotHandler.cs
src/EcadMcadBridge.Altium/EcadMcadBridge.Altium.csproj
tests/EcadMcadBridge.Altium.Tests/EcadMcadBridge.Altium.Tests.csproj
tests/EcadMcadBridge.Altium.Tests/PadShapeMappingTests.cs
tests/EcadMcadBridge.Altium.Tests/OutlineContourMatcherTests.cs
EcadMcadBridge.sln
.agent/SDK_API_LEDGER.md
.agent/ARCHITECTURE.md
.agent/PROJECT_CONTEXT.md
.agent/reports/BRIDGE-000003-report.md
.agent/reports/BRIDGE-000003-FIX-001-report.md
```

Новый тестовый проект `EcadMcadBridge.Altium.Tests` добавлен только для чистых
Altium-функций (mapping/outline matcher), без зависимости Core от Altium SDK.

## 3. Решения по Board Outline

- `BoardOutline_GeometricPolygon()` — источник полного набора контуров и ролей
  `Outer`/`Hole` через `GetState_IsHole`.
- `IPCB_BoardOutline.GetState_Segments()` используется как источник линий/дуг, но не
  считается полным списком контуров.
- Каждый geometric contour отдельно сопоставляется с аналитическим циклом по общим
  IU-точкам.
- Если цикл не найден или сопоставление неоднозначно, контур **не удаляется**:
  экспортируется line-fallback из `IPCB_Contour` с предупреждением о возможной
  аппроксимации/потере дуг.
- Роль hole берётся только из `GetState_IsHole`; по индексу роль не угадывается.
- Лишние аналитические циклы, не сопоставленные с geometric contours, не экспортируются
  отдельно и дают счётчик `outlineUnmatchedLoops`.

## 4. Тест внутреннего выреза

Synthetic cutout smoke **не выполнен**. Причина: в доступных vendor-платах AD26
(`MiniPC`, `Bluetooth_Sentinel`, `PC4-SODIMM`, `WiFi_panel`, `SL1`) внутренний вырез
Board Outline не встретился; попытка создать его через UI на копии vendor-платы не дала
безопасно воспроизводимого результата без риска изменить исходную геометрию.

Подтверждено только:

- `GetState_IsHole()` используется для ролей;
- geometric contours не теряются;
- на `PC4-SODIMM` неполные analytic loops привели к `outlineFallback=1`,
  `outlineLoopErrors=6` и явным warnings вместо потери контура.

Критерий «внутренний вырез подтверждён в AD26» остаётся **неподтверждённым** (V-22).
Не утверждается полная поддержка дуговых cutouts.

## 5. PadShape: подтверждённые преобразования

| TShape | Решение | Категория |
|---|---|---|
| `eRectangular` | `Rectangular` | exact |
| `eCircleShape` | `Circular` | exact |
| `eRounded` (width == height) | `Circular` | exact |
| `eOctagonal` | `Octagonal` | simplified: Altium chamfered rectangle |
| `eRoundRectShape` | `RoundedRectangle` | simplified: corner radius API не подтверждён |
| `eRoundedRectangular` | `RoundedRectangle` | simplified: corner radius API не подтверждён |
| `eRounded` (width != height) | `RoundedRectangle` + radius min/2 | simplified: oval → rounded rect |
| `eRotatedRectShape` | не преобразуется | unsupported |
| `eArcShape` | не преобразуется | unsupported |
| `eTerminator` | не преобразуется | unsupported |
| `eCustomShape` | не преобразуется | unsupported |
| `eNoShape` | пропускается | unsupported/empty |

Неподдерживаемые формы не превращаются в круг/прямоугольник молча; они увеличивают
`padShapesUnsupported` и дают warning.

## 6. Диагностика

`AltiumAdapterDiagnostics` агрегирует:

- `outlineExact`, `outlineFallback`, `outlineOmitted`, `outlineLoopErrors`,
  `outlineUnmatchedLoops`;
- `padShapesExact`, `padShapesSimplified`, `padShapesUnsupported`, `padStacksEmpty`;
- счётчики по каждому исходному `TShape`.

Сводка пишется в лог как `ADAPTER_SUMMARY`; предупреждения по формам агрегируются по
типу (не по каждому паду). `BoardSnapshotValidator` warnings остаются отдельными
(в основном unconnected pads/optional fields); errors отсутствуют.

## 7. Подтверждённые API

Использованы и подтверждены C-73…C-84 из `SDK_API_LEDGER.md`, в частности:

- `BoardOutline_GeometricPolygon`, `GetState_IsHole`, `IPCB_Contour`;
- `IPCB_BoardOutline` как `IPCB_Polygon` (`GetState_PointCount`, `GetState_Segments`);
- `PolySegment.Kind/Vx/Vy/Cx/Cy/Radius/Angle1/Angle2`;
- `IPCB_Pad` shape/size/rotation/hole/net API.

Новые ограничения: V-24 (сегменты не гарантируют все циклы), V-25 (семантика
`eRotatedRectShape`/`eArcShape`/`eTerminator` не подтверждена).

## 8. Unit tests

Добавлены:

- 15 тестов `EcadMcadBridge.Altium.Tests`:
  - exact/simplified/unsupported `TShape` mapping;
  - `eRotatedRectShape`, `eArcShape`, `eTerminator`, `eCustomShape` not guessed;
  - outline matcher: multiple contours, no match, closed/open points.
- 8 тестов `AngleNormalizer` остаются.

Итог: Core.Tests 41/41, Altium.Tests 15/15, всего 56/56.

## 9. Smoke tests

Финальный код, AD26.12.2.9:

### MiniPC.PcbDoc

- 31 слой, 1046 компонентов, 6004 пада, 837 сетей;
- 9669 PadShape, `padShapesExact=8927`, `padShapesSimplified=742`,
  `padShapesUnsupported=0`;
- `outlineExact=1`, fallback/omitted/loopErrors = 0;
- validation errors 0; warnings 1029 (unconnected pads/optional fields);
- SHA256 до/после `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93`.

### Bluetooth_Sentinel.PcbDoc

- 17 слоёв, 73 компонента, 224 пада, 57 сетей;
- `outlineExact=1`, 12 сегментов (10 линий + 2 аналитические дуги),
  radius 5118110 IU;
- `padShapesExact=132`, `padShapesSimplified=92`, unsupported 0;
- validation errors 0; warnings 28;
- SHA256 до/после `1E18C3EFA9D6C3245AA09CAB7268B2A908078E50ED2D09DC9D6DFD3BC36B95F3`.

### PC4-SODIMM_PcbDoc

- `outlineFallback=1`, `outlineLoopErrors=6`;
- контур сохранён geometric fallback с warning, не потерян;
- SHA256 до/после `A42DCCE2230255D724D2D1062F55E52011DDEBED74EE7E4971D98DA4D12420F7`.

## 10. Build/test

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release
Core.Tests: 41/41
Altium.Tests: 15/15
```

## 11. SHA256

| Плата | SHA256 |
|---|---|
| MiniPC | `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93` |
| Bluetooth | `1E18C3EFA9D6C3245AA09CAB7268B2A908078E50ED2D09DC9D6DFD3BC36B95F3` |
| SODIMM | `A42DCCE2230255D724D2D1062F55E52011DDEBED74EE7E4971D98DA4D12420F7` |

Все хеши до/после экспорта совпали.

## 12. Commit / HEAD / PR

- Commit: `pending`.
- Новый HEAD: `pending`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/5 (не слит).

## 13. Оставшиеся вопросы

- Нужен отдельный synthetic cutout test в AD26; текущий smoke не подтверждает
  дуговые внутренние вырезы.
- Core 1.0.0 не содержит pad holes/plating, pin designator, local pad coordinates,
  stack order и 3D-model flag.
- `LayerKind` не различает signal/internal plane.
- Семантика `eRotatedRectShape`/`eArcShape`/`eTerminator` требует отдельного
  эксперимента; сейчас эти формы явно unsupported.
