---
id: BRIDGE-000014
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T15:30:00+03:00
updated: 2026-11-25T15:30:00+03:00
base: source
branch: agent/BRIDGE-000014-altium-3d-model-export
commit: 0b21301
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/13
related:
  - BRIDGE-000003
  - BRIDGE-000004
  - BRIDGE-000007
  - BRIDGE-000009
  - BRIDGE-000010
  - BRIDGE-000011
  - BRIDGE-000012
  - BRIDGE-000013
---

# BRIDGE-000014 — Altium 3D component body export

## 1. Итог

Команда `EcadMcadBridge:ExportBoardSnapshot` теперь пишет в Exchange Package не
только `board.snapshot.json`, но и:

- `models/models.json` с записью для каждого компонента snapshot;
- отдельный STEP AP214 asset для каждого успешно экспортированного тела;
- единый manifest/checksums через уже принятый Core Writer.

Реализовано:

- typed board iterator по `TObjectId.eComponentBodyObject`;
- связь body → owning component → `ComponentInstance.Id` через общий identity
  resolver;
- отдельный `bodyOrdinal` для каждого компонента;
- классификация representation/sourceOrigin без превращения `GetEmbed=false` в
  `linked`;
- `GetOrigin` X/Y (Z = null), `GetState` raw angles/dz и отдельный
  `GetRotation`;
- per-body `IPCB_ComponentBody.SaveModelToFile` с fallback
  `IPCB_Model.ExportToStep`;
- временный каталог `%TEMP%\EcadMcadBridge\model-export\<GUID>\` и удаление в
  `finally`;
- Core Writer с assets, затем Core Reader verification;
- partial inventory как Warning в UI.

Не реализованы и остаются вне scope: SOLIDWORKS Add-in/документы/размещение,
`T_B_S`, Bottom reflection, Euler composition, X_T alongside STEP, linked source
copying, обратный импорт.

## 2. База и окружение

| Параметр | Значение |
|---|---|
| База | `source`, `5da64a3fa70f3484ee652947f8c2c934b06361c9` |
| Ветка | `agent/BRIDGE-000014-altium-3d-model-export` |
| Altium Designer | 26.12.2.9 |
| Altium SDK | 4.0.1 |
| .NET SDK | 10 |
| TFM Altium | `net10.0-windows` |
| TFM Core | `net10.0` |

Smoke-платы — копии вне Git в
`%TEMP%\EcadMcadBridge\BRIDGE-000014\smoke\`:

| Плата | SHA-256 до | SHA-256 после |
|---|---|---|
| `multi.PcbDoc` (Bluetooth_Sentinel copy) | `1E18C3EFA9D6C3245AA09CAB7268B2A908078E50ED2D09DC9D6DFD3BC36B95F3` | тот же |
| `mini.PcbDoc` (MiniPC copy) | `50547E5ED4AA84C430CD1F3B3F5EDBF2ED6FB013E8C076A871C9BDE4B5749A93` | тот же |
| `cutout.PcbDoc` (board with cutout copy) | `73F7E7A9D3F88B5114BF86F0B71AB2F051039BA15C9EA73C25C399306F583958` | тот же |

Исходные vendor-платы не изменялись. Пакеты находятся в
`%TEMP%\EcadMcadBridge\BRIDGE-000014\packages\`; в Git не добавлены.

Версии Add-in согласованы на `0.4.0`: `ExportBoardSnapshotHandler.AddInVersion`,
`AltiumBoardSnapshotAdapter.ToolVersion`, `PluginFactory` log,
`ExportSmokeSnapshotHandler` и `Installation/EcadMcadBridge.ins`
(`0.4.0.0`).

## 3. Использованные API

Полный перечень и доказательства — `.agent/SDK_API_LEDGER.md`, C-94…C-103:
`eComponentBodyObject`, `GetState_Component`, `GetModel`, per-body
`SaveModelToFile`, `ExportToStep`, `GetOrigin`, `GetState`, `GetRotation`,
`GetEmbed`, `GetState_ModelType`, `IsEmpty`, `IsStale`.

`GetDataHash("SHA256")` не используется: в исследовании PR #9 он давал
`COMException`. SHA-256 считает Core Writer по фактическому файлу.

## 4. Архитектура реализации

Новые типы в `src/EcadMcadBridge.Altium/Adapter/`:

- `AltiumComponentIdentity` / `AltiumComponentIdentityResolver` — identity;
- `AltiumModelClassification` — representation/sourceOrigin;
- `AltiumModelMetadataMapper` — IU/mm/raw metadata;
- `AltiumBodyOrdinalTracker` — ordinal per component;
- `AltiumComponentModelRequestBuilder` — body inventory → Core request;
- `AltiumStepExportFile` — STEP file validation/delete;
- `AltiumModelExportWorkspace` — temp directory create/delete;
- `AltiumComponentModelReader` — COM enumeration and export.

`AltiumBoardSnapshotResult` теперь возвращает тот же `IPCB_Board` и список
snapshot identities. Handler использует один board object для snapshot и 3D
reader; повторный `GetCurrentPCBBoard()` посреди операции не вызывается.

## 5. Component identity

Порядок разрешения:

1. точный `GetState_UniqueId()` owner;
2. точный `GetState_SourceUniqueId()` owner;
3. fallback: designator + X/Y IU + normalized rotation + side.

Fallback принимается только при одном совпадении. Дубликат primary-ключа не
перезаписывает ID: resolver возвращает `Ambiguous`. Неизвестный owner —
`Unknown`. Оба случая в model reader считаются fatal
(`AltiumModelInventoryException`), пакет не создаётся.

Тесты: unique id, source id, unique fallback, ambiguous fallback, unknown,
duplicate key (не выбирает последний), duplicate same identity.

## 6. Body enumeration и ordinal

Используется:

```text
TObjectSet { TObjectId.eComponentBodyObject }
TIterationMethod.eProcessAll
FirstPCBObject / NextPCBObject
BoardIterator_Destroy
```

Каждое тело — отдельная запись. `bodyOrdinal` считается отдельно для каждого
componentId в порядке enumeration и увеличивается для каждого тела, включая
тела без модели/без asset. Ordinal не выдаётся за стабильный ID.

Smoke подтвердил multi-body: J1 — 4 тела (ordinal 0…3), C26 — 3. На multi/cutout
45 multi-body components, на MiniPC — 7.

## 7. Classification

`GetState_ModelType`:

| ModelType | Core |
|---|---|
| Generic | `generic` |
| Extruded | `native` |
| Cylinder | `native` |
| Sphere | `native` |
| unknown | `unknown` |

`sourceOrigin`:

1. `GetEmbed == true` → `embedded`;
2. procedural type без внешних признаков → `native`;
3. `GetEmbed == false` само по себе → не `linked`;
4. File/Vault/filename — внешний признак, но `linked` не подтверждён;
5. противоречие/недостаток → `unknown`.

`linked` не присваивается. Тесты покрывают все сценарии, включая
`GetEmbed=false + no external filename → native` и конфликтующий procedural type
→ unknown.

## 8. Metadata

- `GetOrigin`: X/Y сохраняются IU + mm через `AltiumUnits.IuToMm`; Z = null.
- `GetState`: X/Y/Z raw angles без нормализации, включая 360; `dz` IU + mm.
- `GetRotation`: отдельное nullable-поле, не заменяется state rotZ.
- При ошибке отдельного API — соответствующая группа null + warning; body entry
  сохраняется.
- `GetStandoffHeight`/`GetOverallHeight`/body projection в Core не пишутся.

Smoke: L1 Generic — `representation=generic`, `sourceOrigin=embedded`, state
`0,-89.96,90`; J1 Bottom — `native/native`; B-Rep STEP без mesh.

## 9. Export strategy

- Основной вызов: per-body `IPCB_ComponentBody.SaveModelToFile(path)`.
- Fallback: `IPCB_Model.ExportToStep(path)` только для модели этого body; в
  smoke fallback не потребовался (`fallbacks=0`).
- Component-level export и объединение multi-body не используются.
- После экспорта: файл существует, размер > 0, расширение `.step`; иначе файл
  удаляется.
- Успех: `assetAcquisition = sdk-export`, уникальный `assetKey`, формат
  `step-ap214`.
- Missing/empty/stale/failed: body entry остаётся с `not-acquired`, asset
  отсутствует, warning.
- `source-copy-verified` не используется.
- STEP SHA — идентификатор конкретного экспортированного asset, не стабильный ID
  физической модели между экспортами (header timestamp/product ID).

## 10. Интеграция команды

Pipeline:

1. Проверка активного PCB, `GetModified=false`.
2. Snapshot + component identities.
3. Модельный каталог и per-body STEP export в temp.
4. `TryRecheck` исходного документа.
5. SHA-256 исходного `.PcbDoc`.
6. `ExchangePackageWriter.Write(..., modelRequest)`.
7. `ExchangePackageReader.Read` verification.
8. UI summary; partial inventory → Warning.
9. temp cleanup в `finally`.

Если owner не разрешён, документ изменился или Reader не подтвердил пакет —
пакет не публикуется (при ошибке Reader уже опубликованный пакет удаляется).
Capabilities: Writer добавляет `component-3d-models`; остальные прежние
capabilities сохранены.

## 11. Фактический smoke

| Плата | PackageId | Components | Bodies | STEP assets | Multi-body | Model warnings | Package warnings | Model ms | Total ms |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| multi | `4f0f3f17-b06b-4905-b550-08ddb4482ca4` | 73 | 152 | 152 | 45 | 0 | 28 | 3 768 | 6 042 |
| mini | `a609896e-4f3c-4cf6-bd47-5238abe83c0b` | 1 046 | 1 055 | 1 055 | 7 | 0 | 1 029 | 79 905 | 122 796 |
| cutout | `052375f2-7672-4fd2-8267-17c91c663587` | 73 | 152 | 152 | 45 | 0 | 28 | 3 707 | 5 083 |

Для всех трёх:

- `withModel = bodies`, `missingModel=0`, `emptyModel=0`, `staleModel=0`,
  `exportFailures=0`, `metadataWarnings=0`, `bodiesWithoutAsset=0`;
- Core Reader успешно прочитал пакет (`PACKAGE_EXPORT_OK` пишется только после
  `Read`);
- `models.json`/manifest не содержат `C:\`, temp path или `.PcbDoc`;
- исходный SHA-256 до/после совпадает, `GetModified` остался false (повторная
  проверка handler-а прошла);
- cutout: 2 Board Cutout контура сохранены, 3D export их не менял.

Проверенные representative STEP:

- L1 Generic/Embedded — B-Rep, `MANIFOLD_SOLID_BREP`, без `TRIANGULATED_FACE_SET`;
- J1 Bottom/Extruded — B-Rep, все 4 тела отдельные;
- multi-body R14/C25 — отдельные assets на каждое тело.

## 12. Тесты и build

- `dotnet build -c Release` — 0 warnings, 0 errors.
- `dotnet test -c Release`: Core 140/140, Altium 122/122, всего 262/262.
- `git diff --check` — OK.
- GitHub CI не запускался; статусы проверок отсутствуют.

Добавлены Altium tests:

- representation/sourceOrigin, `GetEmbed=false` не linked;
- metadata IU/mm, Z null, raw 360, dz, rotation;
- resolver unique/source/fallback/ambiguous/unknown/duplicate;
- body ordinal per component;
- per-body entry сохраняется при missing asset;
- STEP file validation;
- Altium request → Core Writer → Core Reader round-trip:
  все компоненты, empty bodies, multi-body, assets, SHA/size, no orphan,
  no absolute paths.

## 13. Ограничения и открытые вопросы

- Полная матрица `T_B_S`, Z/Bottom, Euler composition и SOLIDWORKS placement не
  вычисляются; V-27/V-30 остаются открытыми.
- `linked`-модели в smoke не встречались; классификация `linked` намеренно не
  реализована. `GetEmbed=false` не считается доказательством linked.
- STEP SHA нестабилен между экспортами из-за header timestamp/product ID;
  content-addressed path действителен только внутри пакета.
- `bodyOrdinal` не является стабильным ID между сессиями/версиями PCB.
- Fallback identity по designator+X/Y+rotation+side зависит от совпадения
  snapshot-полей; при неоднозначности экспорт останавливается.
- Внешние absolute paths моделей не сохраняются и не логируются.
- SOLIDWORKS importer/placement — следующий этап; успешный STEP B-Rep не
  доказывает корректность placement.

## 14. Commit и PR

- Commit: `0b21301` (`feat(altium): export component 3D bodies to exchange package`).
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/13, ветка
  `agent/BRIDGE-000014-altium-3d-model-export`, base `source`, merge запрещён.

## 15. История изменений

- 2026-11-25: реализованы Altium per-body extraction, identity resolver,
  classification/metadata, temp export, handler integration, tests, smoke и
  документация. Первый commit `0b21301`; PR #13 открыт без merge.
