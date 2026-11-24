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
- пространственная вложенность отверстий и контуров не проверяется геометрическим
  point-in-polygon тестом; это известное ограничение текущей версии.

### 5.3 Идентификаторы

Core рассматривает ID как непрозрачную непустую строку. Генерация ID, SHA-256 и суффиксы
коллизий в `BRIDGE-000002` не реализованы. `SourceUniqueId` сохраняется только как provenance
и не считается доказанно стабильным; D-010 остаётся `Proposed`. ID не выводятся из Designator
или координат внутри модели. Стратегия стабильных идентификаторов — отдельная задача.

### 5.4 Altium adapter — BRIDGE-000003

Реализация: `EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter` и
`AltiumOutlineReader`. Adapter только читает `PCB.IPCB_*`, не содержит JSON и не меняет
плату. Команда `EcadMcadBridge:ExportBoardSnapshot` вызывает adapter, затем
`BoardSnapshotValidator` и `BoardSnapshotJson.SerializeToUtf8`; файл пишется в
`%TEMP%\EcadMcadBridge\board-snapshot-<UTC>.json`.

| Altium | Core |
|---|---|
| `IPCB_Board.LayerIterator()` / `IPCB_LayerObject` | `LayerRef` (V6-ID как opaque `altium:layer:<id>`, имя, kind, side) |
| `IPCB_Board.GetState_MasterStack().Get_ZTop/ZBottom` | `BoardProperties.Thickness` |
| `IPCB_Component` | `ComponentInstance` + `Transform` + `SourceUniqueId` |
| `IPCB_Pad` + copper layer shapes | `Pad` + `PadStack` + `PadShape` (per-layer width/height) |
| `IPCB_Net` | `Net` |
| `IPCB_BoardOutline.BoardOutline_GeometricPolygon()` | контуры и роли outer/hole |
| `IPCB_BoardOutline` как `IPCB_Polygon.GetState_Segments()` | `LineSegment`/`ArcSegment` с сохранением `Angle1/Angle2` |

Ограничения текущего adapter:

- Core `LayerKind` не различает signal и internal plane; оба представлены как `Copper`.
  Механические слои определяются по V6-ID, а не по имени.
- В Core 1.0.0 нет полей pad hole, pin designator, локальных координат пада, stack order
  и признака 3D-модели; adapter не выдумывает эти значения и пишет предупреждения.
- `eRounded` с разными размерами представлен rounded rectangle с радиусом
  `min(width,height)/2` и предупреждением; `eCustomShape` пропускается с предупреждением.
- Внутренние вырезы контура поддержаны через `GetState_IsHole`; на выбранных vendor-платах
  они не встретились, поэтому этот путь не подтверждён smoke-тестом.
- Полное соответствие Core всем структурам Altium не утверждается; adapter проверен на
  `MiniPC.PcbDoc` и `Bluetooth_Sentinel.PcbDoc` в AD26.12.2.9 (см. отчёт `BRIDGE-000003`).

## 6. Формат Exchange Package

Пакет — каталог в локальном runtime-каталоге или ZIP для внешней передачи, но **не в Git**;
структура:

```text
<package-id>/
├── manifest.json
├── board.snapshot.json
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
  "capabilities": ["board-outline", "component-placement"]
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
