# ARCHITECTURE — ECAD-MCAD Bridge

Статус: принята для bootstrap-этапа.
Изменения архитектуры допускаются только через `DECISIONS.md`.

## 1. Архитектурный принцип

Система строится как **два независимых CAD Add-in и одна общая библиотека Core**:

```text
┌──────────────────────────────┐        ┌──────────────────────────────┐
│ Altium Designer 26 (ECAD)    │        │ SOLIDWORKS 2026 (MCAD)       │
│                              │        │                              │
│ EcadMcadBridge.Altium        │        │ EcadMcadBridge.SolidWorks    │
│  └─ Altium SDK adapter       │        │  └─ SOLIDWORKS interop       │
└──────────────┬───────────────┘        └──────────────┬───────────────┘
               │                                       │
               │        нейтральная модель Core        │
               └──────────────►┌───────────────┐◄──────┘
                               │ EcadMcadBridge│
                               │     .Core     │
                               │ domain model  │
                               │ package I/O   │
                               │ validation    │
                               └───────┬───────┘
                                       │ файловый пакет
                                       ▼
                               ┌───────────────┐
                               │ Exchange      │
                               │ Package       │
                               │ (JSON + hash) │
                               └───────────────┘
```

Ключевые правила:

1. `Core` не ссылается на `Altium.SDK*.dll`, `SolidWorks.Interop.*` и другие CAD-сборки.
2. Add-in ссылается на `Core` и на SDK только своей CAD-системы.
3. Altium Add-in не содержит кода SOLIDWORKS, SOLIDWORKS Add-in не содержит кода Altium.
4. Обмен между сторонами идёт только через версионируемый файловый пакет Core **вне Git**.
5. Любая передача данных детерминирована и проверяема: версия схемы, единицы, порядок элементов,
   контрольные суммы.
6. Git используется только для разработки (задачи, отчёты, ревью, код), не для runtime-данных
   пользователя (PCB, STEP, рабочие сборки).

## 2. Компоненты

### 2.1 `EcadMcadBridge.Core`

Назначение: единая модель предметной области и формат обмена.

Состав (целевой):

- `Model/` — `BoardSnapshot`, `BoardProperties`, `BoardOutline`, `ComponentInstance`, `Transform`,
  `LayerRef`, `Pad`, `PadShape`, `PadStack`, `Track`, `Via`, `Region`, `TextItem`, `Net`,
  `Contour`, `ContourSegment`, `LineSegment`, `ArcSegment`, `PolygonGeometry`, `Point2D`,
  `LengthValue`.
- `Packaging/` — чтение/запись пакета, `manifest.json`, контрольные суммы, версии схемы.
- `Validation/` — проверки инвариантов, диагностические коды, отчёт о валидации.
- `Identity/` — стабильные идентификаторы и сопоставление сущностей.
- `Geometry/` — нейтральные примитивы, преобразования координат, допуски.
- `Diagnostics/` — структурированные сообщения без привязки к UI.

Ограничения:

- нет UI;
- нет ссылок на CAD SDK;
- нет сетевого доступа;
- нет чтения реестра Windows;
- все публичные контракты версионируются.

### 2.2 `EcadMcadBridge.Altium`

Назначение: ECAD Add-in для Altium Designer 26.

Слои:

```text
Altium Add-in
├── Bootstrap/      PluginFactory, ServerModule, регистрация команд
├── Adapter/        чтение IPCB_* и преобразование в модель Core
├── Application/    сценарии: экспорт snapshot, проверка, подготовка пакета
└── UI/             диалоги/панели (стек не зафиксирован, Q-005)
```

Правила:

- вход в Add-in — `CSharpPlugin.PluginFactory.InvokePluginFactory(DXP.IClient)`;
- `ServerModule` создаётся в конструкторе фабрики и регистрирует команды;
- чтение платы выполняется только через подтверждённые интерфейсы `PCB.IPCB_*`;
- UI-поток не блокируется длительными операциями;
- в лог не попадают данные заказчика и содержимое платы.

### 2.3 `EcadMcadBridge.SolidWorks`

Назначение: MCAD Add-in для SOLIDWORKS 2026.

На текущем этапе:

- проект не создаётся;
- API не фиксируется в категории подтверждённых;
- допускается только проектирование контрактов Core, не зависящих от SOLIDWORKS.

Будущие слои: `Bootstrap/`, `Adapter/`, `Application/`, `UI/` — по аналогии с Altium,
но с собственным interop-адаптером.

### 2.4 Тесты

- `EcadMcadBridge.Core.Tests` — обязательные unit-тесты Core, работают без CAD.
- Тесты Add-in не должны требовать запущенного CAD в обычном CI;
  smoke-тест в хосте выполняется вручную и фиксируется в отчёте.

### 2.5 Target Framework по проектам

| Проект | Target Framework | Обоснование |
|---|---|---|
| `EcadMcadBridge.Altium` | `net10.0-windows` | In-process загрузка в AD26; совместимость подтверждена оператором (C-65) |
| `EcadMcadBridge.Core` | `net10.0` | Нет UI и CAD-зависимостей; кроссплатформенная проверка модели |
| `EcadMcadBridge.Core.Tests` | `net10.0` | Тестируют Core без Windows-specific API CAD |
| `EcadMcadBridge.SolidWorks` | отдельным решением | Проект не создаётся до отдельного ТЗ (O-009) |

`net10.0-windows` не распространяется автоматически на Core и тесты. Изменение TFM любого проекта —
только через `DECISIONS.md` (D-013, D-014).

В исходном InteractiveBom, описанном в `BRIDGE-BOOTSTRAP-004`, TFM вычисляется в
`Directory.Build.props` из `X2.runtimeconfig.json` установленного Altium:
AD 26.1 → `net8.0`, AD 26.12 → `net10.0` (с суффиксом `-windows`).
Указанная Release-сборка архива находится в `bin/Release/net10.0-windows`. Для Bridge применяется
фиксированный `net10.0-windows` (C-65, D-014); автоматический выбор по версии хоста не используется.
Отдельная бинарная поставка `InteractiveBomPlug.dll` под `net8.0-windows` — другой источник
и не основа для TFM Bridge.

## 3. Направление зависимостей

```text
EcadMcadBridge.Altium ──────► EcadMcadBridge.Core
        │
        └──► Altium.SDK, Altium.SDK.Interfaces (только в adapter/bootstrap)

EcadMcadBridge.SolidWorks ──► EcadMcadBridge.Core
        │
        └──► SOLIDWORKS interop (только в adapter/bootstrap)

EcadMcadBridge.Core ────────► BCL только
```

Запрещено:

- `Core` → `Altium.*`, `SolidWorks.*`;
- `Altium` → `SolidWorks.*`;
- `SolidWorks` → `Altium.*`;
- циклические ссылки между проектами.

## 4. Поток данных ECAD → MCAD (целевой)

```text
1. Пользователь открывает .PcbDoc в Altium Designer 26.
2. Команда Add-in запускает сценарий ExportBoardSnapshot.
3. Adapter читает подтверждённые IPCB_* интерфейсы.
4. Adapter преобразует координаты и слои в нейтральную модель Core.
5. Core валидирует snapshot; назначение стабильных ID — отдельная задача (D-010).
6. Core формирует Exchange Package и контрольные суммы.
7. Пакет пишется в локальный runtime-каталог вне Git (см. раздел 9).
8. MCAD Add-in (будущая фаза) читает пакет и строит/обновляет модель.
```

На этапе `BRIDGE-000002` реализованы нейтральная модель Core, JSON и валидатор (раздел 5).
На этапе `BRIDGE-000003` реализован read-only Altium adapter для слоёв, компонентов и placement,
падов/PadStacks, nets и контура платы, включая аналитические дуги; команда Add-in —
`ExportBoardSnapshot`. Tracks, vias, regions и text items остаются вне scope. Шаги 6–8
(Exchange Package и MCAD-сторона) по-прежнему не реализованы.

## 5. Модель данных

### 5.1 Единицы и система координат

В пакете сохраняются **оба представления координаты**:

- `xIu` / `yIu` — исходные целочисленные Altium Internal Units (IU), источник истины для точности;
- `xMm` / `yMm` — вычисленные координаты в миллиметрах, JSON number с точностью не хуже `1e-6`.

Правила:

- IU не заменяются и не округляются; потеря исходных IU недопустима.
- Применяется коэффициент 1 IU = 0.00000254 mm; контрольные значения приведены в разделе 5.2.
- В Core преобразование IU → мм выполняет `AltiumUnits.IuToMm`; обратное — `AltiumUnits.MmToIu`.
  Altium adapter может получать исходные IU через `EDP.Utils` (C-57/C-58), но Core не зависит
  от Altium SDK и не принимает миллиметры как независимый источник истины.
- Требование сохранять пару IU/мм распространяется на все линейные величины из раздела 5.2,
  а не только на координаты.
- `manifest.json` и snapshot указывают `sourceUnits: "altium-iu"` и `units: "mm"`.
- Система координат обмена: правая декартова, X вправо, Y вверх, начало — начало координат платы Altium.
- Поворот — против часовой стрелки, градусы, диапазон `[-180, 180)`.
- Сторона компонента: `top` / `bottom`; исходный слой сохраняется отдельно как `layerId`.

### 5.2 `BoardSnapshot` — фактический контракт Core

Контракт реализован в `BRIDGE-000002` в `EcadMcadBridge.Core`:

- модель — `Model/`: `BoardSnapshot`, `BoardSnapshotSource`, `BoardProperties`, `BoardOutline`,
  `LayerRef`, `ComponentInstance`, `Transform`, `Pad`, `PadShape`, `PadStack`, `Net`, `Track`,
  `Via`, `Region`, `TextItem`, `Contour`, `ContourSegment`, `LineSegment`, `ArcSegment`,
  `PolygonGeometry`, `Point2D`, `LengthValue`;
- JSON — `Serialization/BoardSnapshotJson` (`Serialize`, `SerializeToUtf8` / `Deserialize`);
- валидация — `Validation/BoardSnapshotValidator`, `BoardValidationResult`, `BoardDiagnostic`.

Извлечение данных из Altium в этой задаче не реализовано. Контракт проверен синтетическими
данными Core.Tests и не считается доказанным соответствием всем структурам Altium.

Состав snapshot:

| JSON | Модель | Ключевые поля и ссылки |
|---|---|---|
| `source` | `BoardSnapshotSource` | `cad`, `cadVersion`, `documentFileName`, `exportedAtUtc`, `toolVersion`; абсолютный путь документа не входит в контракт |
| `sourceUnits`, `units` | строки | всегда `altium-iu` и `mm` |
| `board.properties` | `BoardProperties` | `id`, `name`, `material`, `thicknessIu/thicknessMm` |
| `board.outline` | `BoardOutline` | `id`, `contours` (outer/hole) |
| `layers` | `LayerRef` | `id`, `name`, `kind`, `side` |
| `components` | `ComponentInstance` | `id`, `designator`, `comment`, `footprint`, `sourceUniqueId`, `layerId`, `placement`, `heightIu/heightMm`, `fitted` |
| `padStacks` | `PadStack` → `PadShape` | `id`, `shapes`; shape: `id`, `kind`, `layerId`, `width`, `height`, `cornerRadius`, `vertices` |
| `pads` | `Pad` | `id`, `componentId`, `netId`, `layerId`, `padStackId`, `position`, `rotationDeg` |
| `nets` | `Net` | `id`, `name`, `netClass` |
| `tracks` | `Track` | `id`, `netId`, `layerId`, `start`, `end`, `widthIu/widthMm` |
| `vias` | `Via` | `id`, `netId`, `position`, `diameter`, `holeDiameter`, `startLayerId`, `endLayerId` |
| `regions` | `Region` → `PolygonGeometry` | `id`, `netId`, `layerId`, `kind`, `polygon.contours` |
| `texts` | `TextItem` | `id`, `componentId`, `layerId`, `position`, `text`, `height`, `strokeWidth`, `rotationDeg` |

Геометрия:

- `Point2D` хранит `xIu/yIu` и вычисленные `xMm/yMm`;
- `Transform` содержит `position`, `rotationDeg` и `side`;
- `Contour` содержит `id`, `role` (`outer` / `hole`) и упорядоченные `segments`;
- `LineSegment` содержит `start` и `end`; `ArcSegment` содержит `start`, `end`, `center`,
  `radiusIu/radiusMm` и `sweepDirection`. Дуга хранится аналитически и не заменяется отрезками;
- `Region.polygon.contours` допускает несколько контуров, включая отверстия;
- `PolygonGeometry` — контейнер контуров; `LineSegment`, `ArcSegment`, `Point2D`, `Transform`
  являются value objects и не имеют собственных ID.

Пример формы JSON (сокращённый; порядок полей фиксируется сериализатором):

```json
{
  "schema": "ecad-mcad-bridge/board-snapshot",
  "schemaVersion": "1.0.0",
  "source": {
    "cad": "altium",
    "cadVersion": "26",
    "documentFileName": "board.PcbDoc",
    "exportedAtUtc": "2026-11-24T00:00:00Z",
    "toolVersion": "0.2.0"
  },
  "sourceUnits": "altium-iu",
  "units": "mm",
  "board": {
    "properties": {
      "id": "board-1",
      "name": null,
      "material": null,
      "thicknessIu": null,
      "thicknessMm": null
    },
    "outline": {
      "id": "outline-1",
      "contours": [
        {
          "id": "outline-outer",
          "role": "outer",
          "segments": [
            {
              "kind": "line",
              "start": { "xIu": 0, "yIu": 0, "xMm": 0, "yMm": 0 },
              "end": { "xIu": 10000000, "yIu": 0, "xMm": 25.4, "yMm": 0 }
            },
            {
              "kind": "arc",
              "start": { "xIu": 10000000, "yIu": 0, "xMm": 25.4, "yMm": 0 },
              "end": { "xIu": 10000000, "yIu": 10000000, "xMm": 25.4, "yMm": 25.4 },
              "center": { "xIu": 10000000, "yIu": 5000000, "xMm": 25.4, "yMm": 12.7 },
              "radiusIu": 5000000,
              "radiusMm": 12.7,
              "sweepDirection": "counterClockwise"
            }
          ]
        }
      ]
    }
  },
  "layers": [],
  "components": [],
  "padStacks": [],
  "pads": [],
  "nets": [],
  "tracks": [],
  "vias": [],
  "regions": [],
  "texts": []
}
```

Правила сериализации:

- `BoardSnapshotJson` пишет UTF-8 без BOM, LF, инвариантные числа, фиксированный порядок полей;
- массивы верхнего уровня сортируются по `id`; контуры сортируются по `id`, порядок сегментов
  внутри контура сохраняется;
- отсутствующие значения записываются явным `null`, пустые коллекции — `[]`;
- `NaN` и `Infinity` в JSON не допускаются; сериализатор отклоняет их, читатель принимает только
  конечные числа;
- `schemaVersion` входит в контракт; несовместимое изменение — major;
- диагностический `SmokeSnapshotJson` не смешивается с `BoardSnapshotJson`.

Правила валидации:

- `BoardSnapshotValidator.Validate` не меняет модель и возвращает `BoardValidationResult`;
- ошибки (`Error`) означают, что snapshot непригоден для обмена; предупреждения (`Warning`)
  фиксируют неполные данные;
- коды стабильные: `EMB-VAL-001` … `EMB-VAL-011`, `EMB-VAL-101`, `EMB-VAL-102`;
- проверяются уникальность ID, обязательные атрибуты, ссылки, пары IU/мм, конечность чисел,
  отрицательные размеры, дуги, замкнутость и непрерывность контуров, роли контуров и наличие
  внешнего контура;
- `TextItem.RotationDeg` — optional: `null` даёт `MissingOptionalData` warning, конечное
  значение проверяется в `[-180, 180)`, `NaN`/`Infinity` остаются ошибками;
  `Pad` и `ComponentInstance` по-прежнему требуют конечный угол;
- пространственная вложенность отверстий и контуров не проверяется геометрическим
  point-in-polygon тестом; это известное ограничение текущей версии.

### 5.3 Идентификаторы

Core рассматривает ID как непрозрачную непустую строку. Генерация ID, SHA-256 и суффиксы
коллизий в `BRIDGE-000002` не реализованы. `SourceUniqueId` сохраняется только как provenance
и не считается доказанно стабильным; D-010 остаётся `Proposed`. ID не выводятся из Designator
или координат внутри модели. Стратегия стабильных идентификаторов — отдельная задача.

### 5.4 Altium adapter — BRIDGE-000003 / BRIDGE-000007 / BRIDGE-000009

Реализация: `EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter` и
`AltiumOutlineReader`. Adapter только читает `PCB.IPCB_*`, не содержит JSON и не меняет
плату. Команда `EcadMcadBridge:ExportBoardSnapshot` вызывает adapter, затем Core
`ExchangePackageWriter`; snapshot пишется в Exchange Package через
`BoardSnapshotJson`, JSON-контракт не дублируется.

| Altium | Core |
|---|---|
| `IPCB_Board.LayerIterator()` / `IPCB_LayerObject` | `LayerRef` (V6-ID как opaque `altium:layer:<id>`, имя, kind, side) |
| `IPCB_Board.GetState_MasterStack().Get_ZTop/ZBottom` | `BoardProperties.Thickness` |
| `IPCB_Component` | `ComponentInstance` + `Transform` + `SourceUniqueId` |
| `IPCB_Pad` + copper layer shapes | `Pad` + `PadStack` + `PadShape` (per-layer width/height) |
| `IPCB_Net` | `Net` |
| `IPCB_Track` (`eTrackObject`) | `Track` (start/end/width/layer/net) |
| `IPCB_Via` (`eViaObject`) | `Via` (position/diameter/hole/start+end layer/net) |
| `IPCB_Region` (`eRegionObject`) | `Region` (layer/net/kind/contours); Board Cutout остаётся в outline |
| `IPCB_Text` (`eTextObject`) | `TextItem` (position/layer/rotation/text/height/stroke); designator/comment не дублируются |
| `IPCB_BoardOutline.BoardOutline_GeometricPolygon()` | контуры и роли outer/hole |
| `IPCB_BoardOutline` как `IPCB_Polygon.GetState_Segments()` | `LineSegment`/`ArcSegment` с сохранением `Angle1/Angle2` |

Ограничения текущего adapter:

- Core `LayerKind` не различает signal и internal plane; оба представлены как `Copper`.
  Механические слои определяются по V6-ID, а не по имени.
- В Core 1.0.0 нет полей pad hole, pin designator, локальных координат пада, stack order
  и признака 3D-модели; adapter не выдумывает эти значения и пишет предупреждения.
- `GeometricPolygon` — источник полного набора контуров и ролей. Аналитические сегменты
  сопоставляются с каждым контуром отдельно. Exact допускается только при замкнутом и
  непрерывном цикле, совпадении уникальных вершин с geometric polyline и покрытии всех
  geometric points аналитическими линиями/дугами; одной общей точки недостаточно.
  Для дуг проверяется ограниченный угловой диапазон (CW/CCW и переход 0°/360°),
  а также середины сегментов geometric polyline, а не только вершины. При
  частичном/неоднозначном совпадении контур не теряется, а экспортируется
  line-fallback с предупреждением о возможной потере дуг.
- `TShape`: точные — `eRectangular`, `eCircleShape`, `eRounded` с равными размерами;
  упрощённые с warning — `eOctagonal` (chamfered rectangle), rounded rectangles без
  подтверждённого corner radius, oval через rounded rectangle radius=min/2;
  неподдерживаемые и не подменяемые — `eRotatedRectShape`, `eArcShape`, `eTerminator`,
  `eCustomShape`, `eNoShape`.
- Region geometry в BRIDGE-000009 экспортируется из
  `IPCB_Region.GetGeometricPolygon()` как line segments: analytic region arc API
  не подтверждён безопасным вызовом, поэтому adapter выдаёт явный warning
  `RegionGeometryFallbacks`; вопрос Q-010 по аналитическим дугам регионов остаётся
  открытым. Board Cutout по-прежнему идёт через существующий clip-механизм и не
  дублируется в `Regions`.
- Text designator/comment (`IsDesignator`/`IsComment`) не дублируются в `TextItem`;
  barcode-тексты не представляются и пропускаются с warning; остальные тексты
  используют `IPCB_RectangularPrimitive` position/rotation. Неизвестный rotation не
  подменяется нулём: `TextItem.RotationDeg` остаётся `null`, validator выдаёт
  `MissingOptionalData` warning, а adapter считает `TextUnknownRotations`.
- Altium API предоставляет для текста `Bold`/`Italic`, но Core `TextItem` 1.0.0 не
  содержит полей для этих параметров; текущий adapter их не экспортирует. Font outline
  (TrueType) и barcode также не представляются контрактом Core 1.0.0 и не переносятся.
- Track/via/region/text IDs используют `GetState_UniqueId`, если он непустой, иначе
  deterministic fallback по порядку итерации; дубликаты внутри коллекции
  разводятся суффиксом. На проверенных платах `GetState_UniqueId` для этих
  примитивов вернул пустую строку, поэтому фактически использован fallback.
  Стабильность ID между сессиями не утверждается (D-010 остаётся Proposed).
- `TRegionKind.eRegionKind_NamedRegion/Cutout/Cavity` не имеют представления в Core
  1.0.0 и экспортируются как `RegionKind.Other` с диагностическим счётчиком и warning;
  unknown enum также даёт `Other`.
- Внутренние вырезы контура извлекаются из `IPCB_Region` с
  `TRegionKind.eRegionKind_BoardCutout` и подтверждаются
  `IPCB_BoardOutline.ClipWithPolyRegShapes(false,false)`; параметры `false,false`
  исключают pad/via holes. `GeometricPolygon.GetState_IsHole` на проверенных платах
  вырезов не дал и не является источником cutout. Тип региона без clip result
  недостаточен: `MiniPC.PcbDoc` содержит `eRegionKind_BoardCutout`
  («Layer Stack Region 1»), но клиппер возвращает 0 вырезов, поэтому регион остаётся
  только диагностикой. Реальный `board with cutout.PcbDoc` дал 2 выреза, включая дуги
  (V-22 закрыт в `BRIDGE-000003-FIX-004`).
- Дедупликация cutout сравнивает **фактические точки geometric Hole** с clip loop;
  самосравнение clip loop устранено. Сначала применяется exact tolerance 2 IU, затем
  отдельная approximation-проверка для полигонального Hole против аналитической дуги:
  двустороннее покрытие с допуском 3500 IU, площадь 5% и centroid характерных точек.
  Каждая вершина Hole проецируется на clip loop; сравниваются centroid Hole и
  centroid проекций. Это scale-independent: разная плотность полилинии не меняет
  решение, а перенос независимого контура даёт реальное смещение. Raw centroid
  остаётся диагностикой. Допуск matched-centroid 100 IU. Partial overlap не
  считается дубликатом; сомнительное совпадение не подавляет export.
- Bounds аналитического clip loop вычисляются по Start/End и кардинальным экстремумам,
  которые реально входят в sweep дуги (CW/CCW, переход 0°/360°, дуги >180°).
  Sampling не используется для точных bounds. Поэтому broad phase не может отбросить
  корректный Region из-за пропущенного экстремума.
- Сопоставление clip loop ↔ `IPCB_Region` — geometry-first: проверяются все
  BoardCutout source contours, bounds используются только для диагностики/ранжирования.
  Геометрия: вершины Region-контура 3500 IU, площадь 5%, centroid 1% диагонали
  (минимум 100 IU). Результат — `Matched` / `Unmatched` / `Ambiguous`; связь хранится
  по паре `(RegionIndex, ContourIndex)`, один source contour не назначается повторно.
  `Ambiguous` и `Unmatched` не экспортируются и диагностируются с индексами и bounds.
- Для analytic loop дополнительно проверяются середины дуг/линий самого loop, поэтому
  дуга не может «выпучиваться» за polyline и получать ложный exact.
- `PolySegment.Angle1/Angle2` для `ePolySegmentArc` задают обход CCW; `Angle2 < Angle1`
  означает переход через 0°. Это исправлено в `FIX-005`; ранее такой arc ошибочно
  трактовался как CW-дуга 270°.
- Полное соответствие Core всем структурам Altium не утверждается; adapter проверен на
  `MiniPC.PcbDoc`, `Bluetooth_Sentinel.PcbDoc` и `board with cutout.PcbDoc`
  в AD26.12.2.9 (см. отчёты `BRIDGE-000003`, `BRIDGE-000003-FIX-004`,
  `BRIDGE-000003-FIX-005`, `BRIDGE-000003-FIX-006`).

## 6. Формат Exchange Package

Пакет — каталог в локальном runtime-каталоге или ZIP для внешней передачи, но **не в Git**;
структура:

```text
<package-id>/
├── manifest.json
├── board.snapshot.json
├── models/
│   └── models.json        # опционально
├── assets/
│   └── 3d/
│       └── <sha256>.step  # опционально; также .x_t
├── checksums.sha256
└── attachments/          # опционально, только по согласованию
```

`manifest.json`:

```json
{
  "schema": "ecad-mcad-bridge/package",
  "schemaVersion": "1.0.0",
  "packageId": "uuid",
  "createdAtUtc": "2026-11-23T00:00:00Z",
  "producer": { "component": "EcadMcadBridge.Altium", "version": "0.0.0" },
  "source": { "cad": "altium", "documentSha256": "…" },
  "sourceUnits": "altium-iu",
  "units": "mm",
  "files": [
    { "path": "board.snapshot.json", "sha256": "…", "size": 1234 }
  ],
  "capabilities": ["board-outline", "component-placement", "layers", "nets", "pads", "regions", "texts", "tracks", "vias"]
}
```

Правила:

- `schemaVersion` меняется по SemVer; несовместимое изменение — major.
- Неизвестные поля при чтении игнорируются, но сохраняются в диагностике.
- `checksums.sha256` покрывает все файлы, кроме самого файла контрольных сумм.
- Порядок массивов детерминирован (стабильная сортировка по `id`).
- В JSON запрещены `NaN`, `Infinity`, локалезависимые числа.
- Кодировка — UTF-8 без BOM, перевод строки — LF.
- `units` и `sourceUnits` обязательны; несовпадение версий/единиц — ошибка чтения, а не autocast.

### 6.1 Core Packaging (BRIDGE-000004)

- `PackageManifest` содержит schema `ecad-mcad-bridge/package` 1.0.0, `packageId`,
  `createdAtUtc`, producer/source, `sourceUnits`/`units`, `files` и capabilities.
- `PackageManifestJson` пишет manifest в фиксированном порядке полей, UTF-8 без BOM,
  LF, invariant culture; `files` и capabilities сортируются ordinal, capabilities
  уникальны.
- `checksums.sha256` — строки `<lowercase-sha256>  <relative-path>`, LF, UTF-8 без BOM,
  сортировка по ordinal path; покрывает `manifest.json` и все `files`, но не сам файл.
- `ExchangePackageWriter` валидирует `BoardSnapshot`, не перезаписывает существующий
  package-id, пишет во временный каталог рядом с целевым, проверяет байты и только
  затем выполняет atomic `Directory.Move`. Ошибка удаляет временный каталог.
- `ExchangePackageReader` проверяет schema/major, metadata, units, безопасные
  относительные пути (`/`, без `..`, absolute, `:` и `\`), набор checksum-файлов,
  размеры, SHA-256, JSON snapshot и `BoardSnapshotValidator`.
- Inventory файлов рекурсивный: nested regular files сравниваются с manifest/checksums;
  заявленный nested file проверяется по размеру и SHA-256, незаявленный даёт
  `EMB-PKG-019 ExtraFile`.
- Directory/file reparse points и symlinks не обходятся и диагностируются как
  `EMB-PKG-021 UnsupportedLink`; traversal не выходит за package root.
- `PackagePath` не допускает выход за корень пакета; `checksums.sha256` не может
  покрывать сам себя.
- Диагностика — `PackageDiagnostic`/`PackageDiagnosticCodes` (`EMB-PKG-*`).
- В MVP нет attachments, ZIP и чтения CAD-документа; `documentSha256` передаётся
  вызывающей стороной как metadata.

### 6.2 Altium Package Export (BRIDGE-000007)

- Команда `EcadMcadBridge:ExportBoardSnapshot` проверяет активный PCB через
  `IServerDocument.GetKind/GetFileName/GetModified`.
- `GetFileName()` сохранённого PCB даёт absolute path; новый/несохранённый PCB может
  вернуть относительное имя (`PCB1.PcbDoc`) и отклоняется.
- При `GetModified() == true` экспорт отклоняется с просьбой сохранить плату;
  автоматическое сохранение запрещено.
- После извлечения snapshot путь и `GetModified` проверяются повторно.
- `documentSha256` — SHA-256 исходного `.PcbDoc`, вычисленный после проверки
  стабильности размера/mtime; это не хеш snapshot.
- Package root: `ECAD_MCAD_BRIDGE_PACKAGE_ROOT` (absolute), иначе
  `%LOCALAPPDATA%\EcadMcadBridge\Packages`.
- `PackageId` — новый GUID, `CreatedAtUtc` — UTC, producer — `EcadMcadBridge.Altium`
  `0.3.0`, capabilities — только реально извлекаемые данные. В `BRIDGE-000009`
  объявляются `board-outline`, `component-placement`, `layers`, `nets`, `pad-stacks`,
  `pads`, `regions`, `texts`, `tracks`, `vias`.
- Writer создаёт пакет атомарно и не перезаписывает существующий каталог;
  пользователю показываются путь, package id, counts и warnings, но в лог не
  попадают absolute user paths.

### 6.3 Core 3D component model catalog (BRIDGE-000013)

- `models/models.json` — отдельная версионируемая схема
  `ecad-mcad-bridge/component-models` 1.0.0. Версия схемы manifest остаётся 1.0.0:
  каталог необязательный.
- Capability `component-3d-models` и `models/models.json` обязательны вместе.
  Каталог без capability, capability без каталога или расхождение manifest —
  ошибка целостности `EMB-PKG-026`.
- Каталог содержит запись для каждого `ComponentInstance.Id` из
  `board.snapshot.json`; `designator` сверяется. Компонент без тел имеет пустой
  `bodies`. `bodyOrdinal` уникален внутри компонента; тела не объединяются.
- `representation`: `generic`, `native`, `unknown`; `sourceOrigin`: `embedded`,
  `native`, `linked`, `unknown`. `GetEmbed=false` сам по себе не доказывает
  `linked`. Неизвестное значение не подменяется догадкой.
- `assetAcquisition`: `sdk-export`, `source-copy-verified`, `not-acquired`,
  `unknown`. `source-copy-verified` допустим только при доказанной побайтовой
  идентичности; совпадение размера доказательством не является.
- `assetFormat`: `step-ap214`, `parasolid-x_t`; `assetPath` обязателен только
  вместе с форматом и файлом. Путь — `assets/3d/<lowercase-sha256>.<ext>`,
  относительный, только внутри `assets/3d/`; абсолютные пути и `..` запрещены.
- `sdkOrigin` и `sdkState` хранят сырые значения API. Каждая линейная величина —
  пара IU/мм или оба `null`; углы не нормализуются (360° сохраняется); `dz`
  хранится парой. `modelRotationDegRaw` — отдельное nullable-поле. Полная матрица
  `T_B_S`, Z/Bottom и порядок Euler не выводятся.
- Публичный `ExchangePackageWriter.Write(..., models)` принимает типизированный
  request: компоненты/тела и набор исходных файлов, связанных по `assetKey`.
  Существующий вызов без моделей сохранён. Абсолютные исходные пути не попадают
  в каталог, manifest или диагностику.
- Writer вычисляет SHA-256 и размер фактически записанных байтов, формирует
  content-addressed пути, дедуплицирует одинаковые assets, копирует и хеширует
  потоково, проверяет неизменность source size/mtime и только затем публикует
  каталог через общий manifest/checksums.
- Reader не загружает STEP/X_T в память и не интерпретирует геометрию. Он
  проверяет JSON/UTF-8, схему и major version, обязательные поля, enum,
  уникальность `componentId`/`bodyOrdinal`, связь с snapshot, парность IU/мм,
  `NaN`/`Infinity`, имя/расширение/путь asset, наличие в manifest/checksums,
  совпадение SHA-256 и размера, обратные ссылки и отсутствие потерянных или
  неиспользуемых assets. `PackageChecksums.ComputeFileSha256` и
  `PackageFileHasher.ComputeStableSha256` работают потоково.
- Ошибки каталога и assets используют стабильные коды `EMB-PKG-022`…`EMB-PKG-026`.
  Сообщения не содержат абсолютных путей пользователя.

### 6.5 SOLIDWORKS STEP import backend (BRIDGE-000015)

- Проект `EcadMcadBridge.SolidWorks` — `net10.0-windows`, x64, зависит от Core,
  не зависит от Altium. SOLIDWORKS interop подключается локально из
  `SolidWorksApiDir` с `Private=false`; proprietary DLL не коммитятся.
- `SolidWorksImportPlanBuilder` читает `PackageReadResult`, проверяет
  snapshot/models, собирает уникальные `assetPath` и связи
  `componentId/bodyOrdinal → asset`. NotAcquired bodies пропускаются с warning;
  не-STEP формат — ошибка; отсутствующий/пустой asset — ошибка.
- Выходное имя — content-addressed SHA basename + `.SLDPRT`; dedup по
  `AssetPath`; один asset импортируется один раз, несколько bodies ссылаются на
  одну деталь.
- `SolidWorksImportService`: `GetImportFileData`/`MapConfigurationData=false`,
  `LoadFile4`, проверка `swDocPART`/`swDocASSEMBLY`,
  `PartDoc.GetBodies2(swSolidBody)`, `ModelDocExtension.SaveAs3(
  swSaveAsCurrentVersion, swSaveAsOptions_Silent)`. Target `.SLDPRT` проверяется
  до импорта: существующий или уже открытый файл отклоняется без перезаписи.
- `SaveAs3` считается успешным только при `returned=true`, `Errors=0`, existing
  file и size > 0. Результат сохраняется сначала во временный `.SLDPRT` внутри
  выходного каталога; после закрытия imported-документов файл атомарно
  перемещается в content-addressed native path. Для проверки повторного открытия
  используется байтовая копия с уникальным именем: SOLIDWORKS отклоняет второй
  документ с совпадающим basename, а пользовательские документы не должны
  закрываться. Body count сравнивается с импортированным значением.
- Если STEP импортируется как `swDocASSEMBLY`, backend считает solid bodies
  всех component instances и сохраняет assembly как native part через
  `SaveAs3(.SLDPRT)`. Перед сохранением настройка
  `swSaveAssemblyAsPartOptions` выставляется в `swSaveAsmAsPart_AllComponents`
  и безусловно восстанавливается в `finally`; неудача восстановления — Error.
- `SolidWorksWarningDecoder` разбирает `swFileSaveWarning_e` и
  `swFileLoadWarning_e` по именам; бит `128` — известный warning
  (`AnimatorCameraViews` / `AlreadyOpen`), а не «unknown» и не ошибка.
- `SolidWorksAssemblyService` — только API smoke: `NewDocument` по явному
  assembly template, загрузка временных байтовых копий native parts в память,
  `AddComponent5(0,0,0)`, `GetComponents`, `SaveAs3`, повторное открытие и
  сравнение количества компонентов. Это не placement ECAD.
- `SolidWorksDocumentTracker` снимает baseline через `GetDocuments()` до операции
  и закрывает только положительно зарегистрированные Bridge-owned документы
  (`LoadFile4`, component `GetModelDoc2()`, `OpenDoc6`, `NewDocument`). Документы
  baseline, `swFileLoadWarning_AlreadyOpen` и unknown post-baseline защищены от
  закрытия; unknown оставляет warning и не даёт cleanup статус `Verified`.
- Snapshot имеет явный статус `Available`/`Unavailable`/`Incomplete`; COM/RPC
  exception не превращается в пустой список, неполное чтение identity не считается
  успешным перечислением. `CleanupStatus` — `Verified`/`Incomplete`/`Failed`/
  `Unavailable`; только `Verified` считается успехом. Успех подтверждается final
  snapshot, а не возвратом `CloseDoc`/`QuitDoc`.
- Smoke harness подключается к уже запущенному SOLIDWORKS через ROT, работает в
  STA, вызывает API последовательно, не запускает/не завершает пользовательский
  процесс. Входные STEP assets не изменяются; smoke фиксирует baseline, remaining
  и `baselinePreserved`.
- `T_B_S`, Z/Bottom, Euler composition, ECAD placement, Add-in registration,
  Registry и установщик остаются вне scope.

## 7. Обработка ошибок и логирование

- Все ошибки Core имеют стабильный код `EMB-<AREA>-<NNN>` и сообщение без привязки к UI.
- Add-in перехватывает исключения на границе команды, показывает пользователю краткое сообщение,
  пишет подробности в локальный лог.
- В лог запрещено писать: пути с именами заказчиков, содержимое netlist/BOM, токены, лицензии.
- Для диагностики допускается: версия Add-in, версия CAD, имя документа без пути, счётчики объектов,
  коды ошибок, длительность операции.
- Молчаливое проглатывание исключений запрещено; допустим только явный fallback с записью в лог.

## 8. Git-протокол разработки с локальным агентом

### 8.1 Назначение

Протокол задаёт способ постановки задач локальному агенту, получения отчётов, ревью и кода
без прямого сетевого API: обмен идёт через Git-репозиторий и файлы в `.agent/`.
Это **только разработка**. Runtime-передача пользовательских данных описана в разделе 9
и не использует Git.

### 8.2 Каналы

| Канал | Путь | Направление | Назначение |
|---|---|---|---|
| Tasks | `.agent/tasks/BRIDGE-NNNNNN.md` | Оператор → Агент | Постановка задачи |
| Reports | `.agent/reports/BRIDGE-NNNNNN-report.md` | Агент → Оператор | Результат, решения, вопросы |
| Reviews | `.agent/reviews/BRIDGE-NNNNNN-review.md` | Ревьюер → Оператор/Агент | Вердикт и замечания |
| Context | `.agent/PROJECT_CONTEXT.md`, `ARCHITECTURE.md`, `DECISIONS.md`, `SDK_API_LEDGER.md` | Общий | Долговременное состояние |

Дополнительные каналы и каталоги не создаются без решения в `DECISIONS.md`.

### 8.3 Front matter

Каждый файл task/report/review начинается с YAML front matter:

```yaml
---
id: BRIDGE-000001
type: task            # task | report | review
status: assigned      # assigned | in_progress | blocked | ready_for_review | accepted | rejected | cancelled
from: operator
to: local-agent
created: 2026-11-23T10:00:00+03:00
updated: 2026-11-23T10:00:00+03:00
base: source
branch: task/BRIDGE-000001
related: []
---
```

Обязательные поля: `id`, `type`, `status`, `from`, `to`, `created`, `updated`, `base`.
Для report дополнительно: `commit`, `pr`. Для review: `verdict` (`accepted`/`rejected`).

### 8.4 Именование и идентификаторы

- ID задачи: `BRIDGE-` + 6 цифр, сквозная нумерация, без переиспользования.
- Task: `BRIDGE-000001.md`.
- Report: `BRIDGE-000001-report.md`.
- Review: `BRIDGE-000001-review.md`.
- Ветки:
  - постановка: `task/BRIDGE-000001`;
  - исполнение: `agent/BRIDGE-000001-<краткий-слаг>`;
  - ревью: `review/BRIDGE-000001`.
- База для исполнения — `source`, если в task не указано иное.

### 8.5 Жизненный цикл

```text
assigned
   │  агент берёт задачу
   ▼
in_progress ──► blocked (нужно решение человека)
   │
   ▼
ready_for_review
   │  ревьюер проверяет
   ├──► rejected ──► in_progress
   └──► accepted ──► merge PR в source
```

Правила переходов:

1. Агент не изменяет task-файл после `status: assigned`; уточнения — отдельным сообщением в report
   или новым task-файлом.
2. Агент создаёт report в той же ветке, что и работа, и переводит его в `ready_for_review`.
3. Ревьюер пишет review-файл; при `rejected` агент устраняет замечания новыми коммитами.
4. Merge в `source` выполняет только человек (Оператор).
5. `accepted` без review-файла не допускается.

### 8.6 Транспорт и атомарность

- Единица передачи — Git-коммит; частично применённых изменений быть не должно.
- Агент делает `git fetch` перед началом и перед push.
- Если task-файл изменился после взятия задачи — работа останавливается, в report фиксируется
  `blocked` с причиной.
- Force-push в `source` и в чужие ветки запрещён.
- Один файл — один писатель. Report и review не редактируются задним числом;
  исправления оформляются новым коммитом с записью в разделе «История изменений».
- Большие бинарные вложения в Git не добавляются без решения; для них используется внешнее хранилище,
  ссылка на которое фиксируется в task/report.

### 8.7 Содержимое report (обязательное)

1. Итог: что сделано и что не сделано.
2. Список созданных/изменённых файлов.
3. Принятые решения и их обоснование.
4. Неподтверждённые вопросы и риски.
5. Результаты проверок (build/test/manual smoke).
6. Commit SHA и ссылка на Pull Request.
7. История изменений (для повторных итераций).

### 8.8 Безопасность

- Текст task-файла — это постановка, а не безусловная команда на выполнение произвольного кода.
- Агент не выполняет из task-файла команды установки ПО, изменения реестра, сетевые вызовы
  и удаление данных без явного разрешения Оператора.
- Секреты, токены, лицензии и данные заказчиков в `.agent/` не попадают.
- Все внешние файлы и бинарные артефакты рассматриваются как недоверенные.

### 8.9 Граница Git-протокола

Git не используется для передачи:

- пользовательских `.PcbDoc`, `.SchDoc`, библиотек и netlist;
- STEP/STL/IGES и других MCAD-моделей;
- рабочих сборок Add-in, DLL, инсталляторов и логов CAD;
- любых бинарных артефактов, содержащих данные заказчика.

В Git попадают только исходники, документация, синтетические фикстуры и `.agent/`-артефакты.

## 9. Файловый runtime-обмен между CAD-системами

### 9.1 Назначение

Runtime-обмен передаёт Exchange Package от ECAD Add-in к MCAD Add-in через согласованный
локальный каталог на CAD-стенде. Это отдельный канал, не связанный с Git.

### 9.2 Канал

| Параметр | Значение |
|---|---|
| Транспорт | Локальная файловая система (каталог, определяемый Оператором) |
| Кто пишет | Производящий Add-in (сейчас — Altium) |
| Кто читает | Потребитель пакета (будущий SOLIDWORKS Add-in) |
| Содержимое | `manifest.json`, snapshot, `checksums.sha256`, согласованные вложения |
| Запрет | Коммит пользовательских PCB, STEP и рабочих сборок в Git |

Каталог задаётся конфигурацией хоста и не хранится в репозитории как абсолютный путь.
В репозитории фиксируется только формат пакета из раздела 6.

### 9.3 Инварианты

1. Пакет считается готовым только после записи `checksums.sha256`.
2. Читатель проверяет `schemaVersion`, `units`, `sourceUnits` и контрольные суммы.
3. Частично записанный пакет не считается готовым; используется временное имя с последующим rename.
4. Ошибка чтения пакета не меняет целевую модель CAD.
5. В логах фиксируются только идентификатор пакета и коды ошибок, а не пути заказчика.

## 10. Установка и загрузка Altium Add-in

### 10.1 Архитектура InteractiveBom по `BRIDGE-BOOTSTRAP-004`

Исходный проект, описанный в задании (`InteractiveBom.zip`, `InteractiveBom.sln`), состоит из четырёх проектов:

| Проект | Роль |
|---|---|
| `InteractiveBom` | Загрузочный shim: COM-фабрика, `PluginServerModule`, `Commands.cs`, `Implementation.cs`, установка (`.ins`/`.rcs`); живёт в хосте постоянно |
| `InteractiveBom.Impl` | Реализация команд и извлечения данных платы; в Debug загружается hot-reload хостом, в Release вызывается статически через ProjectReference |
| `InteractiveBom.Panels` | UI-панели (server views) |
| `InteractiveBom.Tests` | Тесты |

shim — тонкий слой, который Altium загружает как расширение. `PluginFactory` создаёт
`PluginServerModule`; `Implementation` — единственный маршрут из shim в `Impl` и единственное место,
где различаются Debug и Release:

- **Debug / Hot Reload.** `Implementation.Start()` поднимает `Altium.Designer.HotReload.HotReloadHost`
  из build output `Impl`; команды `Reload`/`Status` доступны только под `#if DEBUG`. Код shim
  (`OnCreated`) не выгружается при перезагрузке Impl.
- **Release.** Hot-reload-код не компилируется; вызов идёт статически через ProjectReference.
  Указанная сборка — `bin/Release/net10.0-windows`.

`Installation/InteractiveBom.ins` объявляет сервер, исполняемый файл и команды;
`Installation/InteractiveBom.rcs` связывает PLID с caption/иконкой и точкой вставки в меню.
Расширение размещается в каталоге Altium Extensions и регистрируется в `ExtensionsRegistry.xml`.

### 10.2 COM-контракт (по заданию `BRIDGE-BOOTSTRAP-004` и shim-шаблону Altium SDK)

- `[assembly: ComVisible(true)]` задаётся на уровне сборки, а не только на классе фабрики.
- `CSharpPlugin.PluginFactory` реализует `IPluginFactory` и помечен
  `[ClassInterface(ClassInterfaceType.AutoDispatch)]`.
- `PluginServerModule` также помечен `[ClassInterface(ClassInterfaceType.AutoDispatch)]`.
- `InvokePluginFactory(DXP.IClient)` возвращает наследника `DXP.ServerModule`.

`ClassInterfaceType.None` не является вариантом исходного проекта InteractiveBom; прежняя запись
в документации была ошибкой. Для Bridge принято следовать тому же образцу; это зафиксировано
отдельным проектным решением D-017.

### 10.3 Что берём в Bridge, а что пока нет

Используем: shim с фабрикой/модулем, разделение installation (`.ins`/`.rcs`), COM-контракт
из 10.2, минимальный слой адаптера вместо `Impl` и явную одну команду.

Не копируем автоматически: Debug Hot Reload и команды `Reload`/`Status`, `InteractiveBom.Panels`
и зависимости от UI-панелей, динамическую диспетчеризацию `Cmd=…` через параметры команды,
а также автоматический выбор TFM по версии хоста. Это усложняет каркас без необходимости
для `BRIDGE-000001` и может быть добавлено отдельным решением.

Детали сигнатур и доказательства — в `SDK_API_LEDGER.md`. Автоматизация установки не входит
в текущий scope (Q-006).

## 11. Нефункциональные требования

- Детерминизм: повторный экспорт неизменной платы даёт идентичный snapshot (кроме timestamp).
- Производительность: экспорт типовой платы — без зависания UI; ориентир уточняется в `BRIDGE-000001`.
- Надёжность: падение Add-in не должно повреждать `.PcbDoc`; запись только в отдельный пакет/лог.
- Совместимость: Altium Designer 26 + Altium SDK 4.0.1 + .NET SDK 10 с `net10.0-windows`
  подтверждена оператором (C-65); SOLIDWORKS 2026 — целевая платформа, детали не фиксировались.
- Разделение TFM по проектам — D-014; `net10.0-windows` не распространяется на Core и тесты.
- Локализация: сообщения UI — русский/английский по решению UI-этапа; коды ошибок — английские.
- Лицензии: сторонние библиотеки допускаются только с совместимой лицензией и записью в `DECISIONS.md`.

## 12. Явные non-goals

- Прямая работа с файлами `.PcbDoc` без Altium API.
- Обход лицензирования CAD-систем.
- DelphiScript и Legacy API.
- Автоматическое изменение платы без подтверждения пользователя.
- Синхронизация в реальном времени.
- Облачный сервис и сетевой протокол в текущей фазе.
- Передача пользовательских PCB, STEP и рабочих сборок через Git.
