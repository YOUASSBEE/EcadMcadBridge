---
id: BRIDGE-000002
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T10:00:00+03:00
updated: 2026-11-24T10:00:00+03:00
base: task/BRIDGE-000001
branch: agent/BRIDGE-000002-core-model
commit: pending
pr: pending
related: [BRIDGE-000002, BRIDGE-000001, D-005, D-009, D-010, D-014, D-015]
---

# BRIDGE-000002 — нейтральная модель PCB в Core

## 1. Итог

Реализована первая полноценная версия нейтральной модели платы в `EcadMcadBridge.Core`:

- добавлены сущности платы, слоёв, компонентов, падов, цепей, проводников, переходных отверстий,
  регионов и текста;
- геометрия хранится в IU и вычисляемых мм; дуги представлены аналитически;
- реализован детерминированный JSON `BoardSnapshotJson` с обратной десериализацией
  и записью UTF-8 без BOM;
- реализован независимый от CAD валидатор `BoardSnapshotValidator` со структурированными
  ошибками и предупреждениями;
- добавлены синтетические unit-тесты Core.Tests;
- обновлены `ARCHITECTURE.md`, `PROJECT_CONTEXT.md`, `SDK_API_LEDGER.md`.

Не реализовано намеренно (вне scope задачи):

- извлечение полного `BoardSnapshot` из Altium и adapter-преобразование;
- Exchange Package, manifest и checksums;
- генерация стабильных ID и проверка стабильности `SourceUniqueId`;
- SOLIDWORKS-код и построение нативной MCAD-модели;
- геометрическая проверка вложенности отверстий (point-in-polygon) в валидаторе.

Код Altium Add-in не менялся, новые Altium API не добавлялись.

## 2. Изменённые файлы

Новые файлы Core:

```text
src/EcadMcadBridge.Core/Model/Board.cs
src/EcadMcadBridge.Core/Model/Components.cs
src/EcadMcadBridge.Core/Model/Connectivity.cs
src/EcadMcadBridge.Core/Model/Geometry.cs
src/EcadMcadBridge.Core/Model/Values.cs
src/EcadMcadBridge.Core/Serialization/BoardSnapshotJson.cs
src/EcadMcadBridge.Core/Validation/BoardDiagnostic.cs
src/EcadMcadBridge.Core/Validation/BoardDiagnosticCodes.cs
src/EcadMcadBridge.Core/Validation/BoardSnapshotValidator.cs
src/EcadMcadBridge.Core/Validation/BoardValidationResult.cs
```

Новые тесты:

```text
tests/EcadMcadBridge.Core.Tests/BoardSnapshotFixture.cs
tests/EcadMcadBridge.Core.Tests/BoardSnapshotJsonTests.cs
tests/EcadMcadBridge.Core.Tests/BoardSnapshotValidatorTests.cs
```

Изменены:

```text
.agent/ARCHITECTURE.md
.agent/PROJECT_CONTEXT.md
.agent/SDK_API_LEDGER.md
.agent/reports/BRIDGE-000002-report.md
```

`EcadMcadBridge.Altium`, TFM, solution и существующий smoke-формат не изменялись.
Пользовательские PCB, DLL, STEP и рабочие данные в Git не добавлены.

## 3. Архитектура модели

### 3.1 Плата и сущности

| Сущность | Назначение | Основные ссылки |
|---|---|---|
| `BoardSnapshot` | Корневой снимок платы | `Properties`, `Outline`, коллекции |
| `BoardSnapshotSource` | Происхождение снимка без абсолютного пути | — |
| `BoardProperties` | Имя, материал, толщина | — |
| `BoardOutline` | Контуры платы, включая отверстия | `Contour` |
| `LayerRef` | Слой, сторона, тип | используется через `layerId` |
| `ComponentInstance` | Designator, Comment, Footprint, placement, height | `layerId` |
| `Transform` | Позиция, поворот, сторона | `Point2D` |
| `PadStack` | Набор форм пада | `PadShape` |
| `PadShape` | Форма, размеры, радиус, вершины | `layerId` (опционально) |
| `Pad` | Пад компонента | `componentId`, `netId`, `layerId`, `padStackId` |
| `Net` | Электрическая цепь | — |
| `Track` | Прямолинейный проводник | `netId`, `layerId` |
| `Via` | Переходное отверстие | `netId`, `startLayerId`, `endLayerId` |
| `Region` | Полигон/зона | `netId`, `layerId`, `PolygonGeometry` |
| `TextItem` | Текст на слое | `componentId` (опционально), `layerId` |

### 3.2 Геометрия

- `Point2D` — пара `xIu/yIu` + вычисленные `xMm/yMm`.
- `LengthValue` — исходный `int? Iu` + вычисленный `double? Mm`; публичный `Mm` нельзя
  изменить независимо от IU. Для чтения JSON внутренне сохраняется предоставленное mm,
  чтобы валидатор мог обнаружить несовпадение.
- `LineSegment` — start/end.
- `ArcSegment` — start/end/center/radius/sweepDirection; дуга не аппроксимируется.
- `Contour` — `id`, `role` (`outer` / `hole`), упорядоченные сегменты.
- `PolygonGeometry` — набор контуров; используется в `Region`.
- `Transform` — позиция, поворот, сторона компонента.

`Contour` поддерживает последовательности прямых и дуговых сегментов, а также несколько
контуров в `Region`, включая отверстия. Валидатор проверяет замкнутость, непрерывность,
роли контуров и наличие внешнего контура, но не выполняет point-in-polygon проверку
вложенности отверстий.

### 3.3 Идентификаторы

ID — непрозрачная непустая строка. Генерация ID, SHA-256, суффиксы коллизий и использование
`SourceUniqueId` как стабильного ключа не реализованы. `SourceUniqueId` хранится только как
provenance. D-010 остаётся `Proposed`; ID не выводятся из Designator или координат.

### 3.4 Единицы

Все линейные величины используют `LengthValue` или `Point2D`:

- координаты, толщина платы, высота компонента, ширина и высота пада, радиус скругления,
  ширина проводника, диаметр и отверстие via, радиус дуги, высота и толщина текста;
- исходный IU не округляется;
- mm вычисляется через `AltiumUnits.IuToMm` (D-015);
- JSON пишет пары `xIu/xMm`, `yIu/yMm`, `widthIu/widthMm`, `heightIu/heightMm`,
  `thicknessIu/thicknessMm`, `diameterIu/diameterMm`, `holeDiameterIu/holeDiameterMm`,
  `radiusIu/radiusMm`, `strokeWidthIu/strokeWidthMm`.

## 4. JSON-контракт

`BoardSnapshotJson.Serialize`, `SerializeToUtf8` / `Deserialize`:

- schema: `ecad-mcad-bridge/board-snapshot`, version `1.0.0`;
- UTF-8 без BOM, LF, инвариантные числа;
- фиксированный порядок верхнего уровня:
  `schema`, `schemaVersion`, `source`, `sourceUnits`, `units`, `board`, `layers`,
  `components`, `padStacks`, `pads`, `nets`, `tracks`, `vias`, `regions`, `texts`;
- верхнеуровневые коллекции сортируются по `id`; контуры сортируются по `id`,
  порядок сегментов внутри контура сохраняется;
- отсутствующие значения — явный `null`, пустые коллекции — `[]`;
- `NaN` и `Infinity` не допускаются; сериализатор отклоняет их;
- десериализация проверяет schema/version/единицы и восстанавливает модель;
- диагностический `SmokeSnapshotJson` остаётся отдельным форматом.

Пример полного JSON и описание полей — в `.agent/ARCHITECTURE.md`, раздел 5.2.

## 5. Валидация

`BoardSnapshotValidator.Validate` возвращает `BoardValidationResult`:

- `Errors` — snapshot непригоден для обмена;
- `Warnings` — данные неполные, но структура допустима;
- `IsValid` — отсутствие ошибок.

Проверяются:

- уникальность ID в каждой коллекции, включая формы падов и контуры;
- обязательные ID, Designator, имена слоёв/цепей, текст, ширина проводника, диаметры via;
- ссылки `componentId`, `layerId`, `netId`, `padStackId`, `startLayerId`, `endLayerId`;
- согласованность IU/мм (включая значения, прочитанные из JSON);
- конечность чисел и диапазон поворота `[-180, 180)`;
- отрицательные размеры, ширины, диаметры и радиусы;
- радиус дуги, положение start/end на окружности, sweepDirection;
- замкнутость и непрерывность контуров, наличие внешнего контура, роли outer/hole;
- непустые коллекции и неполные необязательные данные.

Коды: `EMB-VAL-001`…`EMB-VAL-011`, `EMB-VAL-101`, `EMB-VAL-102`.
Геометрия не исправляется автоматически.

## 6. Проверки

```text
dotnet build -c Release
Сборка успешно завершена.
Предупреждений: 0
Ошибок: 0

dotnet test -c Release
Пройден! : не пройдено 0, пройдено 30, пропущено 0, всего 30
```

Тесты покрывают:

1. создание и сериализацию платы со всеми типами объектов;
2. round-trip JSON → `BoardSnapshot` → JSON;
3. детерминированность и сортировку коллекций;
4. контуры с прямыми и дуговыми сегментами;
5. отверстия и несколько контуров в `Region`;
6. корректность ссылок и ошибку отсутствующей ссылки;
7. дублирующиеся ID;
8. несогласованные IU/мм;
9. отрицательные размеры и неконечный поворот;
10. nullable-поля и неизвестные значения;
11. пустые коллекции;
12. контрольные преобразования IU/мм D-015.

Отдельно проверены фиксированный порядок полей, невалидный радиус дуги и незамкнутый контур.

Smoke-тест в Altium не выполнялся: задача не меняет Add-in и не использует Altium SDK.

## 7. Неподтверждённые решения и риски

| Вопрос / риск | Статус | Влияние |
|---|---|---|
| Полнота модели относительно реального Altium `IPCB_*` | Не проверено | Модель проверена только синтетическими данными; adapter-экспорт — следующая задача |
| Стабильность `SourceUniqueId` | Не проверено (D-010/Q-008) | ID остаются непрозрачными; генерация отложена |
| Пространственная вложенность отверстий | Не реализовано | Проверяются роли, замкнутость и непрерывность, но не point-in-polygon |
| Отображение всех типов падов Altium в `PadShapeKind` | Не проверено | Возможны дополнительные виды форм; неизвестные значения остаются `null` |
| Соответствие имён слоёв/сторон Altium | Не проверено | Adapter должен подтвердить маппинг до объявления контракта окончательным |
| `LengthValue` хранит внутреннее provided-mm для диагностики | Реализовано | Публичный API не позволяет независимо менять mm; validator ловит расхождение |
| Exchange Package и checksums | Вне scope | Не реализованы, формат не смешивается с smoke |

## 8. Commit SHA и PR

- Commit: `pending` (будет заполнен после коммита).
- PR: `pending` (база `task/BRIDGE-000001`, ветка `agent/BRIDGE-000002-core-model`).
- PR не мержится автором; после слияния PR #2 база следующего PR может быть перенесена
  на `source`.

## 9. История изменений

| Дата | Изменение |
|---|---|
| 2026-11-24 | Первая версия отчёта: модель, JSON, валидатор, тесты, документация. |
