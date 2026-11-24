---
id: BRIDGE-000010
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T01:05:00+03:00
updated: 2026-11-25T01:05:00+03:00
base: source
branch: agent/BRIDGE-000010-altium-3d-model-research
commit: pending
pr: pending
related: [BRIDGE-000009, BRIDGE-000010, V-06, V-26, V-27, V-28, V-29, V-30, V-31, V-32, V-33]
---

# BRIDGE-000010 — исследование извлечения 3D-моделей Altium

## 1. Итог

**Исследование выполнено. Производственный экспорт 3D-моделей не реализован.**

Подтверждено на Altium Designer 26.12.2.9 / SDK 4.0.1:

- все 3D Body находятся обычным board iterator с `TObjectId.eComponentBodyObject`;
- тело и его `IPCB_Model` связаны через `IPCB_PrimitiveHelper.GetState_Component` и
  `IPCB_ComponentBodyHelper.GetModel`;
- модели реально извлекаются из всех трёх тестовых плат;
- STEP AP214 B-Rep экспортируется через `IPCB_Model.ExportToStep` и
  `IPCB_ComponentBody.SaveModelToFile`; Parasolid X_T — через `SaveModelToFile`;
- embedded STEP/X_T можно сохранить в исходном размере, если использовать исходное
  расширение; иначе Altium пересобирает STEP;
- `IPCB_Model.GetOrigin`/`GetState` и body projection дают материал для размещения,
  но точная композиция для generic-моделей и Bottom-side остаётся открытой;
- исходные `.PcbDoc` не изменились.

Предложен контракт `models/models.json` + `assets/3d/...` для будущего Exchange Package;
Core 1.0.0, `BoardSnapshot` и JSON-схема не менялись.

## 2. Окружение и исходные данные

| Параметр | Значение |
|---|---|
| Altium Designer | 26.12.2.9 |
| Altium SDK | 4.0.1 |
| .NET SDK | 10.0.404 |
| TFM Altium Add-in | `net10.0-windows` |
| Core | `net10.0` |
| База ветки | `source`, `516e264a3cdea434a0d40ecffe1a06c2da0ffd6c` |

Исходные платы и SHA-256 до/после экспериментов:

| Плата | SHA-256 |
|---|---|
| `MiniPC.PcbDoc` | `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93` |
| `Bluetooth_Sentinel.PcbDoc` | `1E18C3EFA9D6C3245AA09CAB7268B2A908078E50ED2D09DC9D6DFD3BC36B95F3` |
| `board with cutout.PcbDoc` | `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753` |

Временный каталог экспериментов:
`%TEMP%\EcadMcadBridge\3d-research\` (`cutout`, `bluetooth`, `minipc`,
`minipc-offset`, `formats-cutout`, `formats-minipc`, `link-test2`, `link-test3`).
Файлы моделей и плат в Git не коммитились.

## 3. Объектная модель SDK

### 3.1 Перечисление тел

Использован типизированный board iterator:

```text
TObjectSet { TObjectId.eComponentBodyObject }
TIterationMethod.eProcessAll
FirstPCBObject / NextPCBObject
BoardIterator_Destroy
```

`IPCB_ComponentBody` реализует `IPCB_Region` и `IPCB_Primitive`. Поэтому у тела
доступны `GetState_Layer`, `GetState_UniqueId`, `UniqueName`, `GetState_SnapCount`,
`AxisCount`, `GetState_ModelHasChanged`, `GetStandoffHeight`, `GetOverallHeight`,
`GetBodyProjection`, `BoundingRectangle` и `GetGeometricPolygon`.

Связь с компонентом — `IPCB_PrimitiveHelper.GetState_Component(body)`; это
подтверждено на всех телах. `GetState_UniqueId` у тел пустой; `UniqueName` не
уникален (у J1 повторяется `22844634`), поэтому ordinal модели внутри компонента —
не стабильный ID.

Инвентаризация:

| Плата | Bodies | Models | Extruded | Generic | Embedded Generic | Компонентов с телами | Multi-body | Max bodies |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| cutout | 152 | 152 | 151 | 1 | 1 | 61 / 73 | 45 | 4 |
| Bluetooth | 152 | 152 | 151 | 1 | 1 | 61 / 73 | 45 | 4 |
| MiniPC | 1 055 | 1 055 | 20 | 1 035 | 1 035 | 1 042 / 1 046 | 7 | 3 |

Тела без модели: **0**. Компоненты без 3D-тела:
cutout/Bluetooth — 12 (`TP1…TP11`, `Altium Logo BOT1`), MiniPC — 4 (`MH1…MH4`).
У J1 — 4 тела, у C26 — 3, у C128/R2-подобных — до 3.

### 3.2 Модель

`IPCB_ComponentBodyHelper.GetModel(body)` возвращает `IPCB_Model`.
Подтверждённые метаданные:

- `GetState_ModelType`: `e3DModelType_Extruded`, `e3DModelType_Generic`,
  `e3DModelType_Cylinder`, `e3DModelType_Sphere`;
- `GetModelSource`: `eModelSource_Undefined`, `eModelSource_File`,
  `eModelsource_Vault`;
- `GetEmbed()`;
- `GetFileName()`;
- `IsEmpty()`, `IsStale()`, `GetDataSize()`, `GetChecksum()`;
- `GetState(out rotX, rotY, rotZ, out dz)`;
- `GetOrigin()` → `CoordPoint` (X/Y);
- `GetRotation()` — отдельное значение, не эквивалентное `GetState` rotZ;
- `GetDataHash("SHA256")` — `COMException` на всех проверенных моделях.

Фактические типы/признаки:

| Плата | ModelType | Embed | FileName непустой | State ≠ 0 |
|---|---|---:|---:|---:|
| cutout | 151 Extruded + 1 Generic | 1 true | 1 | 1 |
| Bluetooth | 151 Extruded + 1 Generic | 1 true | 1 | 1 |
| MiniPC | 20 Extruded + 1 035 Generic | 1 035 true | 1 035 | 894 |

`GetModelSource` на всех трёх платах был `Undefined`; это не позволяет отличить
linked от embedded. Признак `GetEmbed` различал их: embedded generic имел true.
Linked-моделей в реальных платах не найдено.

## 4. Инвентаризация representative-объектов

Полные inventory-файлы находятся во временных каталогах; ниже — характерные строки.
Все координаты — board IU, если не указано mm.

| ComponentId | Designator | ModelIndex | Type | Source | SourceFile | Format | File, size, SHA256 | Transform | Result |
|---|---|---|---:|---|---|---|---|---|---|
| `altium:component:SIPNHWFE` | R16 | 0 | Extruded | embedded/none | — | STEP | `R16-0/model.step`, 11 016 B, `60ca3fb4d1f9a150d3e8081d5e19935309202226e54e749abf259e946491add7` | origin `(602928263,389419376)`, rot 0, state 0 | OK |
| `altium:component:AICJNCOD` | R14 | 0 | Extruded | embedded/none | — | STEP | `R14-0/model.step`, 11 055 B, `d36c9869a57a637bf393cbb63963709052276b78ffd15e14c328ec4ad80bbd08` | rot 90, body bbox 0.55×0.225 mm | OK |
| `altium:component:MQRWFGQS` | R15 | 0 | Extruded | embedded/none | — | STEP | `R15-0/model.step`, 11 016 B, `b1f86de820e823096d6ce00f0e2c07761ef6337dee10c3f941e3c2dcbca9585d` | rot 180 | OK |
| `altium:component:IWHIYIIP` | R2 | 0 | Extruded | embedded/none | — | STEP | `R2-0/model.step`, 11 051 B, `64265facc307e7f2b2eebea7def2f2993796426ffa4649d770f30bc57e312284` | rot 270 | OK |
| `altium:component:DTJWMQGY` | J1 body 0 | 0 | Extruded | embedded/none | — | STEP | `J1-0/model.step`, 103 011 B, `91abe81ddcd1f25641793a80cb1a4e4f282ab4e94fe193ce103bce5d00ce1aec` | bottom, rot 180, 20×20×3.2 mm | OK |
| `altium:component:DTJWMQGY` | J1 body 1 | 1 | Extruded | embedded/none | — | STEP | `J1-1/model.step`, 60 209 B, `b48356fcbdc0b2268ccc1e8a9947cc32d5bced375e127d7e4f8582ed26b725ad` | standoff 4.0 mm, overall 4.1 mm, local z 0…0.1 mm | OK |
| `altium:component:QYVSTFQR` | L1 | 0 | Generic | embedded | `742792623.stp` | STEP/X_T | `L1-0/model.step`, 38 446 B, `6e1ebc675b3ece706ff864ae9069c2227c5f5607fffbffbfc65491407d28074a`; `L1-0/model-body.stp`, 55 221 B, `f7b1f34547a752792266472e5bb1c38ab5983f87992d08c626d5720ea6eda8b9` | state `(0,-89.96,90,153543)`, standoff −0.0103 mm, overall 0.7903 mm | OK |
| `altium:component:FFPUPAKY` | C63 | 0 | Generic | embedded | `CAPC3216X18M.step` | STEP | `C63-0/model.step`, 59 763 B, `b54da4c51b1bfc84f89dfbbd2719360d0924e72d32ed6e421f2a4e0ea0e10f5d`; original `model-body.step`, 142 343 B, `12635bc0c616a0904c63a17a0064981c86be6a421bf7168aebcb0947c735a6bf` | state `(0,0,90,0)`, origin board, rot 90 top | OK |
| `altium:component:XWMBTJJB` | C59 | 0 | Generic | embedded | `CAPC1005X06N.step` | STEP | `C59-0/model.step`, 60k B | state `(0,0,180,0)`, rot 180 top | OK |
| `altium:component:AFVBOKBM` | C573 | 0 | Generic | embedded | `CAPC1005X06L.step` | STEP | `C573-0/model.step` | bottom, component rot 90, state `(0,0,270,0)` | OK |
| `altium:component:FOERFJPY` | B2 | 0 | Generic | embedded | `747540101.stp` | STEP | `B2-0/model.step`, 1 189 874 B, `f914c6bf17fbf358ddb6e7964a04fa6a15ade67f4f3a38b6f48a5200bfd1c836`; original `model-body.stp`, 1 200 231 B, `97da630e15e82d6f9e5386f4fab53056a21c5da5cd9d34de41ecd2d7ddd29da4` | origin `(145807025,67588523)`, bbox center offset `(-0.350,-17.420) mm`, state `(0,0,90)`, dz −1.223 mm | OK |

## 5. Экспорт геометрии

### 5.1 Подтверждённые вызовы

| API | Результат | Ограничение |
|---|---|---|
| `IPCB_Model.ExportToStep(path)` | STEP AP214, B-Rep | Всегда reserialize; header timestamp делает SHA нестабильным между запусками |
| `IPCB_ComponentBody.SaveModelToFile(path)` | STEP по умолчанию; при совпадении расширения с исходным форматом сохраняет embedded bytes | Для extruded исходного файла нет; путь не должен попадать в пакет |
| `IPCB_Component.SaveModelToFile(path)` | STEP; для multi-body компонента объединяет тела | Нельзя использовать как per-body asset |
| `IPCB_Component.SaveModelToFileAsPart(path)` | STEP; другой набор сущностей/дополнительная геометрия | Не подтверждено как исходная B-Rep одного body |
| `body.SaveModelToFile(".x_t")` | Parasolid text (`**ABCDEF…`) | Только X_T подтверждён |
| `body.SaveModelToFile(".sat")` | `COMException` | Не поддерживается |
| `body.SaveModelToFile(".igs")` | `COMException` | Не поддерживается |
| `body.SaveModelToFile(".stl")` | `COMException` | Не использовать как замену B-Rep |

На cutout-плате выполнено **760** экспортных вызовов (152 тела × 5 API);
все файлы созданы, ошибок и пустых файлов нет. Для одного body point set
`model.ExportToStep` и `body.SaveModelToFile` совпал (LED, J1, L1). Для J1
`component.SaveModelToFile` содержит дополнительные точки от других тел (707 vs 412
для body 0), что подтверждает неоднозначность component-level API.

STEP-файлы содержат `MANIFOLD_SOLID_BREP`, `ADVANCED_FACE`, `CLOSED_SHELL`,
`EDGE_CURVE` и не содержат `TRIANGULATED_FACE_SET`; это B-Rep, а не mesh.
X_T-файл начинается с Parasolid-заголовка `**ABCDEFGHIJKLMNOPQRSTUVWXYZabcdef`.

Для embedded generic `body.SaveModelToFile` с исходным расширением дал размер,
равный `GetDataSize`:
- C63 `CAPC3216X18M.step`: 142 343 B = `GetDataSize`;
- L1 `742792623.stp`: 55 221 B = `GetDataSize`;
- B2 `747540101.stp`: 1 200 231 B = `GetDataSize`.
Это лучший путь сохранения исходной B-Rep. `ExportToStep`/другое расширение
пересобирает STEP и меняет размер/структуру.

### 5.2 Воспроизводимость файлов

STEP-экспорт содержит `FILE_NAME(...,'<timestamp>',...)` и product/assembly ID,
поэтому SHA-256 одного и того же тела может отличаться между запусками. Для
дедупликации в пакете нельзя полагаться только на SHA сгенерированного STEP:
нужна нормализация header/ID или сохранение исходных embedded bytes. Для embedded
generic исходные bytes стабильны; для extruded — SHA будет зависеть от экспорта.

## 6. Трансформации и координаты

### 6.1 Что подтверждено

- `IPCB_Model.GetOrigin()` — точка начала модели в **координатах платы**, не
  обязательно центр body bbox. На B2 origin `(145807025,67588523)`, центр bbox
  `(145944794,74447791)`: смещение `(-0.350,-17.420) mm`.
- Для простых extruded-моделей `GetOrigin` совпал с центром body bbox
  (152/152 на cutout/Bluetooth, 1 040/1 055 MiniPC; 15 смещённых — generic).
- `GetStandoffHeight`/`GetOverallHeight` дают Z-диапазон тела: R16 — 0…0.4 mm,
  J1 body 0 — 0…3.2 mm, J1 body 1 — 4.0…4.1 mm.
- `Component.GetState_Height()` **не** является высотой модели: J1 4.57 mm при
  максимальном body overall 4.1 mm.
- Body projection (`GetGeometricPolygon`) для 151 extruded-тел совпал по размерам
  с локальной STEP bbox **без применения component rotation**:
  R16 rot0, R15 rot180 — 0.225×0.55 mm; R14 rot90 — 0.55×0.225 mm;
  R2 rot270 — 0.55×0.225 mm. То есть polygon переведён в координаты платы по
  origin, но сохраняет локальную ориентацию; component rotation нужно применять
  отдельно.
- `model.ExportToStep` для extruded-моделей не зависит от component rotation:
  R16 (rot0) и R15 (rot180) имеют одинаковый model checksum и одинаковый point set.
  Для generic тот же `fileName` с разным `GetState` даёт разные point sets;
  `GetOrigin` при этом не запекается (C63/C62 — одинаковый point set при разных
  origin).
- `GetState` generic: C63 `(0,0,90,0)`, C59 `(0,0,180,0)`, C573 bottom
  `(0,0,270,0)`, L1 `(0,-89.96,90,153543)`, B2 `(0,0,90,-1223346)`. Значения
  выглядят как градусы/IU, но точная официальная семантика не подтверждена.
- `GetRotation()` не равен `GetState` rotZ (у L1/B2/C63 — 0 при rotZ 90).

### 6.2 Рабочая гипотеза композиции (не финальная)

Для extruded-тела:

```text
P_board = O_origin + Rz(componentRotation) * p_local
z_board = z_local + standoff/overall Z-offset
```

где `O_origin` — `GetOrigin()` в board coordinates, `p_local` — STEP geometry.
Body projection уже даёт локальный footprint, переведённый в `O_origin`; для
получения board footprint его нужно повернуть вокруг `O_origin` на component rotation.
Bottom-side дополнительно требует mirror/сторону; формула не подтверждена (V-30).

Для generic-моделей `GetState` уже содержит часть ориентации (совпадает с
component rotation на Top и меняет знак на Bottom), а `ExportToStep` может включать
эту ориентацию. Нельзя складывать component rotation и model state без проверки;
это главный открытый вопрос (V-27). Поэтому в предложенном контракте сохраняются
**все** компоненты преобразования отдельно, а не одна «итоговая матрица».

### 6.3 Проверенные сценарии

| Сценарий | Пример | Результат |
|---|---|---|
| Top, rot 0 | R16 | origin/bbox/model OK; STEP 11 016 B |
| Top, rot 90 | R14 | body bbox локальная 0.55×0.225; STEP OK |
| Top, rot 180 | R15 | тот же model checksum, что R16; STEP point set идентичен |
| Top, rot 270 | R2 | STEP OK; body bbox локальная ориентация |
| Bottom | J1 (layer 32) | 4 тела, standoff 4.0/0, STEP OK; mirror open |
| Несколько моделей | J1 (4), C26 (3) | все тела/модели найдены, component-level export смешивает их |
| Модель с offset | B2 | origin ≠ bbox center, state/dz ненулевые, STEP OK |
| Локальный origin не в центре | B2 (X/Y), extruded z=0 (Z) | origin из `GetOrigin`, Z из standoff/overall |
| Embedded | L1/C63 | embed=true, fileName basename, исходные bytes сохранены |
| Linked | нет в платах | искусственная модель: embed=true, absolute path; `SetEmbed(false)` работает; board linked не подтверждён |

## 7. Предложение для Exchange Package

**Статус: Proposed, не реализовано.**

Рекомендуемая структура:

```text
<package-id>/
├── manifest.json
├── board.snapshot.json
├── checksums.sha256
├── models/
│   └── models.json
└── assets/3d/<sha256>/<safe-name>.<ext>
```

`models/models.json` — отдельная схема `ecad-mcad-bridge/models` 1.0.0:
массив записей `id`, `componentId`, `bodyOrdinal`, `modelIndex`, `modelType`,
`sourceKind` (embedded/linked/converted), `sourceFileName` (только basename),
`assetPath`, `assetSha256`, `assetSize`, `format`, `units`, `transform`
(`originMm`, `rotationDeg`, `dzMm`, `bodyStandoffMm`, `bodyOverallHeightMm`,
`componentSide`, `bodyProjection`) и `result/warnings`.

Правила:

- asset path — относительный POSIX, имя генерируется из SHA-256/формата;
  исходное имя только санитизированный basename; absolute paths запрещены;
- `models.json` и assets входят в `manifest.files`; `checksums.sha256` уже
  покрывает nested files; reader проверяет размер/SHA и `PackagePath`;
- capability `3d-models`; старые пакеты без `models/` читаются как «модели
  отсутствуют»;
- `BoardSnapshot` 1.0.0 не меняется; при необходимости ссылок из snapshot —
  отдельный minor bump 1.1.0 и ADR;
- дедупликация по SHA-256, но для сгенерированных STEP нужна нормализация
  header/timestamp; исходные embedded bytes предпочтительнее;
- STL/mesh, ZIP/attachments, linked absolute paths и внешние ссылки запрещены.

## 8. Риски и открытые вопросы

1. **V-27 / композиция.** Для generic-моделей `GetState` уже содержит ориентацию,
   для extruded — нет; единая формула не доказана. Нужен controlled test с
   известной моделью и поворотами 0/90/180/270 Top/Bottom.
2. **V-30 / Bottom mirror.** J1 — bottom, но модель имеет положительные Z/standoff;
   явный mirror-флаг не найден. Требуется сравнение с Altium 3D Layout.
3. **V-29 / единицы `GetState`.** Похоже на градусы/IU, но не документировано;
   `dz` и `GetRotation` требуют отдельной проверки.
4. **V-26 / linked.** В реальных платах linked-моделей нет; `GetModelSource`
   всегда `Undefined`; `ModelFactory_FromFilename` создаёт embedded модель с
   absolute path; `SetEmbed(false)` меняет флаг, но board-linked не подтверждён.
5. **V-28 / hash.** `GetDataHash("SHA256")` не работает; использовать SHA файла.
6. **V-32 / сохранение исходной B-Rep.** Подтверждено для embedded generic по
   размеру; побайтовое равенство и linked не проверены.
7. **V-33 / MCAD.** STEP/X_T синтаксически B-Rep, но импорт в SOLIDWORKS 2026 и
   совпадение с Altium 3D Layout не проверены.
8. **Нестабильность SHA STEP.** Header timestamp/product ID меняет SHA между
   запусками; content-addressed asset должен либо сохранять исходные bytes, либо
   нормализовать файл.
9. **Privacy.** `GetFileName`/`ModelFactory_FromFilename` могут вернуть absolute
   path; в manifest можно писать только basename.

## 9. Проверки

```text
dotnet build -c Release   → 0 warnings, 0 errors
dotnet test -c Release    → Core 84/84, Altium 82/82, всего 166/166
git diff --check          → OK
```

Экспериментальный probe-код удалён до PR; в рабочем дереве только документация.
Исходные SHA-256 плат до/после экспериментов совпадают (см. раздел 2).
Типизированный Altium SDK; reflection/dynamic/Legacy API не использовались.
Файлы моделей и пользовательские платы в Git не добавлены.

## 10. Commit и PR

- Commit: `pending`.
- PR: `pending`; ветка `agent/BRIDGE-000010-altium-3d-model-research`, база `source`,
  merge запрещён.

## 11. История изменений

| Итерация | Что изменено |
|---|---|
| 1 | API-исследование и временный probe; инвентаризация cutout/Bluetooth/MiniPC |
| 2 | Экспорт STEP/X_T, форматы, embedded/linked, inventory representative |
| 3 | Анализ origin/body projection/state, offset-сценарий B2, документация и отчёт |
