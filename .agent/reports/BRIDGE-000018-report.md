---
id: BRIDGE-000018
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T02:00:00+03:00
updated: 2026-11-26T03:00:00+03:00
base: source
branch: agent/BRIDGE-000018-first-placed-pcb-assembly
commit: d217eae
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/17
related: [BRIDGE-000014, BRIDGE-000015, BRIDGE-000016, BRIDGE-000017]
---

# BRIDGE-000018 — First placed PCB assembly (Top prototype)

## 1. Итог

Реализован первый рабочий pipeline размещения Top/Top Generic Embedded тел
Altium в нативной сборке SOLIDWORKS 2026.

Выполнено:

- Component Model Catalog обновлён до 1.1.0: `BodyProjection`,
  `StandoffHeight`, `OverallHeight`, `ModelStateSource`; формат 1.0.0 читается.
- Altium adapter сохраняет projection/высоты/состояние и отдельный
  `ModelRotationDegRaw`.
- SolidWorks backend строит placement requests из Exchange Package, вызывает
  Core `PlacementTransformBuilder`, конвертирует матрицу в `MathTransform` и
  создаёт occurrences в `.SLDASM`.
- Runtime smoke на реальном MiniPC package: **20 component instances / 8 native
  SLDPRTs**, save/reopen **20/20**, все transforms verified, cleanup `Verified`.
- R3 control package: **U101–U104 / 1 native part**, 4/4 occurrences, residuals 0.
- Bottom, projection mismatch, Native/External, NotAcquired и неполные данные
  получают явный Unsupported; нулевая матрица не подставляется.

Runtime smoke в реальных CAD-хостах выполнен (Altium + SOLIDWORKS). Это не
полная сборка MiniPC: размещены выбранные 20 component instances.

## 2. Архитектура pipeline

```text
Altium BoardSnapshot + per-body STEP export (BRIDGE-000014)
  → Exchange Package (models 1.1.0)
  → ExchangePackageReader
  → SolidWorksImportPlanBuilder
       unique asset → native SLDPRT
  → SolidWorksPlacementRequestBuilder
       component placement + model origin + board frame
       → Core PlacementTransformRequest
  → Core PlacementTransformBuilder (Matrix4x4D)
  → MathTransform ArrayData (R^T row-major, metres)
  → IAssemblyDoc.AddComponent5
  → IComponent2.SetTransformAndSolve3
  → GetTotalTransform readback + residuals
  → SaveAs3 .SLDASM
  → close Bridge-owned docs → reopen → verify count/mapping/transforms
  → cleanup baseline/final snapshots
```

Ключевые решения:

- board-local XY: из `ComponentInstance.Placement.Position` вычитается минимум
  контура платы; Z surfaces — `±BoardSnapshot.Properties.Thickness / 2`;
- model-origin offset: `deltaBoard = Origin - ComponentPosition`,
  `deltaLocal = Rz(-RotationDeg) * deltaBoard`; в Core передаётся именно
  `deltaLocal`, а не board offset;
- `AssetOriginLocalMm = 0`; STEP asset не содержит placement/rotation;
- `ModelStateSource = PlacedBodyEffective`; Core нормализует raw Rz, поэтому
  `ComponentRotationDeg` применяется один раз;
- `ModelRotationDegRaw` обязателен и должен быть 0;
- одинаковый `AssetPath` использует один native `.SLDPRT`, но создаёт отдельный
  occurrence на каждый body reference.

## 3. Изменённые файлы

Core:

- `src/EcadMcadBridge.Core/Model/ComponentModels.cs`
- `src/EcadMcadBridge.Core/Packaging/ComponentModelsJson.cs`
- `src/EcadMcadBridge.Core/Packaging/ComponentModelsValidator.cs`
- `src/EcadMcadBridge.Core/Packaging/ComponentModelsWriteRequest.cs`
- `src/EcadMcadBridge.Core/Packaging/ExchangePackageWriter.cs`
- `src/EcadMcadBridge.Core/Placement/PlacementContracts.cs`
- `src/EcadMcadBridge.Core/Placement/PlacementTransformBuilder.cs`

Altium:

- `src/EcadMcadBridge.Altium/Adapter/AltiumComponentModelReader.cs`
- `src/EcadMcadBridge.Altium/Adapter/AltiumComponentModelRequestBuilder.cs`
- `src/EcadMcadBridge.Altium/Adapter/AltiumModelMetadataMapper.cs`

SOLIDWORKS:

- `src/EcadMcadBridge.SolidWorks/Placement/SolidWorksPlacementPlan.cs`
- `src/EcadMcadBridge.SolidWorks/Placement/SolidWorksPlacementRequestBuilder.cs`
- `src/EcadMcadBridge.SolidWorks/Placement/SolidWorksTransformConverter.cs`
- `src/EcadMcadBridge.SolidWorks/Assemblies/SolidWorksPlacedAssemblyService.cs`
- `tools/EcadMcadBridge.SolidWorks.Smoke/Program.cs`

Tests/docs:

- `tests/EcadMcadBridge.Core.Tests/ComponentModelsPackageTests.cs`
- `tests/EcadMcadBridge.Altium.Tests/*`
- `tests/EcadMcadBridge.SolidWorks.Tests/SolidWorksPlacementTests.cs`
- `.agent/ARCHITECTURE.md`, `PROJECT_CONTEXT.md`, `DEVELOPMENT_RULES.md`,
  `DECISIONS.md`, `SDK_API_LEDGER.md`, `AGENTS.md`, `README.md`.

## 4. Package schema changes

`ComponentModelCatalog.SchemaVersionValue = 1.1.0`.

Новые поля `ComponentBodyEntry`:

| Поле | JSON | Значения |
|---|---|---|
| `BodyProjection` | `bodyProjection` | `top`, `bottom`, `unknown` |
| `ModelStateSource` | `modelStateSource` | `source-footprint`, `placed-body-effective`, `unknown` |
| `StandoffHeight` | `standoffHeight: { iu, mm }` | пара IU/мм или `null` |
| `OverallHeight` | `overallHeight: { iu, mm }` | пара IU/мм или `null` |

Совместимость:

- Reader принимает `1.0.0` и `1.1.0`; отсутствующие новые поля → `Unknown`/`null`.
- Writer всегда пишет 1.1.0.
- Validator проверяет enum, finite values, пары IU/мм и
  `LengthValue.HasConsistentMm`.
- `BoardSnapshot` и manifest schema не менялись.

## 5. Placement mapping и формулы

Board frame:

```text
P_board = ComponentPosition_altium - (minX(outline), minY(outline))
T = BoardSnapshot.Properties.Thickness.Mm
TopSurfaceZ = +T/2
BottomSurfaceZ = -T/2
```

Model-origin:

```text
deltaBoard = ModelOrigin.XY - ComponentPosition.XY
deltaLocal = Rz(-ComponentRotationDeg) * deltaBoard
```

Top placement (Core, BRIDGE-000017):

```text
R_state = Rz(rz) * Ry(ry) * Rx(rx)
M_base = T(P_board + deltaLocal, TopSurfaceZ + Standoff)
         * Tz(dz) * R_state * T(-AssetOrigin)
M = T(P_board) * Rz(ComponentRotationDeg) * T(-P_board) * M_base
```

Для `PlacedBodyEffective` Core вычитает `ComponentRotationDeg` из raw Rz
(нормализация), поэтому поворот применяется один раз.

SOLIDWORKS conversion:

```text
ArrayData[0..8]  = row-major R^T
ArrayData[9..11] = translation mm / 1000
ArrayData[12]    = 1
ArrayData[13..15] = 0
```

Проверки: finite, rigid inverse, determinant +1, no scale/reflection.
Readback: translation ≤ 0.001 mm, rotation basis ≤ 1e-8.

Capability matrix:

| Mode | Status |
|---|---|
| Top/Top Generic Embedded, STEP AP214, SdkExport, PlacedBodyEffective | Supported |
| Top compound State + Rotation | Supported |
| Bottom, projection mismatch, Native, External, NotAcquired, Unknown | Unsupported |
| Missing thickness/position/origin/state/standoff/modelRotation | Unsupported |
| `ModelRotationDegRaw != 0` | Unsupported |
| Missing footprint calibration | Unsupported |

## 6. Ограничения и неподдерживаемые случаи

- Bottom и Bottom compound не размещаются; нужна per-footprint calibration.
- BodyProjection mismatch не размещается.
- Native/Extruded, External/Vault, NotAcquired и Unknown не размещаются.
- Board solid/outline как MCAD geometry не создаётся.
- `AssetOriginLocalMm = 0` предполагается для текущего STEP AP214 pipeline;
  asset с другим контрактом не размещается без отдельной проверки.
- Board thickness берётся из Altium BoardSnapshot; расхождение с STEP board
  solid (R0–R4, 0.02 мм) не используется и не «исправляется» в этом prototype.
- Отсутствие достоверного `ContourRole.Outer` или некорректная геометрия
  внешнего контура приводят к invalid placement plan с диагностикой
  `SW-PLC-021`. Fallback к BoardOrigin `(0,0)` отсутствует.
- Multi-body components поддерживаются запросом на каждый body; FIX-001
  подтвердил runtime на M501 с двумя разными Generic Embedded body (2/2 occurrences).
- Полноценный SOLIDWORKS Add-in, Registry, installer и UI не реализованы.

## 7. Runtime smoke

Среда:

| Параметр | Значение |
|---|---|
| Altium Designer | 26.12.2.9 |
| Altium add-in | `0.4.0` (временно собранный из BRIDGE-000018, затем восстановлен установленный) |
| SOLIDWORKS | 2026 SP3.2, revision `34.3.2`, build `sw2026_SP03/d260702.008/` |
| SOLIDWORKS interop | 34.3.2.3 |
| Assembly template | `C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gost-assy.asmdot` |

### 7.1 MiniPC package (20 instances)

| Параметр | Значение |
|---|---|
| Package ID | `e3c7ca93-be53-402c-a109-141f04a32bf0` |
| Package files/bytes | 159 / 402 394 613 |
| `checksums.sha256` SHA-256 | `1B68EBC3CD60735B95B3461093AD01A72DE76A983BF47DE5253C5633CEE70289` |
| `manifest.json` SHA-256 | `464246342AA075E1895F16597324F8FAAA7CEA8C35D5B105142308DEC25F0A02` |
| `board.snapshot.json` SHA-256 | `0D598E6BBF29D39BFA00F75EDA70351163FE55D8460335F4045B103BAB4983E8` |
| `models/models.json` SHA-256 | `5385674B7C11A4B862314389BC47C5096ECC6DEF7C9CE644AB9FE0C303EB8769` |
| Components / bodies | 1046 / 1055 |
| STEP assets | 1055 |
| Eligible Top/Top bodies | 363 |
| Unsupported body warnings | 692 |
| Selected designators | C1, C16, C17, C18, C19, C93, C97, C102, C104, C105, C108, C109, C110, C111, C112, C113, C114, C115, C116, C117 |
| Selected unique assets | 8 |
| Imported native SLDPRT | 8 |
| Selected body references (shared assets) | 157 |
| Assembly occurrences | 20 |
| Reopened occurrences | 20 |
| Transforms verified | true |
| Cleanup | `Verified` |
| Baseline preserved | true |

Assembly: `smoke-placed-json/assembly/smoke-placed-assembly.SLDASM`,
204 011 bytes, SHA-256 `92065E320A47F7348B28A8BA97A105E3AFA05F41140B58B9D51F70AEB365F9DF`.

Smoke JSON: SHA-256 `0F9FAFDDB2DB6BA8CAC222A9604ABF6C0CBEC2BC45F3FFAF48DB2B515F0292B3`.

### 7.2 R3 control package (U101–U104)

| Параметр | Значение |
|---|---|
| Package ID | `39c0d4a0-935d-491d-bd38-61c6b4152e6c` |
| `checksums.sha256` SHA-256 | `EF5A5F82D97CF64F0FA9001D177D67EE6EC6C7788E2627F885AFF52A6FC421E7` |
| `manifest.json` SHA-256 | `20971DBB545EFFE2F39192B5D6B1E3B72BC06DAC664B3D98E56D05536BD17C3F` |
| `models/models.json` SHA-256 | `FF1BA5DFB1613247D6938055298838F3B3CA445746D8F3B92E9FD5CB624BB56D` |
| Selected designators | U101, U102, U103, U104 |
| Unique assets / native parts | 1 / 1 |
| 4 occurrences from 1 native part | yes |
| Reopened occurrences | 4 |
| Transforms verified | true |
| Cleanup | `Verified` |

Assembly: `smoke-r3-json/assembly/smoke-placed-assembly.SLDASM`,
66 747 bytes, SHA-256 `4BD3AC062743522E194411CFC3EEF1569726C8E644BD04BD8F08D1887DC2E413`.

Smoke JSON: SHA-256 `0E6664C13289AC6BC3F416E4C1403DE4D051F909B89262F19D73EE45CBB8B526`.

## 8. Численные transform residuals

MiniPC 20 occurrences:

- translation residual max `2.8421709430404E-14 mm` (C116), остальные 0;
- rotation basis residual max `0`;
- determinant `1` для всех;
- save/reopen residuals также 0.

R3 U101–U104 requested board-local transforms:

| Component | Tx, mm | Ty, mm | Tz, mm | Expected R3 XY | Residual |
|---|---:|---:|---:|---|---:|
| U101 | 23.085659 | 54.540450 | 1.004001 | (23.085654, 54.540448) | 0 |
| U102 | 65.659551 | 48.485659 | 1.004001 | (65.659552, 48.485654) | 0 |
| U103 | 111.714344 | 51.059552 | 1.004001 | (111.714346, 51.059552) | 0 |
| U104 | 149.140448 | 57.114343 | 1.004001 | (149.140448, 57.114346) | 0 |

`Tz = 1.004001 mm` is `Thickness/2` for `2.00800208 mm`; R3 STEP report used
board-local top surface Z=0, so the standalone assembly uses the requested
mid-plane board frame. XY matches R3 within `1e-5 mm`; readback residuals are 0.

## 9. Save/reopen и lifecycle

- Assembly saved via `SaveAs3`; target existed check prevented overwrites.
- Created documents were closed only when tracked as Bridge-owned.
- Reopen verified 20/20 and 4/4 occurrences; native part mapping and transforms
  were re-read and matched.
- Baseline snapshot `Available/0`, final `Available/0`,
  `createdDocumentsRemaining=0`, `baselinePreserved=true`,
  cleanup `Verified` for import and placed assembly.
- User documents were not closed; no Registry/`.ins`/extension registration
  changes were made. The temporarily updated installed Altium DLL was restored
  to the clean `E376B34E…717E` build after export.

## 10. Unit tests и build

- `dotnet build -c Release` — 0 warnings / 0 errors.
- `dotnet test -c Release --no-build`:
  - Core `188/188`
  - Altium `151/151`
  - SolidWorks `64/64`
  - total `403/403` (after FIX-001).
- `git diff --check` — OK.
- New coverage includes catalog 1.0.0/1.1.0 compatibility, legacy missing
  fields, invalid enums/LengthValue, Altium projection/heights/state source,
  package→placement requests, board thickness/origin validation, R3
  rotations, Top compound state, PlacedBodyEffective normalization, unsupported
  modes, matrix conversion, rigidity/determinant and residual checks.

Runtime tests actually performed: Altium 26.12.2.9 package export; SOLIDWORKS
2026 SP3.2 import, placement, save, close, reopen, transform readback. No full
MiniPC assembly and no Bottom/Native/External runtime.

## 11. Commit и PR

- Branch: `agent/BRIDGE-000018-first-placed-pcb-assembly`
- Base: `source` `9c5a98f05a175ba612349dda3675f6379f730981`
- Content commits: `d3064c2` (`feat(bridge-000018): implement first placed Top assembly`);
  `d217eae` (`fix(bridge-000018): correct board bounds and placement completeness`).
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/17
- Merge: not performed.

## 12. Остаточные риски и следующий этап

- Board-local origin uses the analytic Outer-contour minimum. Missing or invalid
  Outer contour now makes the placement plan invalid; there is no `(0,0)`
  fallback (FIX-001).
- Board thickness source remains Altium `BoardProperties`; STEP board solid
  mismatch (0.02 mm) is unresolved and board solid is not in the assembly.
- Only Top/Top Generic Embedded is proven; Bottom calibration, projection
  mismatch and Native/External remain next-stage work. Multi-body runtime is
  confirmed by FIX-001 (one controlled component, two bodies).
- The assembly contains occurrences only, not board copper/pads/outline solids.
- Large package performance (300 MB snapshot, 1055 STEP assets) needs streaming
  or subset package strategy before full-board assembly.
- Full MiniPC assembly, user UI, Add-in registration and installer remain out of
  scope.

## 13. FIX-001 — BoardOrigin bounds and placement completeness

### 13.1. Причины дефектов

- **FIX-A:** `GetBoardOrigin()` учитывал все контуры (включая cutout) и
  `ArcSegment.Center` как граничную точку. Центр окружности не обязан лежать на
  дуге; для короткой дуги он занижал bounds, а для cutout мог сдвинуть origin.
  При отсутствии/повреждении Outer contour код возвращал `(0,0)`.
- **FIX-B:** explicit selection проверял только уже построенные requests. Если у
  компонента два body и только один поддержан, второй молча пропускался, а
  `SW-PLC-018` не переводил план в invalid. Проверки catalog/import/STEP для
  каждого body отсутствовали.

### 13.2. FIX-A — аналитический Outer bounds

Добавлен независимый `BoardOutlineBounds`:

- используются только контуры `ContourRole.Outer`;
- `LineSegment` — Start/End;
- `ArcSegment` — Start/End и кардинальные точки 0/90/180/270, только если они
  попадают в фактический CW/CCW sweep;
- `Center` никогда не добавляется как граница;
- внутренние/cutout contours игнорируются;
- отсутствие Outer contour, некорректный radius/center/sweep/start/end или
  несовпадение точки с радиусом — ошибка `SW-PLC-021`;
- fallback `(0,0)` удалён.

Новые тесты (`BoardOutlineBoundsTests`): line rectangle, CCW/CW arcs с центром
за фактической границей, короткая дуга, cardinal внутри/вне sweep, outer+cutout,
missing Outer, invalid radius/start. Проверяются точные min/max.

### 13.3. FIX-B — полнота Designators

План теперь хранит полный набор ожидаемых body entries из catalog и проверяет
snapshot/catalog/import reference/STEP asset для каждого body.

Для явного `--designators`:

- отсутствие Designator в snapshot — `SW-PLC-017`;
- отсутствие ComponentModelCatalog entry — `SW-PLC-022`;
- отсутствие body entries — `SW-PLC-023`;
- unsupported/missing body/asset/import — `SW-PLC-024` с `Designator/BodyOrdinal`;
- при неполном multi-body компоненте не добавляется ни один его request;
- `IsValid=false` и smoke `success=false`.

Для автоматического `--max-components`:

- выбираются только компоненты, у которых **все** body entries поддержаны;
- частично поддержанный multi-body компонент исключается целиком;
- лимит считается по component instances, а не по body requests.

Новые тесты: partial multi-body explicit, один unsupported из двух, два
supported body → 2 requests, body без STEP, body без import reference,
автоматическое исключение частичного multi-body. Сравнивается полный набор
`ComponentId + BodyOrdinal`.

### 13.4. Runtime smoke после FIX-001

| Smoke | Package | Результат | JSON SHA-256 | Assembly SHA-256 |
|---|---|---|---|---|
| MiniPC 20 | `e3c7ca93-be53-402c-a109-141f04a32bf0` | 20/20 reopened, verified, cleanup Verified | `7FD92BF39B2FE37E3A0FB652AC8D51DF6FA1B4571A66DC74E1BAFEF4142CA498` | `C9301050A78783C0D68D1414DA2B49BC126AFA1270102E6309805F6A215D81BE` |
| R3 U101–U104 | `39c0d4a0-935d-491d-bd38-61c6b4152e6c` | 4/4 reopened, verified, cleanup Verified | `FA7E3A29F0DA049AB96858B5FE7C692970CDBF061847D7F905306E98AED4EA6F` | `1B49264C83FAFD752C7675EF77A0C66011CBA2DDB92CB1E55E23E8CB7F2C854F` |
| Multi-body M501 | `3f68dfe4-2dcd-4bdc-90b3-30dae9e84e88` | 2/2 reopened, verified, cleanup Verified | `C6BE490309D8D06FB29A8D12D46252490C1F77B964B8BA4C136A579AE4D20335` | `7222213F0AF4FA3491DA1BEEF483F847093052E897CAB3F22F23A2D03D83D70D` |

Общий board origin после аналитического Outer bounds:
`(212.59999898, 167.19999834) mm`; R3 XY transforms не изменились в пределах
`1e-5 mm`. MiniPC 20/20 и R3 4/4 transforms verified, residual 0.

Multi-body fixture:

- scratch board `multi-body.PcbDoc` SHA-256
  `22BF91C0DC232D56F91E3E8004538DE36336BBE47CF069B56DE50CBAA485A3F6`;
- scratch PcbLib `multi-body.PcbLib` SHA-256
  `BE74368D90236DF7C1B00E0B3A33E7A0C781C97F7569F8E9F66DDD881356429C`,
  footprint `BRIDGE_ROT_3D`, two primitives/two bodies;
- M501 has two Generic Embedded Top bodies, different data sizes/STEP assets
  (`0be67952…`, `e5f44d66…`), both `PlacedBodyEffective`;
- package 168 files / 401 365 134 bytes; checksums SHA
  `9B5F4C70C8E2ABFEC55C353BC1E257145F1EEF8DCBBCDE156C058B94A6FC6DAA`,
  manifest SHA `95736D98191C4F7D395003CF5EA083FF193412FC6EF9633ECA4319BE60603CB5`,
  models SHA `1F6394B43DBA4EED41D223467BB04B04818DBC4680C8568B6E5A7208E22D57E4`;
- 2 unique STEP assets → 2 native SLDPRT → 2 occurrences; save/reopen 2/2,
  translation/rotation residuals 0, determinant 1; baseline preserved, cleanup
  Verified.

Временный Altium SDK handler для scratch component удалён; установленный
`EcadMcadBridge.dll` восстановлен (`E376B34E…717E`), `.ins`/Registry не менялись.

### 13.5. Изменённые файлы FIX-001

- `src/EcadMcadBridge.SolidWorks/Placement/BoardOutlineBounds.cs`
- `src/EcadMcadBridge.SolidWorks/Placement/SolidWorksPlacementRequestBuilder.cs`
- `tests/EcadMcadBridge.SolidWorks.Tests/BoardOutlineBoundsTests.cs`
- `tests/EcadMcadBridge.SolidWorks.Tests/SolidWorksPlacementTests.cs`
- `.agent/reports/BRIDGE-000018-report.md`
- `.agent/ARCHITECTURE.md`, `.agent/DEVELOPMENT_RULES.md`
- FIX-001 content commit: `d217eae`.

### 13.6. Проверки

- `dotnet build -c Release` — 0 warnings / 0 errors.
- `dotnet test -c Release` — Core `188/188`, Altium `151/151`,
  SolidWorks `64/64`, всего `403/403`.
- `git diff --check` — OK.
- Runtime: Altium 26.12.2.9, SOLIDWORKS 2026 SP3.2 (34.3.2.3).

### 13.7. Остаточные ограничения

- FIX-A bounds проверены unit-тестами (включая CW/CCW arc cases); реальные
  MiniPC/R3 outlines line-only, поэтому их результаты не изменились.
- Multi-body runtime uses a controlled scratch PcbLib with two different embedded
  STEP models; it is not a user board.
- Bottom/Native/External, board solid and full MiniPC assembly remain out of
  scope.
- `SW-PLC-024` is a deliberate fatal diagnostic for incomplete explicit
  selection; automatic inventory may still contain unsupported bodies as
  warnings.
