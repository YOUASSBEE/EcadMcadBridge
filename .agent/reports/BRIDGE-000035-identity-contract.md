---
id: BRIDGE-000035
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-27T00:00:51+03:00
updated: 2026-11-27T00:00:51+03:00
base: source
branch: task/BRIDGE-000035-top-only-smoke
commit: 9c1652319241489f271fddf174ec7fe4f3fe6611
pr: none (no PR created by this task)
related: [BRIDGE-000034, BRIDGE-000028, BRIDGE-000029, BRIDGE-000030, BRIDGE-000033]
---

# BRIDGE-000035 — Identity acceptance contract для ограниченного Top-only smoke

## 1. Назначение и границы контракта

Этот документ фиксирует **минимальный, доказанный набор identity-полей** для первого
контролируемого Top-only ECAD–MCAD smoke. Контракт действует только для поддерживаемого
класса из D-022: `Top component`, `Top BodyProjection`, `Generic`, `Embedded`, `STEP AP214`,
`SdkExport`, `PlacedBodyEffective`, `ModelRotationDegRaw == 0`.

Контракт **не** расширяет D-010, **не** активирует D-024, **не** объявляет per-instance
`UniqueID` library identity и **не** требует недоступный library GUID там, где Top-only
безопасно работает на доказанном per-package ключе.

Итог: library footprint/body identity для Top-only **не требуется**, если доступны
`ModelOrigin.X/Y` из placed body и полный набор полей категории A ниже. D-024 остаётся
`Accepted (contract only; production activation deferred)`; production Bottom остаётся OFF.

## 2. Источники доказательств

- `A` — `src/EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter.cs`
- `IB` — `src/EcadMcadBridge.Altium/Adapter/AltiumComponentIdBuilder.cs`
- `IR` — `src/EcadMcadBridge.Altium/Adapter/AltiumComponentIdentityResolver.cs`
- `MRO` — `src/EcadMcadBridge.Altium/Adapter/AltiumComponentModelReader.cs`
- `OT` — `src/EcadMcadBridge.Altium/Adapter/AltiumBodyOrdinalTracker.cs`
- `CM` — `src/EcadMcadBridge.Core/Model/ComponentModels.cs`
- `SW` — `src/EcadMcadBridge.SolidWorks/Placement/SolidWorksPlacementRequestBuilder.cs`
- `PTB` — `src/EcadMcadBridge.Core/Placement/PlacementTransformBuilder.cs`
- `L` — `.agent/SDK_API_LEDGER.md`
- `D` — `.agent/DECISIONS.md`
- `R28/R29/R30/R33` — отчёты BRIDGE-000028/000029/000030/000033.

Ключевые цитаты:

- `D:567-573`: Top-only список — только Top/Top, Generic Embedded, STEP AP214, SdkExport,
  PlacedBodyEffective; XY board frame по минимуму Outer; Z только из `Thickness`;
  `ModelRotationDegRaw` обязан быть `0`.
- `D:671-675,679-682`: D-024 key =
  `(footprintSourceUniqueId, bodyUniqueId, bodyOrdinal, stepAssetSha256)`.
- `L:299` (C-98): `X/Y origin модели в координатах платы; Z API не предоставляет`.
- `L:333` (V-07): «Стабилен ли `SourceUniqueId` между сессиями и ECO? Не проверено».
- `R29:159-160`: `UniqueID` у двух CAP-размещений различен — это instance ID, не library identity.
- `R30:133-138`: Pattern и `PackageSpecificHash` стабильны на observed библиотеках, но не имеют
  доказанного provenance.
- `R28:121`: asset SHA идентифицирует байты экспорта, а не library footprint/body.
- `R33:120`: V-16 остаётся `unverified`.

## 3. Категории полей

| Категория | Определение | Последствие |
|---|---|---|
| **A** | Обязательно для Top-only | Отсутствие или неизвестное значение → `Unsupported`, без transform |
| **B** | Необязательно, но полезно для диагностики/трассировки | Отсутствие не блокирует Top-only, если категория A полна; значение пишется как `null`/diagnostic |
| **C** | Запрещено использовать как замену library identity без доказательства provenance | Может фигурировать только как diagnostic; не может закрывать D-024 key |

## 4. Категория A — обязательные поля Top-only

| Поле | Назначение; источник и provenance | Повторные чтения / save-reopen | Необходимость; отсутствие; неизвестное значение |
|---|---|---|---|
| `ComponentInstance.Id` | Join snapshot ↔ catalog ↔ import. `GetState_UniqueId()` → `SourceUniqueId()` → `designator:index` (`A:1293-1302`, `IB:22-31`, `A:1603-1627`), resolver `IR:46-50`. | Детерминирован в одной сессии/порядке enumeration;исходные instance стабильны после reopen (`R29:152-153`); новые placement получают новые ID (`R29:159-160`); index-fallback нестабилен после ECO. | **Обязательно.** Отсутствие → `SW-PLC-003` (`SW:85-92`); неоднозначность → fail-closed (`IR:60-63`). Для smoke: проверить неизменность набора ID до/после export, но не заявлять кросс-сессионную стабильность. |
| `ComponentSide` | `GetState_Layer()` / `GetState_FlippedOnLayer()` (`A:1310,1629-1638`, `L:165`). | Стабильно при неизменённом placement; flip меняет значение намеренно (`L:191-192`). | **Обязательно.** Только `Top`; иначе `SW-PLC-006` (`SW:315-319`), transformation не строится. |
| `ComponentPosition` X/Y | `GetState_XLocation/YLocation()` в IU → mm (`A:1307-1309`, `D:210-216`). | Стабильно при повторном чтении и после save/reopen исходных instance (`L:186-187`). | **Обязательно.** Отсутствие/не-final → `SW-PLC-013` (`SW:357-363`); fallback ≠ 0. |
| `ComponentRotationDeg` | `GetState_Rotation()` (`A:1333-1338`), CCW, градусы. | Стабильно для неизменённого placement. | **Обязательно.** Отсутствие/не-final → `SW-PLC-013`; используется в `Rz(−θ)·(Origin−Position)` и pivot-вращении. |
| `BodyProjection` | `GetBodyProjection()` → `ComponentBodyProjection` (`MRO:414-429`, `L:182,192`). | Сохраняется после save/reopen; меняется при flip. | **Обязательно.** Только `Top`; `Unknown` или mismatch → `SW-PLC-007` (`SW:321-325`). |
| `ModelOrigin.X/Y` | `IPCB_ModelHelper.GetOrigin()` →IU `X/Y` → mm (`MRO:533-548`, `CM:121-134`, `L:299`). Абсолютные координаты платы, не model-local/STEP/bbox. | Повторные чтения стабильны; 15 тел дали residual `0.000004577 mm` против STEP placement (`R29:211-218`). Отдельного save/reopen-измерения origin в R29 нет. | **Обязательно только X/Y.** Отсутствие → `SW-PLC-014` (`SW:365-369`); Z недоступен по контракту и не подставляется. |
| Model state `(rx, ry, rz, dz)` | `GetState(out rx,ry,rz,dz)` (`MRO:551-560`, `L:300`). Для `PlacedBodyEffective` `rz = raw.rz − θ`; rx/ry/dz без изменения (`PTB:84-104`). | Raw angles сохраняются; после reopen `rz` нормализуется в `[0,360)` (`L:193`). | **Обязателен полный набор.** Отсутствие/не-final → `SW-PLC-015`; rotation/translation считаются только из sandwich-формулы, не из догадок. |
| `ModelRotationDegRaw` | Отдельный `GetRotation()` (`MRO:569-577`, `L:301`), не заменяет `GetState.rotZ`. | Требуется как gate, отдельно не измерялся в R29. | **Обязательно и должно быть 0** (`DEC:573`, `SW:380-390`); иначе `Unsupported`. |
| `StandoffHeightMm` | `GetStandoffHeight()` (`MRO:431-446`, `L:182`). | Повторные чтения стабильны; `SetBodyProjection` не меняет (`L:192`); кросс-сессия не измерялась. | **Обязательно.** Отсутствие/не-final → `SW-PLC-019` (`SW:392-396`). |
| Model kind/source | `GetState_ModelType/GetEmbed/GetModelSource/GetFileName` (`MRO:196-215,465-531`, `L:302-304`). | Классификация стабильна по типу; runtime `GetState_ModelType` через bridge не читался (`R29:255-256`). | **Обязательно.** Требуется `Generic + Embedded + StepAp214 + SdkExport + PlacedBodyEffective`; иначе `SW-PLC-008…012`. |
| `BodyOrdinal` | Zero-based ordinal внутри компонента (`OT:3-13`, `CM:85-88`). | Стабилен при том же порядке enumeration; отдельного save/reopen-измерения нет; observed `0` для 15 тел (`R28:171`). | **Обязательно** для multi-body matching; отсутствие → catalog/D-024 fail-closed; отрицание/unknown → Unsupported. |
| STEP asset reference | Путь + SHA-256 байтов экспорта (`SW:57-59,309-313`, `L:306-307`, `ExchangePackageWriter.cs:199-206`). | Стабилен внутри одного package; **не стабилен между повторными экспортами** из-за STEP header (`D:434-435`, `R28:121`). | Для Top **не identity**: absence → `SW-PLC-005`; unknown → package validation failure. Для D-024 — обязательная часть key. |

## 5. Категория B — необязательные, но полезные поля

| Поле | Назначение | Стабильность | Поведение при отсутствии / неизвестном |
|---|---|---|---|
| `sourceUniqueId` | Вторичный resolver key (`A:1305-1306,1331`, `IR:49`, `L:102,165`). | Стабилен у исходных instance после reopen; пустой у новых placement (`R29:109,161-165`); может быть `null` после `LoadFromLibrary` (`L:190`); кросс-сессия/ECO не проверена (`L:333`). | Отсутствие → fallback на `UniqueId`/`designator:index`; unknown → diagnostic. **Не library identity.** |
| `UniqueID` raw | Per-instance component id (`L:165`, `A:1294`, `IR:46`). | Original instance стабилен после reopen; новый placement даёт другой ID; body `UniqueID` пуст (`R29:83,162`). | Отсутствие → fallback (`IB:24-30`); unknown → diagnostic. **Per-instance, не library identity.** |
| `designator` | Отображение, фильтр `--designators`, диагностический путь (`A:1285-1291`, `SW:21,188-195`). | Стабилен по типу после reopen; пустой → synthetic `UNNAMED_n`. | Отсутствие не блокирует transform; unknown → `SW-PLC-017`. |
| `Pattern` | Имя footprint как diagnostic hint (`L:99`, `A:1330`). | Стабилен для типа после reopen (`R29:75,157`). | Diagnostic только; **не library ID**. |
| Model filename | Путь модели (`MRO:516-530`, `L:302`). | Зависит от FS (`R29:80`). | Diagnostic только; **не library ID**. |
| Package capability/source enum values | Полезны для трассировки `SdkExport`, `StepAp214` и т.п. | Стабильны по типу. | Не заменяют обязательные category A проверки; unknown остаётся `Unknown`, не `true`. |

## 6. Категория C — запрещённые подстановки library identity

| Кандидат | Почему запрещён | Разрешённое использование |
|---|---|---|
| `footprintSourceUniqueId` | Требуется D-024, но во всех доступных `.PCBLIB` = `unavailable`; ItemGUID/ItemRevisionGUID/VaultGUID имеют только signature-only proof (`R28:35-40,118-120`, `R29:68-70`, `R30:80-81`). | Только как missing field D-024; Top-only его не требует. |
| `bodyUniqueId` | Empty на всех 15 body и библиотеках; `Identifier` = `0402 cap` не уникален (`R29:83-85,114-115`). | Только как missing field D-024; Top-only его не требует. |
| ItemGUID / ItemRevisionGUID / VaultGUID / model GUID | Runtime `Not a PCB property`; equivalence to D-024 key не доказана (`R29:68-82,96-101`, `R30:151-160`). | Diagnostic; без отдельного ADR не повышать статус. |
| `IPCB_Model.GetChecksum()` (`uint`) | Runtime не измерен; `ModelHash` меняется; `GetDataHash("SHA256")` runtime unsupported (`L:196,194`, `R30:83-89`). | `null`/diagnostic; не STEP SHA, не library ID. |
| `PackageSpecificHash` | Стабилен на observed libraries, но provenance и публичный SDK-путь не доказаны (`R29:113,157`, `R29:242-244`, `R30:133-138`). | Diagnostic; **не** `footprintSourceUniqueId`. |
| `Pattern`, filename, designator, per-instance `UniqueID`, `SourceUniqueId` | Стабильность/provenance не доказаны: instance scope, FS dependence, schematic-instance scope (`R29:159-161`, `R30:133-138`, `L:333`). | Diagnostic/внутрипакетный join; **не** library identity. |
| bbox corner как `ModelOrigin` | Даёт `+0.524002 / +0.499999 / +1.25000004 mm` прокси-offset, это другая величина (`R28:61-70,246-267`). | Запрещено; `ModelOrigin` должен из `GetOrigin()`. |
| `e≈0` как calibration | `max |e| = 0.00000254 mm` по reference point без D-024 key и независимого gate (`R28:240-244`, `R29:232-235`). | Исследование, не production calibration. |

## 7. Точные acceptance criteria smoke

Перед построением Top placement smoke **должен** подтвердить:

1. Каждый выбранный body имеет все category A поля с не-`null`, типизированными, конечными значениями.
2. `ComponentInstance.Id` уникален в package; множество ID до и после export совпадает; новые placement в smoke не создаются.
3. `ComponentSide == Top` и `BodyProjection == Top`; любое другое значение → отказ, без exports/placement Bottom.
4. `ModelOrigin.X/Y` присутствуют в mm; `ModelOrigin.Z` отсутствует и остаётся `null`; подстановка `0` запрещена.
5. `ModelRotationDegRaw == 0`; полный model state конечен; `rx/ry/rz/dz` не выводятся из STEP.
6. `StandoffHeightMm` конечен; `Thickness` берётся только из `BoardSnapshot.Properties.Thickness`; Z surfaces = `±T/2`.
7. Model kind/source проходит все пять гейтов D-022/SW builder; STEP asset существует и совпадает с checksums package.
8. `BodyOrdinal` неотрицателен и однозначен внутри компонента; multi-body partial selection запрещён.
9. Любое category B поле может отсутствовать, но тогда оно фиксируется как `null`/diagnostic и не повышается до category A.
10. Любое category C поле не используется как library identity; при попытке подстановки smoke считается проваленным.
11. Fail-closed gate document↔board ownership должен подтвердить текущий документ; при отказе операция завершается, обход запрещён.
12. Итог фиксируется в smoke JSON: требуемые поля, диагностические коды, package ID, SHA-256 входов/выходов.

## 8. Что контракт не доказывает

- Не доказывает стабильность library GUID или кросс-сессионную ECO identity (V-07 открыт, D-010 `Proposed`).
- Не активирует D-024 и не создаёт `footprintCalibration` в catalog 1.1.0.
- Не закрывает скрытый Z-offset в `GetOrigin()`; Z APIs нет.
- Не закрывает V-16: STA-thread observation из BRIDGE-000033 — runtime fact, не SDK 4.0.1 contract.
- Не заменяет independent alignment verification; это только identity gate.

## 9. Safety verification и не-действия

- Изменён только этот Markdown-отчёт; production code, SDK ledger, ADR, fail-closed gate и package format не тронуты.
- Не использованы DelphiScript, Legacy API, reflection, `dynamic`, undocumented COM/DispId.
- SDK API не запускались из фоновых потоков; CAD-приложения не запускались и не перезапускались.
- Не открывались и не сохранялись пользовательские PCB/CAD-документы; бинарные артефакты в Git не добавлены.
- Bottom и D-024 не активировались; PR #25/#26/#27 не мержились; новый PR не создан.

## 10. Commit и review

- Content commit: `9c1652319241489f271fddf174ec7fe4f3fe6611`; metadata-only SHA update follows.
- Branch: `task/BRIDGE-000035-top-only-smoke`, base `source` @
  `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`.
- PR: none.
