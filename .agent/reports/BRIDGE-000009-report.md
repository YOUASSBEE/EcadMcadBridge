---
id: BRIDGE-000009
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T23:28:00+03:00
updated: 2026-11-24T23:33:00+03:00
base: source
branch: agent/BRIDGE-000009-complete-altium-snapshot
commit: 1f44629
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/8
related: [BRIDGE-000003, BRIDGE-000004, BRIDGE-000007, BRIDGE-000009, D-010, D-016]
---

# BRIDGE-000009 — полный Altium BoardSnapshot: Tracks, Vias, Regions, Texts

## 1. Итог

**BRIDGE-000009 выполнен.**

Altium adapter теперь заполняет все четыре ранее пустые коллекции `BoardSnapshot`:

- `Tracks` — `IPCB_Track` / `eTrackObject`;
- `Vias` — `IPCB_Via` / `eViaObject`;
- `Regions` — `IPCB_Region` / `eRegionObject`, кроме Board Cutout;
- `Texts` — `IPCB_Text` / `eTextObject`.

Схема `BoardSnapshot` 1.0.0 и JSON-контракт не менялись. Board Cutout по-прежнему
извлекается только существующим outline-механизмом и не дублируется в `Regions`.
Обратный импорт и SOLIDWORKS не затрагивались.

Дополнительно manifest пакета объявляет новые capabilities:
`regions`, `texts`, `tracks`, `vias`.

## 2. Изменённые файлы

| Файл | Изменение |
|---|---|
| `src/EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter.cs` | чтение Tracks/Vias/Regions/Texts, диагностика, fallback/уникальность ID, обновление `AltiumRegionInfo` |
| `src/EcadMcadBridge.Altium/Adapter/AltiumSnapshotMapping.cs` | новые чистые mapping-функции Track/Via/RegionKind/Contours/Text |
| `src/EcadMcadBridge.Altium/Application/ExportBoardSnapshotHandler.cs` | capabilities `regions`, `texts`, `tracks`, `vias` |
| `tests/EcadMcadBridge.Altium.Tests/AltiumSnapshotMappingTests.cs` | 15 новых тестов mapping, единиц, ссылок, дублей ID, контуров и rotation |
| `.agent/SDK_API_LEDGER.md` | подтверждения C-90…C-93 и фактические результаты smoke |
| `.agent/ARCHITECTURE.md` | таблица Altium → Core, ограничения region/text/ID, capabilities |
| `.agent/PROJECT_CONTEXT.md` | статус BRIDGE-000009 и Q-010 |
| `README.md` | текущий этап |

## 3. Реализация

### 3.1 Tracks

- Итератор с фильтром `TObjectSet { TObjectId.eTrackObject }` и `eProcessAll`.
- Читаются `GetState_X1/Y1/X2/Y2/Width`, layer, net и `GetState_UniqueId`.
- Нулевая длина или неположительная ширина пропускаются со счётчиком и ограниченным
  warning.
- В `Track` попадают `Id`, `NetId`, `LayerId`, `Start`, `End`, `Width`.

### 3.2 Vias

- Фильтр `TObjectId.eViaObject`.
- Читаются position, `Size`, `HoleSize`, `StartLayer`/`StopLayer`, net и ID.
- Некорректные размеры (`size <= 0`, `hole < 0`, `hole > size`) или отсутствующие
  слои пропускаются со счётчиком и warning.
- В `Via` попадают `Id`, `NetId`, `Position`, `Diameter`, `HoleDiameter`,
  `StartLayerId`, `EndLayerId`.

### 3.3 Regions

- `AltiumRegionInfo` расширен layer/net/keepout; контуры читаются для всех регионов.
- В Core `Region` попадают все регионы, кроме `eRegionKind_BoardCutout`.
- Board Cutout остаётся исключительно в `BoardOutline`: adapter не добавляет его
  повторно и не меняет существующий clip-механизм
  `ClipWithPolyRegShapes(false,false)`.
- `eRegionKind_Copper` → `Copper`, keepout — `KeepOut`.
- `eRegionKind_NamedRegion` / `eRegionKind_Cutout` / `eRegionKind_Cavity` не имеют
  представления в Core 1.0.0 → `Other` со счётчиком `regionOtherKinds` и warning.
- Неизвестное значение enum → `Other` со счётчиком `regionUnknownKinds`.
- Контуры и роли outer/hole сохраняются; вырожденные контуры пропускаются.
- Геометрия берётся из `GetGeometricPolygon()` как line segments. Аналитические
  дуги регионов не подтверждены безопасным API, поэтому выдаётся явный warning
  (`regionGeometryFallbacks`); Q-010 остаётся частично открытым.

### 3.4 Texts

- Фильтр `TObjectId.eTextObject`.
- `IsDesignator()` и `IsComment()` пропускаются, чтобы не дублировать designator и
  comment компонентов.
- Barcode-тексты пропускаются: Core `TextItem` не представляет barcode.
- Пустой текст пропускается.
- Позиция, layer, height, stroke width и rotation читаются через
  `IPCB_RectangularPrimitive`; нефинитный rotation не подменяется нулём.
- Связь с компонентом подтверждается через `GetState_Component()` и
  `UniqueId`/`SourceUniqueId` компонента; при отсутствии компонента в snapshot
  `ComponentId` остаётся пустым, пишется ограниченный warning.

### 3.5 IDs и детерминизм

- Для всех примитивов сначала используется `GetState_UniqueId`.
- На всех трёх smoke-платах этот API вернул пустую строку для tracks/vias/regions/texts,
  поэтому фактически применён fallback `altium:<kind>:fallback:index:<ordinal>`.
- Ordinal — порядок исходного объекта в итераторе; ID непустые и уникальные внутри
  коллекции.
- Дубликаты внутри коллекции дополнительно разводятся суффиксом `:dup:<index>`.
- Коллекции сортируются по `Id` ordinal.
- Стабильность ID между сессиями не утверждается: D-010 остаётся `Proposed`.

## 4. Неподтверждённые вопросы и риски

1. **Q-010, region arcs.** Геометрия регионов экспортируется line-fallback из
   geometric polygon; аналитические дуги регионов не подтверждены. Для плат с
   дуговыми регионами форма может быть аппроксимирована. Warning и счётчик есть.
2. **ID без source unique id.** `GetState_UniqueId` пуст для tracks/vias/regions/texts
   на проверенных платах. Потребитель не должен считать ID стабильными между
   сессиями или редакциями платы без отдельного решения (D-010).
3. **Region kinds.** `eRegionKind_Cutout` на MiniPC (104) и других платах (4)
   экспортированы как `Other`; семантика cutout в Core 1.0.0 отсутствует. Это
   осознанно и сопровождается warning; при необходимости отдельный ADR/задача.
4. **Text coverage.** TrueType font outline, barcode и designator/comment не
   представляются в Core 1.0.0. На cutout/Bluetooth подтверждена 1 связь text →
   component, на MiniPC — 0; остальные тексты свободные.
5. **Board Cutout без clip-результата.** На Bluetooth (3 BoardCutout region) и MiniPC
   (1) клиппер не вернул вырезов; поведение совпадает с BRIDGE-000007 и не является
   регрессией. Реальный cutout-борд даёт 2 выреза / 16 сегментов.
6. **Версия Add-in.** Осталась `0.3.0`; расширен только набор capabilities. Если
   требуется отдельная версия для нового контракта данных — решение Оператора.

## 5. Проверки

### 5.1 Build и tests

```text
dotnet build -c Release      → 0 warnings, 0 errors
dotnet test -c Release       → Core.Tests 78/78, Altium.Tests 80/80, всего 158/158
```

Окружение: Altium Designer `26.12.2.9`, Altium SDK `4.0.1`, .NET SDK `10.0.404`,
TFM `net10.0-windows`.

### 5.2 Smoke — три платы

Сценарий: открыть `.PcbDoc` в AD26, выполнить `Tools → Export Board Snapshot`,
прочитать пакет через `ExchangePackageReader`, проверить `BoardSnapshotValidator`,
сравнить SHA-256 исходного `.PcbDoc` до и после.

| Плата | Package ID | Tracks | Vias | Regions | Texts | Outline contours | Reader | Validation |
|---|---|---:|---:|---:|---:|---|---|---|
| `board with cutout.PcbDoc` | `2f783715-dd40-4903-803c-b3543a5b51c8` | 10 657 | 173 | 12 | 87 | 3 (Outer 1 / Hole 2) | success, 0 errors | 0 errors, 28 warnings |
| `Bluetooth_Sentinel.PcbDoc` | `a173ec17-0de0-4d05-8ca4-30eaefd2a8a0` | 10 038 | 173 | 12 | 87 | 1 (Outer) | success, 0 errors | 0 errors, 28 warnings |
| `MiniPC.PcbDoc` | `7a2454de-b150-476f-b92c-880a9b8cbf46` | 48 299 | 4 025 | 381 | 34 | 1 (Outer) | success, 0 errors | 0 errors, 1 029 warnings |

Дополнительные проверки на прочитанных пакетах:

- все ID непустые и уникальные внутри каждой коллекции;
- нет отсутствующих layer/net/component-ссылок;
- tracks: 0 нулевой длины, 0 неположительной ширины;
- vias: 0 `holeDiameter > diameter`, 0 нулевого диаметра;
- texts: 0 дублей designator/comment, 0 пропущенных component-ссылок;
- Board Cutout на cutout-борде: 5 регионов, 2 clip cutout, 16 сегментов,
  `duplicateCutoutContours=0`, outline сохранён (1 outer + 2 hole);
- manifest capabilities: `board-outline, component-placement, layers, nets, pad-stacks,
  pads, regions, texts, tracks, vias`;
- `documentSha256` в manifest совпадает с SHA-256 исходного `.PcbDoc`.

SHA-256 исходных файлов до и после smoke не изменились:

```text
MiniPC.PcbDoc              50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93
Bluetooth_Sentinel.PcbDoc  1E18C3EFA9D6C3245AA09CAB7268B2A908078E50ED2D09DC9D6DFD3BC36B95F3
board with cutout.PcbDoc   ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753
```

### 5.3 Ограничения smoke

- Smoke выполнялся через UI-автоматизацию Altium; это ручной сценарий, а не
  unit/integration test в CI.
- Пакеты и `.PcbDoc` остаются вне Git (`%TEMP%\EcadMcadBridge\smoke-000009-final4`).
- Reader warnings на всех платах — преимущественно `EMB-VAL-101` для layer side,
  унаследованные от Core 1.0.0; новых ошибок нет.

## 6. Commit и PR

- Feature commit: `1f44629` (`feat(altium): complete BoardSnapshot tracks/vias/regions/texts (BRIDGE-000009)`).
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/8 — направлен в `source`,
  автором не мержится.

## 7. История изменений

| Итерация | Что изменено |
|---|---|
| 1 | API-исследование Track/Via/Region/Text, реализация чтения и mapping |
| 2 | Чистые mapping-функции, Altium.Tests, диагностика и уникальность ID |
| 3 | Region kind diagnostics, capabilities пакета, документация |
| 4 | Финальный build/test и smoke трёх плат; SHA-256 подтверждены |
