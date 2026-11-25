---
id: BRIDGE-000016
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T20:30:00+03:00
updated: 2026-11-25T23:10:00+03:00
base: source
branch: task/BRIDGE-000016-coordinate-contract
commit: ab76100
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/15
related:
  - BRIDGE-000009
  - BRIDGE-000011
  - BRIDGE-000013
  - BRIDGE-000014
  - BRIDGE-000015
---

# BRIDGE-000016-R0/R1/R2/R3 — Altium → SOLIDWORKS coordinate calibration

## 1. Итог

R0/R1 подтвердили математическую модель для `STEP asset → 3D body → Altium component → PCB board → SOLIDWORKS assembly` в объёме, достаточном для Top-only следующего этапа, и явно отделили неподтверждённые части.

Подтверждено:

- STEP, полученный через `IPCB_ComponentBody.SaveModelToFile` / `IPCB_Model.ExportToStep`, содержит локальную B-Rep модели и **не содержит** component placement, component rotation, model `GetState` rotation, `dz` или Bottom mirror;
- Generic STEP control имеет локальный origin в нижнем углу; Native Extruded — центрированный bbox;
- `BodyProjection` — отдельное свойство тела (`TBoardSide`: Top=0, Bottom=1), не замена стороны компонента;
- `StandoffHeight`/`OverallHeight` — IU от поверхности платы наружу в сторону `BodyProjection`; для Generic control они совпали с Z-границами модели после state-преобразования;
- combined model rotation подтверждена как `Rz(rz) · Ry(ry) · Rx(rx)` для столбцовых векторов: C524/C516 дали expected/actual Z-границы с ошибкой ≤ 0.000003 mm;
- `dz` — IU по Z платы; `+393700 IU` дал `+1.000 mm`, `-393700 IU` — `-1.000 mm`;
- SOLIDWORKS `CreateTransform` принимает 16 значений: 3×3 block, translation в индексах 9–11, scale в 12; translation в **метрах**;
- `Component2.Transform2` и `SetTransformAndSolve3` приняли точный transform; `GetTotalTransform` и чтение `Transform2` вернули те же значения;
- `ComposeTransform` даёт тот же layout и может использоваться как адаптер basis vectors → `MathTransform`.

Не подтверждено и помечено `Unknown`:

- 3D feature-level residual для component rotation на **Bottom** и для случая `BodyProjection != Component.Side`; для Top library-relative тела это подтверждено в R3-FIX-003;
- Bottom с ненулевым component rotation: full board STEP не содержит component rotation; `Rx(180°)` подтверждён только для rot0, отражение не требуется;
- независимый случай `BodyProjection != Component.Side` (в фикстуре все тела совпали со стороной компонента);
- `IPCB_Model.Rotation` как отдельное свойство: на фикстуре он был 0 и не совпал с `GetState` rotZ;
- Z-составляющая `GetOrigin`; board origin подтверждён как Altium absolute origin, но отдельного поля в Core snapshot нет;
- `SnapCount`/snap points: API подтверждён документацией, значения на фикстуре не сняты;
- полный Altium board STEP golden reference: получен в R2 (без component rotation) и R3 (с rotation-bearing Top U101–U104); сравнение численное, по матрицам и landmark-точкам, без выдачи визуального совпадения за численное.

R1 исправил математическую композицию:

- board-space `delta_B = GetOrigin.XY - Component.Position.XY` переводится в локальный frame как `delta_D = R_BD^T * delta_B`, где `R_BD = R_BC * R_CD`; использовать `delta_B` как локальную трансляцию нельзя;
- `dz` применяется после `R_state`: `T_DM = T(delta_D) * Tz(dz) * R_state`, а не `R_state * Tz(dz)`;
- `Rz·Ry·Rx` подтверждён на уровне bounding-box/outline: observed rectangle 12.797273 × 7.562180 mm, long side 60.000°, predicted `Rz·Ry·Rx` gives 12.797272 × 7.562178 mm and the same orientation; full board STEP подтвердил state matrix для C524/C516;
- component `RotationDeg` sign подтверждён по pad landmark points: positive = CCW на board;
- model-origin offset проверен на C62 при component rotation 90°: absolute `GetOrigin` совпал с `T_BC·T_CD·T_DM` только через `delta_D`, ошибка альтернативы 9.497 mm;
- board origin: Altium absolute coords в Core; полный STEP использует board-local frame `T_BS = T(-212.6,-167.2,-T/2)`; C366 совпал с ошибкой ~0.000006 mm;
- SOLIDWORKS `MathTransform` повторно проверен на полной матрице (rotation + translation + basis), включая non-zero component rotation.

R2 получил полный board STEP и подтвердил board frame, `GetOrigin`, `R_state`, `dz`, Bottom `Rx(180°)` для rot0 и C366 feature-level placement. Однако Altium full board STEP **не содержит component RotationDeg** для test bodies: C62/C63/C59/C61/C524/C516/C573/C567/C571 имеют identity/state-only transforms. Поэтому component-rotation 3D placement остаётся **blocked**; R2 не закрывает full Top placement sign-off.

R3-FIX-001 completed native fixture creation: `BRIDGE_ROT_3D` footprint exists with exactly one library-owned Generic/Embedded `control-generic` body; PcbLib SHA is `FECA6F37…`. R3-FIX-003 used the SDK to create U101–U104 with correct Designators, one owned Generic body each, saved/reopened the PCB, exported the full STEP, and confirmed Top component rotation in SOLIDWORKS with matrix and feature residuals ≤ 0.000014 mm. Top Component RotationDeg is **confirmed**; Bottom remains blocked.

R2/R3 не реализуют production placement, не меняют Core schema и не меняют PR #13/#14. Bottom с rotation и `BodyProjection != Component.Side` остаются blockers; Top component rotation закрыт.

## 2. Окружение и версии

| Параметр | Значение |
|---|---|
| Altium Designer | 26.12.2.9 |
| Altium SDK | 4.0.1 |
| SOLIDWORKS | 2026 SP3.2, `34.3.2.0003` |
| SOLIDWORKS interop | 34.3.2.3 |
| .NET SDK | 10 |
| Core | `net10.0` |
| Base | `source`, `5da64a3fa70f3484ee652947f8c2c934b06361c9` |

PR #13/#14 не изменялись. Для локальных API-проб использовались отдельные worktree их веток; результаты проб не коммитились в них.

## 3. Test fixture

Использован контролируемый fixture, созданный для `BRIDGE-000011` и повторно проверенный в R0:

| Параметр | Значение |
|---|---|
| PCB | `%TEMP%\EcadMcadBridge\BRIDGE-000011\test-matrix.PcbDoc` |
| SHA-256 PCB | `28908F70769C6ABEDE125C1B3F3A132520F2C1157C5A897699320FA306D5E5FD` |
| Board thickness | `790552 IU` = `2.00800208 mm` |
| Generic STEP | `control-generic.step` |
| SHA-256 Generic STEP | `E5F44D66D7124349C5B86934288E0BF4AC6CF80352B12C9154F3127D435477D4` |
| Generic solid | 12 × 7 × 3 mm, origin in lower corner |
| Native model | L-profile 12 × 7 × 3 mm, centered bbox |
| Control instances | 16: Generic/Native × Top/Bottom × 0/90/180/270 + state variants |

Fixture geometry: asymmetric base with a 2 × 3 × 2 mm protrusion and a Ø1 mm through hole at `(2,2)`. Recommended R0 size was about 20 × 10 × 4 mm; the existing controlled fixture is 12 × 7 × 3 mm. It remains asymmetric and dimensionally known, but size independence is **not** claimed.

User PCBs не изменялись. CAD-бинарники не добавлены в Git.

## 4. Исходные численные параметры

### 4.1. Board

- `sourceUnits = "altium-iu"`, `units = "mm"`.
- `1 IU = 0.00000254 mm`.
- `BoardProperties.Thickness = 790552 IU = 2.00800208 mm`.
- Board mid-plane в предлагаемом контракте: `Z = 0`; top surface `+1.00400104 mm`, bottom surface `-1.00400104 mm`.
- Board origin: Altium coordinate origin. В `BoardSnapshot` отдельного поля `boardOrigin` нет; это зафиксировано как `Unknown` для будущего schema delta.

### 4.2. Component samples

| UID | Ref | Side | Component rot | Projection | Standoff, mm | Overall, mm | Body bbox, mm | Raw state (Rx,Ry,Rz,dz) |
|---|---|---|---:|---|---:|---:|---|---|
| `CSQWQOOA` | C366 | Top | 0 | Top (0) | 0 | 3.000 | 12 × 7 × 3, origin lower corner | 0,0,0,0 |
| `FFPUPAKY` | C63 | Top | 90 | Top (0) | 0 | 3.000 | 12 × 7 × 3 | 0,0,360,0 |
| `EQHFYMFD` | C572 | Bottom | 0 | Bottom (1) | 0 | 3.000 | 12 × 7 × 3 | 0,0,0,0 |
| `LEDWRKXO` | C543 | Top | 0 | Top (0) | 0 | 7.000 | 12 × 3 | 90,0,0,0 |
| `TKBKODKJ` | C522 | Top | 0 | Top (0) | -12.000 | 0 | 3 × 7 | 0,90,0,0 |
| `RAAHGPJU` | C523 | Top | 0 | Top (0) | 0 | 3.000 | 7 × 12 | 0,0,90,0 |
| `QIJVOLYO` | C524 | Top | 180 | Top (0) | -7.485281 | 5.311988 | ≈12.948 × 14.864 | 30,45,60,+1 mm |
| `NDXGLJXI` | C516 | Top | 180 | Top (0) | -9.485277 | 3.311992 | ≈12.948 × 14.864 | 30,45,60,-1 mm |
| `SNLMUCLA` | C563 | Top | 0 | Top (0) | 0 | 3.000 | 12 × 7 × 3, centered | 0,0,0,0 |
| `UTNOUUSL` | C570 | Bottom | 0 | Bottom (1) | 0 | 3.000 | 12 × 7 × 3, centered | 0,0,0,0 |

`GetOrigin` для Generic control совпал с component X/Y в абсолютных координатах платы; для Native Extruded — с центром bbox (тоже абсолютные координаты). Поэтому в transform используется `(x_m - x_c, y_m - y_c)`, иначе component XY учитывается дважды. `ComponentInstance.Height` не использовался как высота тела: например, C75 имеет component height 10.5 mm при body overall 4.0 mm.

## 5. Измеренные свойства API

| API | Результат |
|---|---|
| `IPCB_ComponentBody.GetBodyProjection()` | `TBoardSide.eBoardSide_Top=0` для Top bodies; `eBoardSide_Bottom=1` для Bottom bodies |
| `GetStandoffHeight()` | IU; для state 0 Top/Bottom — 0; для Rx90 — 0; Ry90 — -12.0 mm; combined — -7.485 / -9.485 mm |
| `GetOverallHeight()` | IU; для state 0 — 3.0 mm; Rx90 — 7.0 mm; Ry90 — 0; combined — 5.312 / 3.312 mm |
| `GetState_SnapCount()` / `GetState_SnapPoint(i)` | API документирован; значения fixture не сняты — **Unknown** |
| `IPCB_Model.GetState(out rx,out ry,out rz,out dz)` | degrees, degrees, degrees, IU |
| `IPCB_Model.GetRotation()` | на всех проверенных моделях 0; не равен state rotZ — **не использовать как 3D orientation** |
| `IPCB_Model.GetOrigin()` | X/Y в IU; Z отсутствует |
| `IPCB_ComponentBody.GetState_ModelHasChanged()` | не использовался для transform |
| `IPCB_Component.GetState_FlippedOnLayer()` | false на Bottom; не использовать как источник стороны |

`BodyProjection` документирован как `TBoardSide` и не является производным от component layer. В фикстуре, однако, все тела имели projection, совпадающий со стороной компонента; независимый mismatch-тест не выполнен — **Unknown**.

## 6. Однофакторные эксперименты

| Фактор | Изменение | Наблюдение | Статус |
|---|---|---|---|
| Origin X/Y | Generic vs Native fixture | Generic origin = component XY; Native origin = center bbox | Partial |
| `IPCB_Model.Rotation` | отдельное свойство | 0 на всех моделях, не совпало с state rotZ | Unknown |
| `GetState` Rx | 90° | bbox 12 × 3; Z bounds 0…7 mm | Pass для величины/оси |
| `GetState` Ry | 90° | bbox 3 × 7; Z bounds -12…0 mm | Pass для величины/оси |
| `GetState` Rz | 90° | bbox 7 × 12; Z bounds 0…3 mm | Pass для величины/оси |
| `dz` | ±393700 IU | Z bounds сдвинулись на ±1.000 mm | Pass |
| `BodyProjection` | Top/Bottom | 0/1; STEP не отражает side | Partial (нет mismatch) |
| `StandoffHeight` | state 0, Rx, Ry, combined | совпал с Z min после rotation+dz | Pass для fixture |
| `OverallHeight` | state 0, Rx, Ry, combined | совпал с Z max после rotation+dz | Pass для fixture |
| Component rotation | 0/90/180/270 | не входит в STEP/body bbox; body projection остаётся локальным; pad landmarks подтвердили positive=CCW | Sign **Confirmed (2D pads)** |
| Bottom | Top vs Bottom | STEP и body projection не зеркалированы; `GetFlippedOnLayer=false` | Transform **Unknown** |

Одиночные 90° меняют extents body projection; sign компонента подтверждён отдельно по pad landmark points, не по body bbox.

## 7. Combined Euler rotation and independent XY outline

Для Generic 12×7×3, state `(30,45,60)` и `dz=±1 mm` проверены два независимых экземпляра C524/C516.

Формула проверки:

```text
R_state = Rz(rz) · Ry(ry) · Rx(rx)      (column vectors, right-handed, active)
z_min = min(R_state · p + (0,0,dz))
z_max = max(R_state · p + (0,0,dz))
```

| Instance | Expected z_min, mm | Actual Standoff, mm | Error, mm | Expected z_max, mm | Actual Overall, mm | Error, mm | Tolerance | Pass |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| C524, dz=+1 | -7.485281 | -7.485281 | 0.000000 | 5.311991 | 5.311988 | 0.000003 | 0.001 | Yes |
| C516, dz=-1 | -9.485281 | -9.485277 | 0.000004 | 3.311991 | 3.311992 | 0.000001 | 0.001 | Yes |

Matrix for `(30,45,60)`:

```text
R = [ 0.353553  -0.573223   0.739199 ]
    [ 0.612372   0.739199   0.280330 ]
    [-0.707107   0.353553   0.612372 ]
```

Independent XY outline check for C524 (BodyProjection polygon, relative to origin):

| Parameter | Observed | Predicted `Rz·Ry·Rx` | Error | Tolerance | Pass |
|---|---:|---:|---:|---:|---|
| Long rectangle side, mm | 12.797273 | 12.797272 | 0.000001 | 0.001 | Yes |
| Short rectangle side, mm | 7.562180 | 7.562178 | 0.000002 | 0.001 | Yes |
| Long-side direction, deg | 60.000 | 60.000 | 0.000 | 0.01 | Yes |
| Corner p1 relative, mm | (7.697673, 10.332763) | (7.697672, 10.332763) | 0.000001 | 0.001 | Yes |

The outline rectangle is formed from `Rx/Ry` extents and then rotated by `Rz`; the Z bounds are formed from the full `Rz·Ry·Rx`. Together these independently confirm the full Euler order for the non-zero combined case. Candidate orders with different placement of `Rz` or swapped `Rx/Ry` produce side-length errors >2 mm; they are rejected.

Full 3D feature-level residuals for all six candidate orders require a rotation-bearing full board STEP reference. R2 obtained the full board STEP, but it does not encode component `RotationDeg` for the fixture bodies; therefore the candidate table is still not claimed for component-rotation cases. Model `R_state` itself matches the full board transform for C524/C516.

Component `Rz` sign is confirmed separately by pad landmarks (section 7.1). Full board STEP confirms Bottom `Rx(180°)` for rot0 (C572); rotation-bearing Bottom remains blocked.

### 7.1. Component RotationDeg sign

Fixture components C572 (rot 0), C573 (rot +90), C567 (rot -180), C571 (rot -90), same `CAPC1005X55X10LL05` footprint. Pad `QUPKSVYF` relative to component placement:

| Ref | RotationDeg | Pad relative (mm) | Expected CCW | Error | Pass |
|---|---:|---|---|---:|---|
| C572 | 0 | (-0.500, 0.000) | (-0.500, 0.000) | 0 | Yes |
| C573 | +90 | (0.000, -0.500) | (0.000, -0.500) | 0 | Yes |
| C567 | -180 | (+0.500, 0.000) | (+0.500, 0.000) | 0 | Yes |
| C571 | -90 | (0.000, +0.500) | (0.000, +0.500) | 0 | Yes |

This confirms positive `RotationDeg` = CCW in the board frame for pad placement. It does not by itself prove the 3D body orientation, but the body is not baked with component rotation in STEP, so the same `Rz(θ_c)` is the only component-rotation term in the contract.

## 8. STEP export semantics

Сравнение `SaveModelToFile` / `ExportToStep` с исходной геометрией и импортом в SOLIDWORKS:

| File / instance | Volume, mm³ | BBOX in SOLIDWORKS, m | Conclusion |
|---|---:|---|---|
| C366 Generic `model.step` | 95.2146018 | 0…0.012 × 0…0.007 × 0…0.003 | local geometry, no placement |
| C366 Generic `body.step` | 95.2146018 | 0…0.012 × 0…0.007 × 0…0.003 | same B-Rep |
| C572 Bottom `model.step` | 95.2146018 | 0…0.012 × 0…0.007 × 0…0.003 | no Bottom mirror |
| C63 rot90 `model.step` | 95.2146018 | 0…0.012 × 0…0.007 × 0…0.003 | no component rotation |
| C524 combined `model.step` | 95.2146018 | 0…0.012 × 0…0.007 × 0…0.003 | no state rotation |
| C563 Native `model.step` | 146.999118 | -0.006…0.006 × -0.0035…0.0035 × 0…0.003 | local, centered |
| C570 Bottom Native `model.step` | 146.999118 | -0.006…0.006 × -0.0035…0.0035 × 0…0.003 | no Bottom mirror |

Вывод: STEP asset является **локальной геометрией в model/asset frame**. В нём нет component placement, component rotation, model `GetState`, `dz` или Bottom reflection. Повторно применять эти преобразования к геометрии нельзя; их нужно применять к occurrence/transform в SOLIDWORKS.

Generic `body.step` совпал с `control-generic.step` по SHA/размеру в контролируемом эксперименте; `model.step` отличается из-за обёртки/header, но геометрия совпала по объёму и bbox.

## 9. SOLIDWORKS transform layout и units

Проверено на SOLIDWORKS 2026 SP3.2 через `ISldWorks.GetMathUtility`, `IMathUtility.CreateTransform`, `ComposeTransform`, `MathPoint.MultiplyTransform`, `Component2.Transform2`, `SetTransformAndSolve3`, `GetTotalTransform`.

### 9.1. 16-element layout

For `MathTransform.ArrayData` / `CreateTransform`:

```text
indices 0..8   : 3x3 matrix block, row-major
indices 9..11  : translation tx, ty, tz
index 12       : scale (1.0 for rigid transform)
indices 13..15 : 0
```

Translation is in meters. Example: `(0.01, 0.02, 0.03)` maps origin to `(0.01, 0.02, 0.03)` and x unit to `(1.01, 0.02, 0.03)`.

### 9.2. Rotation block convention

A `CreateTransform` array with 3×3 block

```text
[ 0 -1  0 ]
[ 1  0  0 ]
[ 0  0  1 ]
```

maps `x=(1,0,0)` to `(0,-1,0)` and `y=(0,1,0)` to `(1,0,0)`. Therefore the block is the **transpose** of the usual column-vector rotation matrix `R`:

```text
ArrayData[0..8] = R^T row-major
```

For CCW `Rz(+90°)`, use the transposed block:

```text
[ 0  1  0 ]
[-1  0  0 ]
[ 0  0  1 ]
```

`ComposeTransform(XVec, YVec, ZVec, TransVec, Scale)` produced the same layout and is the preferred adapter: pass the basis vectors and translation in meters.

### 9.3. Component transform probe

| Step | Expected | Actual | Tolerance | Pass |
|---|---|---|---|---|
| identity origin | `(0,0,0)` | `(0,0,0)` | 1e-9 | Yes |
| identity x | `(1,0,0)` | `(1,0,0)` | 1e-9 | Yes |
| translation origin | `(0.01,0.02,0.03)` m | `(0.01,0.02,0.03)` m | 1e-9 | Yes |
| translation x | `(1.01,0.02,0.03)` m | `(1.01,0.02,0.03)` m | 1e-9 | Yes |
| component `Transform2` origin | `(0.01,0.02,0.03)` m | `(0.01,0.02,0.03)` m | 1e-9 | Yes |
| component x basis | `(0.01,-0.98,0.03)` m | same | 1e-9 | Yes |
| `GetTotalTransform` | equal to `Transform2` | equal | 1e-9 | Yes |

`AddComponent5(...,0,0,0)` used only to create the occurrence; exact placement used `Transform2`/`SetTransformAndSolve3`. `AddComponent5` X/Y/Z are approximate bounding-box positioning and are not the placement contract.

R1 full-matrix check (C524 Top, component rot 180°, state 30/45/60, dz +1 mm, board surface +1.004001 mm):

| Local point | Expected board XYZ, m | Actual `MathPoint` after `CreateTransform`, m | Error, m | Tolerance | Pass |
|---|---|---|---|---:|---|
| `(0,0,0)` | `(0.2625, 0.2935, 0.002004)` | `(0.2625, 0.2935, 0.002004)` | 0 | 1e-9 | Yes |
| `(0,0,3 mm)` | `(0.2602824, 0.2926590, 0.0038411)` | `(0.2602824, 0.2926590, 0.0038411)` | 0 | 1e-9 | Yes |

`MathTransform.Multiply` was not used as the contract composition primitive because its argument order was not part of the confirmed probe; the final 4×4 matrix was converted once to `ArrayData` (R^T + translation) and applied. `Transform2`/`SetTransformAndSolve3` use that final matrix.

Units: all SOLIDWORKS API values in this probe were meters. `0.01 m = 10 mm`; conversion from IU/mm to meters must occur exactly once before `CreateTransform`.

## 10. Финальная математическая модель

### 10.1. Frames

| Frame | Definition |
|---|---|
| `B` | Board frame: origin at Altium board origin, +X right, +Y up, +Z bottom→top, right-handed, mm |
| `C` | Component placement frame: component XY on board, +Z from bottom to top, rotated by component `RotationDeg` |
| `D` | Body/model frame: origin at model origin, local +Z along the body's outward normal for `BodyProjection` |
| `M` | Altium model frame: origin X/Y from `IPCB_Model.GetOrigin`, Z via `dz`; state rotations |
| `A` | STEP asset frame: local geometry inside the exported file |

### 10.2. Composition

```text
p_B = T_BC · T_CD · T_DM · T_MA · p_A
```

All matrices are 4×4, column vectors, mm in the mathematical model.

- `T_BC = T(x_c, y_c, 0) · Rz(θ_c)` — component position and rotation. Positive `RotationDeg` = CCW is confirmed by pad landmark points; 3D body feature-level residual is limited by the missing full board STEP.
- `T_CD = T_z(+T/2)` for Top (`BodyProjection=0`, `R_CD=I`); for Bottom (`BodyProjection=1`) full board STEP C572 (rot0) confirms `T_z(-T/2) · Rx(π)` with det=+1. Bottom with non-zero component rotation remains **Blocked** because the reference omits component rotation.
- `T_DM = T(delta_D) · T_z(dz) · R_state`, where `R_state = Rz(rz)·Ry(ry)·Rx(rx)` (confirmed by Z bounds), `dz` in mm, and `delta_D = R_BD^T · (x_m - x_c, y_m - y_c, 0)` with `R_BD = R_BC · R_CD` and `(x_m,y_m)` the absolute `GetOrigin` in board coordinates. Do not use `(x_m - x_c, y_m - y_c)` directly as a D-frame translation. `GetOrigin` Z is **Unknown** and set to 0 only as a schema placeholder, not as a placement fact.
- For Top (`R_CD = I`), `delta_D = Rz(θ_c)^T · delta_B`. For Bottom, `R_CD` must be the confirmed side transform; until Bottom is confirmed, `delta_D` is **Unknown**.
- `T_MA = I` for the exported STEP asset; STEP is local geometry and no second transform is applied.

### 10.3. Numeric origin-offset check (C62, Top, component rot 90°)

C62 has a Native model with a non-zero model-origin offset:

```text
component C62: (x_c, y_c) = (331.480, 307.130) mm, θ_c = +90°
GetOrigin:    (x_m, y_m) = (337.480, 310.630) mm
delta_B = (6.000, 3.500) mm
delta_D = Rz(+90°)^T · delta_B = (3.500, -6.000) mm
T_BC · T_CD · T_DM origin = (331.480, 307.130) + Rz(+90°) · (3.500, -6.000)
                              = (337.480, 310.630) mm
```

Using `delta_B` directly as a local translation would produce `(331.480,307.130) + Rz(+90°)·(6.000,3.500) = (327.980,313.130) mm`, which is **9.497 mm** away from the measured `GetOrigin`; this candidate is rejected. `R_CD=I` is assumed for this Top case; Bottom requires a confirmed `R_CD`.

### 10.4. Numeric z check

For a top body with state 0, local geometry z=0…3 mm and `T=2.008 mm`:

```text
near boundary: T/2 + Standoff = 1.004001 + 0 = 1.004001 mm
far boundary:  T/2 + Overall  = 1.004001 + 3 = 4.004001 mm
```

For C524 (state 30,45,60, dz=+1 mm), `R_state` as above and `z_local∈[-8.485281,4.311991]`; with `dz=+1` and `T/2=1.004001`:

```text
near = 1.004001 + (-8.485281 + 1.000) = -6.481280 mm
far  = 1.004001 + ( 4.311991 + 1.000) =  6.315992 mm
Altium reported Standoff=-7.485281, Overall=5.311988 relative to surface;
T/2 + Standoff = -6.481280; T/2 + Overall = 6.315989. Error < 0.00001 mm.
```

### 10.5. SOLIDWORKS adapter

For a desired column-vector transform `p_B = R p_A + t` (both in meters):

```text
ArrayData[0..8] = row-major R^T
ArrayData[9..11] = (tx, ty, tz)
ArrayData[12] = 1
ArrayData[13..15] = 0
```

or construct with `ComposeTransform(XAxis, YAxis, ZAxis, Translation, 1.0)`. Do not pass a user 4×4 row-major array directly without transposing the rotation and checking translation indices.

## 11. Bottom-side

Наблюдения:

- `BodyProjection` для Bottom bodies = `TBoardSide.eBoardSide_Bottom` (1);
- `StandoffHeight`/`OverallHeight` для Bottom C572/C570 = 0/3 mm, same as Top;
- exported STEP для Bottom C572/C570 имеет тот же bbox/volume, что Top, без mirror;
- `GetState_FlippedOnLayer()` = false на Bottom; не использовать как сторону;
- body layer (32 vs 70) меняется, но STEP не меняется.

Кандидаты:

1. proper `Rx(π)` (det=+1), local +Z → board -Z, +Y → -Y;
2. proper `Ry(π)` (det=+1), local +Z → board -Z, +X → -X;
3. reflection in board plane (det=-1) — не подтверждён и не должен применяться к общему asset без доказательства.

R2 full board STEP C572 (Bottom, rot0) даёт transform block `diag(1,-1,-1)` = `Rx(180°)`; determinant +1, reflection не требуется. Translation `z=-T` в STEP frame соответствует `Tz(-T/2)` в R1 midplane frame. Поэтому для Bottom rot0 confirmed: `Rx(180°)`. Bottom с component rotation остаётся **Blocked**, потому что full board STEP не содержит component RotationDeg для fixture bodies. STEP asset не зеркалится.

## 12. Golden reference

Использованы:

1. Altium `BodyProjection`, `StandoffHeight`, `OverallHeight`, `GetOrigin`, `GetState` для каждого control instance.
2. Per-body STEP/X_T exports, импортированные в SOLIDWORKS 2026 с проверкой volume/bbox.
3. Контрольные размеры fixture: 12×7×3 Generic, 12×7×3 Native L, protrusion 2×3×2, hole Ø1 at (2,2).
4. R2 full board STEP: SHA `7FC3E1C871A657348555A02FCD81220EC6538E4D1971320CB01E81995F8B9894`; board frame, component transforms, board body/cutout volume.

R2 full board reference использован для C366 feature-level проверки, board origin, `GetState`, `dz` и Bottom rot0. Он **не содержит component RotationDeg** для test bodies, поэтому component-rotation cases не считаются подтверждёнными. Visual screenshots не использованы как численное доказательство.

## 13. Ошибки и допуски

| Check | Expected | Actual | Error | Tolerance | Pass |
|---|---:|---:|---:|---:|---|
| Generic volume | 95.2146018 mm³ | 95.2146018 mm³ | 0 | 0.001 | Yes |
| Native volume | 147.000 mm³ | 146.999118 mm³ | 0.000882 | 0.1% | Yes |
| Generic bbox | 12×7×3 mm | 12×7×3 mm | 0 | 0.001 | Yes |
| Native bbox | 12×7×3 mm | 12×7×3 mm | 0 | 0.001 | Yes |
| C524 z_min | -7.485281 mm | -7.485281 mm | 0.000000 | 0.001 | Yes |
| C524 z_max | 5.311991 mm | 5.311988 mm | 0.000003 | 0.001 | Yes |
| C516 z_min | -9.485281 mm | -9.485277 mm | 0.000004 | 0.001 | Yes |
| C516 z_max | 3.311991 mm | 3.311992 mm | 0.000001 | 0.001 | Yes |
| SW translation | 0.01 m | 0.01 m | 0 | 1e-9 | Yes |
| SW x basis | (0,-1,0) for chosen array | (0,-1,0) | 0 | 1e-9 | Yes |

## 13.1. R2 full board STEP golden reference

### 13.1.1. Export

Полный STEP получен из Altium Designer 26.12.2.9 через `File → Export → STEP 3D` для копии `test-matrix-r2.PcbDoc`:

| Параметр | Значение |
|---|---|
| File | `%TEMP%\EcadMcadBridge\BRIDGE-000016\R2\test-matrix-r2.step` |
| Size | `86 127 237` bytes |
| SHA-256 | `7FC3E1C871A657348555A02FCD81220EC6538E4D1971320CB01E81995F8B9894` |
| STEP schema | AP214 `AUTOMOTIVE_DESIGN` |
| Units | `SI_UNIT(.MILLI.,.METRE.)` — mm |
| 3D Bodies Export Options | `Prefer generic 3D models` |
| Components With 3D Bodies | `Export All` |
| Export As Single Part | OFF |
| Export Folded Board | OFF |
| Pad Holes | `Export All` |
| Component Suffix | None |
| Board included | Yes, one solid board body |
| Cutouts | Not independently confirmed; board body has 576 vertices and non-rectangular outline |

Export was made from a copy; `test-matrix.PcbDoc` не изменялся. STEP в Git не добавлен. Target Designators found in the STEP: `C366`, `C63`, `C59`, `C61`, `C62`, `C524`, `C516`, `C572`, `C573`, `C567`, `C571`. Full component count was not enumerated; these are the calibrated cases.

### 13.1.2. Board frame

Полный STEP использует **не Altium absolute XY**, а board-local frame:

- board body bbox: `0…175 mm × 0…155 mm × 0…2.028 mm`;
- Altium outline absolute: `212.6…387.6 × 167.2…322.2 mm`;
- Altium board thickness: `2.00800208 mm`; STEP board thickness: `2.02800204 mm` (расхождение 0.02 mm; причину нужно уточнить; STEP board body includes export geometry/tolerance).

`T_BS = T(-212.6, -167.2, -T/2)` переводит R1 board frame `B` (Altium absolute, midplane) в STEP board frame `S` (outline min, top surface `z=0`). Для C366: R1 B position `(225.0966,197.3255,+1.004001)` → S `(12.4966,30.1255,0)`, что совпало с полным STEP `(0.012496604,0.030125472,0) m` с ошибкой ~0.000006 mm.

Board volume `5.39806014e-5 m³ = 53 980.60 mm³`. Gross rectangle `175 × 155 × 2.028 = 55 009.50 mm³` не является корректным cutout-доказательством: outline имеет вырез/ступень (Altium contour), поэтому deficit нельзя приписать только cutouts. Точные cutout landmark points не извлечены — `Unknown`.

### 13.1.3. Component reference transforms

В полном STEP компонентные occurrence transforms (SOLIDWORKS `GetTotalTransform`) для target components:

| Ref | STEP transform translation, m | STEP rotation | R1 expectation | Result |
|---|---|---|---|---|
| C366 | `(0.012496604, 0.030125472, 0)` | identity | identity + same t | Pass |
| C62 | `(0.124879981, 0.143430007, 0)` | identity | `Rz(+90°)` | Translation pass, rotation **Fail** |
| C524 | `(0.049900001, 0.126300002, 0.000999997)` | `R_state` | `Rz(180°)·R_state` | Translation/state pass, component rotation **Fail** |
| C516 | `(0.059100013, 0.126900008, -0.000999997)` | `R_state` | `Rz(180°)·R_state` | Translation/state pass, component rotation **Fail** |
| C572 | `(0.093000007, 0.028925002, -0.002028001)` | `diag(1,-1,-1)` (`Rx(180°)`) | `Tz(-T/2)·Rx(180°)` | Pass for rot0 Bottom |

Full board STEP also returned identity/state-only transforms for C63 (+90°), C59 (-180°), C61 (-90°), C573 (+90°), C567 (-180°), C571 (-90°). Therefore **Altium full board STEP in this fixture does not encode component RotationDeg for the test bodies**. It encodes `GetOrigin`, `GetState`, `dz`, `BodyProjection`, and Bottom `Rx(180°)`, but not `Rz(theta_c)`.

Это не доказывает, что component rotation не нужна в production placement: pad landmarks подтверждают positive=CCW. Это означает, что full board STEP fixture не является валидным 3D-эталоном для component rotation; нужен fixture с library-relative 3D body или другой независимый 3D reference. R2 Top feature-level placement **не закрыт**.

### 13.1.4. Feature-level residuals

Reference points взяты из полного STEP body extremes (six axis directions), calculated points — из per-body STEP + R1 transform. Для C366 (rot0) точки совпали:

| Component | Landmark | Reference X/Y/Z, mm | Calculated X/Y/Z, mm | Error, mm | Pass |
|---|---|---|---:|---:|---|
| C366 | +X extreme | (24.4966, 37.1255, 1.0000) | same | 0.000006 | Yes |
| C366 | -X extreme | (12.4966, 37.1255, 1.0000) | same | 0.000006 | Yes |
| C366 | -Z extreme | (24.4966, 37.1255, 0.0000) | same | 0.000006 | Yes |
| C366 | +Z extreme | (22.4966, 37.1255, 3.0000) | same | 0.000006 | Yes |

C524/C516 state translation and Z boundaries matched full board, but component rotation Rz(180°) is not present in the reference. Representative corner residual for the rotated case is about 27.8 mm; therefore rotation cases are not within tolerance and are **blocked**, not silently accepted. C572 rot0 Bottom matches `Rx(180°)` at translation/rotation level. Bottom with non-zero component rotation is also blocked because the reference omits component rotation.

### 13.1.5. R2 capability result

| Capability | R2 result |
|---|---|
| Board origin/outline/thickness | Board-local frame confirmed; thickness mismatch 0.02 mm unresolved |
| Top rot0 Generic placement | Pass at feature level, ≤0.000006 mm |
| Top model origin offset C62 | Translation pass; component rotation 3D **blocked** |
| Top combined state C524/C516 | State/dz translation pass; component rotation 3D **blocked** |
| Top component rotation sign | 2D pad landmarks confirm CCW; full STEP body does not encode it |
| Bottom rot0 | `Rx(180°)` confirmed by full STEP; implementation still out of R2 |
| Bottom with rotation | Blocked; reference omits component rotation |
| BodyProjection mismatch | Not exercised; blocked |
| Full board cutout landmarks | Not extracted; Unknown |

**R2 status: blocked for full Top placement sign-off.** The independent golden reference is valid for board frame, model origin, state rotation, dz, and Bottom rot0, but not for component RotationDeg; production placement cannot start until a valid rotation-bearing fixture/reference is produced.

## 13.2. R3 library-relative fixture attempt

R3 attempted to remove the R2 component-rotation blocker by creating a real PCB Library footprint with a library-relative Generic 3D body.

Environment and attempted route:

| Step | Result |
|---|---|
| Copy `test-matrix.PcbDoc` to `test-matrix-r3.PcbDoc` | Created outside Git |
| `Design > Make PCB Library` on standalone PCB | No persisted `.PcbLib` appeared; command did not produce a usable library document |
| `File > New > Library...` | Created an in-memory `PcbLib1.PcbLib`; initial library shell |
| Save As through Windows dialog | Saved `BRIDGE-000016-R3.PcbLib` shell, 105 472 bytes, SHA-256 `A8DDA92D0D616461AA7803C55693194E1BEB4D584CFBFA501CFF52FDA09F4D51` |
| `Place > 3D Body` with `control-generic.step` | Dialog accepted the file path, but no runtime proof that a body was attached to a footprint; library did not report a modified state and file size did not change after Save |
| API ownership probe for `IPCB_ComponentBody` | Not run: the allowed fixture did not contain a verifiable library footprint/body |

No R3 full board STEP, per-body library STEP, occurrence matrices, or feature residuals were produced. The R3 fixture is therefore **not a valid library-relative fixture**; it is only a shell library and must not be used as evidence.

Cause is tooling/fixture construction, not a measured contradiction of the R1 formula. Creating a valid library fixture requires either an operator-authored `.PcbLib` with `BRIDGE_ROT_3D` footprint and embedded Generic body, or an explicitly approved temporary .NET SDK extension installed/loaded in Altium (registration/settings change requires operator approval under current constraints).

R3 status: **blocked for fixture creation**; R2 component-rotation blocker remains open. No Top/Bottom rotation capability is enabled by R3.

## 13.3. R3-FIX-001 native PcbLib fixture attempt

Использован существующий shell:

- initial SHA-256: `A8DDA92D0D616461AA7803C55693194E1BEB4D584CFBFA501CFF52FDA09F4D51`
- initial size: `105 472` bytes.

Native Altium workflow:

1. Открыт `BRIDGE-000016-R3.PcbLib` в Altium 26.12.2.9.
2. Открыта панель `View → Panels → PCB Library`; в списке были `PCBCOMPONENT_1` и duplicate-копии.
3. Через `Tools → New Blank Footprint` / переименование footprint строка переименована в `BRIDGE_ROT_3D`.
4. Включён режим редактирования footprint (кнопка `Edit` в PCB Library panel).
5. `Place → 3D Body` → `control-generic.step` → Open.
6. Модель появилась в workspace; выполнен явный left-click, затем right-click для выхода из placement mode.
7. Properties подтвердили: `3D Model Type = Generic`, `Source = Embed Model`, `Identifier = control-generic`, `Layer = Mechanical 1`.
8. PcbLib сохранён штатным `File → Save`.

Postconditions:

| Check | Result |
|---|---|
| Footprint name | `BRIDGE_ROT_3D` |
| Footprint list row | `BRIDGE_ROT_3D; 0; 1` (0 pads, 1 primitive) after operator removed duplicate |
| Selected 3D Body properties | Generic, Embedded Model, `control-generic`, Mechanical 1 |
| Component Body Manager | `Component bodies in: BRIDGE_ROT_3D` lists exactly 1 body: `130 200.271 sq mils` |
| Document after Save | no asterisk; saved |
| Final size | `94 720` bytes |
| Final SHA-256 | `FECA6F37AD9A690CE1F1A990CD41D8AEC2FF8BD1AE7D7D66F0B17B68FF03B19F` |

Body ownership is confirmed by the PCB Library panel row + `Component Body Manager for component: BRIDGE_ROT_3D`; the expected Generic/Embed model is present, and after removing the duplicate the footprint contains exactly one 3D body. The PcbLib file SHA differs from the initial shell, so **R3 fixture creation is complete**.

Component placement on PCB, full STEP export and occurrence-transform comparison were not completed in this task. Therefore component `RotationDeg` calibration remains **blocked** until the full STEP occurrence-transform comparison is performed in the next task.

## 13.4. R3-FIX-002 placement attempt

Library fixture was verified at final SHA `FECA6F37AD9A690CE1F1A990CD41D8AEC2FF8BD1AE7D7D66F0B17B68FF03B19F`, size `94 720` bytes:

- footprint `BRIDGE_ROT_3D`;
- panel row `0 pads, 1 primitive`;
- Component Body Manager: exactly one body, `130 200.271 sq mils`;
- model Generic/Embedded `control-generic`, Mechanical 1.

A board copy `test-matrix-r3-placement.PcbDoc` was opened and the library Place control was used. Four instances were created on the board, but the UI automation edited the `Comment` field instead of `Designator`; actual designators remained default (`Designator…`), not `U101…U104`. No full STEP was exported and no SOLIDWORKS occurrence transforms were read. The board copy was not saved as a verified placement fixture.

R3-FIX-002 is therefore **blocked at component property/edit/export automation**, not at the library fixture. No rotation capability is enabled from this attempt.

## 13.5. R3-FIX-003 SDK-native placement and rotation reference

### 13.5.1. Runtime result

Temporary diagnostic handler was built into the existing `EcadMcadBridge.Altium` add-in and invoked through the existing `Tools → Export Board Snapshot` command; the installed extension registration was not changed. The handler was removed from the working tree before documentation commit.

Inputs:

| Item | Value |
|---|---|
| PcbLib | `BRIDGE-000016-R3.PcbLib` |
| PcbLib SHA-256 before/after | `FECA6F37AD9A690CE1F1A990CD41D8AEC2FF8BD1AE7D7D66F0B17B68FF03B19F` (unchanged) |
| Scratch PCB | `test-matrix-r3-sdk-placement.PcbDoc` |
| PCB SHA-256 after save | `69C62604A25972BDE59C65A98866793BC2D8674C211735748728E4E4A7AC3D67` |
| Full STEP | `test-matrix-r3-sdk-placement.step` |
| Full STEP SHA-256 | `75B8DD8DA0921D0BFD32489A9CBBE4E465555EECA5475A42F75BE4D61280D8ED` |
| Full STEP size | `86 154 564` bytes |
| Schema/units | AP214; mm |

The SDK created exactly four components from footprint `BRIDGE_ROT_3D`:

| Designator | X mm | Y mm | RotationDeg | Body count | Owner resolved | Model | Embedded | Projection |
|---|---:|---:|---:|---:|---|---|---|---|
| U101 | 240 | 220 | 0 | 1 | Yes | Generic | Yes | Top |
| U102 | 280 | 220 | 90 | 1 | Yes | Generic | Yes | Top |
| U103 | 320 | 220 | 180 | 1 | Yes | Generic | Yes | Top |
| U104 | 360 | 220 | 270 | 1 | Yes | Generic | Yes | Top |

Runtime JSON `r3-sdk-placement.json` recorded body data size `27 834` bytes for all four, `bodyInComponent=true`, `bodyInBoard=true`, Standoff `0`, Overall `1 181 102 IU` (`3.0 mm`), and Designator readback exactly `U101`…`U104`; Comment remained `Comment`. PcbLib hash was unchanged.

The PCB was saved, closed, reopened, and the read-only SDK enumeration was run again. All four Designators, rotations, pattern, model type/embedding, and body ownership survived serialization.

### 13.5.2. Critical rotation-state finding

Inside the placed components, `IPCB_Model.GetState` returned `rotZ = 0/90/180/270` matching the component rotation, while the source footprint model state was `(0,0,0,0)`. This means the placed-body `GetState` is not an independent local state: Altium has already incorporated the component rotation into the body model state.

Therefore the R1 formula must not add both `Rz(component_rotation)` and the placed-body `R_state` when both are read from the placed component. For this library-relative fixture the measured full STEP occurrence contains exactly one rotation:

- use footprint-local model state `(0,0,0,0)` + component `RotationDeg`, or
- use the placed-body `GetState` alone and set the component rotation term to identity.

The full STEP relative matrices below match the component-rotation term exactly; no double rotation is present.

### 13.5.3. SOLIDWORKS occurrence transforms

SOLIDWORKS 2026 SP3.2 imported the full STEP with separate occurrences U101–U104, one solid body each, volume `95.2146018 mm³`, and one `control-generic` body per occurrence.

The full board STEP uses board-local coordinates. For the relative test, positions were converted to the same board-local frame used by the STEP:

```text
P0 = (0.0274, 0.0528) m
P(U102) = (0.0674, 0.0528) m
P(U103) = (0.1074, 0.0528) m
P(U104) = (0.1474, 0.0528) m
M0 translation = (0.023085654706523, 0.054540449658047, 0) m
```

Actual child occurrence transforms (rotation converted from SOLIDWORKS `R^T` array to column-vector `R`):

| Occurrence | Actual translation, m | Actual R | Determinant |
|---|---|---|---|
| U101 | `(0.023085655, 0.054540450, 0)` | identity | +1 |
| U102 | `(0.065659553, 0.048485655, 0)` | `Rz(+90°)` | +1 |
| U103 | `(0.111714332, 0.051059554, 0)` | `Rz(+180°)` | +1 |
| U104 | `(0.149140449, 0.057114348, 0)` | `Rz(−90°)` | +1 |

Prediction `M_pred(θ) = T(Pi) · Rz(θ) · T(-P0) · M0` produced the same translations/matrices. Maximum translation residual `0.0000136 mm`; all three relative matrices matched the expected basis vectors and determinant.

### 13.5.4. Feature-level XYZ residuals

Landmarks used: local `(0,0,0)`, `(12,0,0)`, `(0,7,3)`, `(10,7,3)`, `(12,7,0)` mm, including points at `z=0` and `z=3`. Reference points were computed from the actual full STEP occurrence transform; calculated points from the per-body Generic asset plus `M_pred(θ)`.

| Component | Landmarks | Max error, mm | RMS error, mm | Tolerance, mm | Pass |
|---|---:|---:|---:|---:|---|
| U102 | 5 | 0.0000028 | 0.0000024 | 0.01 | Yes |
| U103 | 5 | 0.0000136 | 0.0000132 | 0.01 | Yes |
| U104 | 5 | 0.0000028 | 0.0000028 | 0.01 | Yes |

**Top Component RotationDeg blocker is removed** for library-relative Generic bodies. The component rotation is confirmed in translation, rotation matrix, basis, determinant, and feature-level XYZ. Bottom remains outside R3-FIX-003 and is still blocked.

## 14. Неподтверждённые параметры

| Параметр | Статус | Причина |
|---|---|---|
| Component `RotationDeg` sign/3D rotation | **Confirmed for Top library-relative body** | R3-FIX-003: U101–U104, full STEP relative matrices and 5-landmark residuals ≤ 0.000014 mm; Bottom remains separate |
| Bottom proper rotation axis / reflection | **Rx(180°) confirmed for rot0; rotation-bearing Bottom Blocked** | full board STEP C572 = `diag(1,-1,-1)`; C573/C567/C571 same state-only, component rotation missing |
| `BodyProjection != Component.Side` | **Unknown** | в fixture все тела совпали со стороной компонента |
| `IPCB_Model.Rotation` | **Unknown** | на fixture 0; отдельное свойство, не совпало с state rotZ |
| `GetOrigin.Z` | **Unknown** | API даёт X/Y; Z не предоставлен |
| Board origin | **Altium absolute in Core; STEP uses board-local `T_BS`** | `T_BS = T(-212.6,-167.2,-T/2)`; C366 matched STEP within ~0.000006 mm; отдельного boardOrigin поля в Core нет |
| `SnapCount` / `SnapPoint` | **Unknown** | API документирован, значения fixture не сняты |
| Body projection polygon → 3D matrix | **Partially confirmed** | rectangle sides/orientation and Z bounds matched RzRyRx; full board model state confirmed, component rotation blocked |
| Full board STEP golden reference | **Obtained; rotation coverage incomplete** | file SHA `7FC3E1C…B9894`; board/origin/state/dz/Bottom rot0 confirmed; component RotationDeg not encoded |
| Board thickness | **Mismatch 0.02 mm** | Altium properties 2.00800208 mm; STEP board body 2.02800204 mm; unresolved |
| Board cutout landmarks | **Unknown** | board volume deficit confirms voids; exact cutout XYZ not extracted |
| Component height vs body height | Not used | `Component.Height` не равен body Overall (C75: 10.5 vs 4.0 mm) |

## 14.1. Capability matrix

| Capability | Status | First placement version |
|---|---|---|
| Top rot0, state 0, Generic/Native | C366 full board feature-level pass, ≤0.000006 mm | Allowed |
| Top combined Rx/Ry/Rz + dz (component rot0) | Full board state matrix/dz matches C524/C516 at rot0-relative frame | Allowed only with component rotation excluded |
| Top non-zero model-origin offset + component rot90/180/−90 | R3-FIX-003: `M0` offset differs from `P0`; relative matrix and 5 landmarks ≤0.000014 mm | Confirmed for library-relative Generic body |
| Top component RotationDeg 90/180/270 | R3-FIX-003 full board STEP: U102/U103/U104 transforms are `Rz(+90/+180/−90)` with determinant +1 | Confirmed |
| Top private model state (placed body rotZ already includes component rotation) | R3-FIX-003: do not add `Rz(component)` to placed-body `rotZ` | Rule recorded in ledger C-107…C-112 note |
| Bottom rot0 | Full board STEP confirms `Rx(180°)` and translation | Confirmed but excluded from R2 implementation |
| Bottom with component rotation | Reference omits rotation | **Blocked** |
| BodyProjection != Component.Side | Not exercised | Excluded |
| Multi-body component | Mapping exists; 3D transform not separately validated | Mapping only, no placement claim |
| SnapPoint origin correction | Runtime values Unknown | Excluded |
| Full board STEP reference | R2 board/origin/state/dz/Bottom rot0; R3 adds rotation-bearing Top U101–U104 | Confirmed for Top; Bottom rotation not covered |

## 15. Рекомендуемая структура assembly

Для MiniPC: 1 046 компонентов, 1 055 body references, 155 уникальных STEP assets.

Вариант A — каждое тело отдельным occurrence в root assembly:

- ~1 055 occurrences, простой mapping body→asset;
- multi-body физическая сущность компонента теряется, если не хранить отдельный mapping;
- больше occurrences, но меньше уникальных документов при повторном использовании asset parts.

Вариант B — каждый физический компонент отдельный component node/подсборка:

- 1 046 component nodes; тела входят в component part/subassembly;
- сохраняет физическую сущность и multi-body;
- если делать отдельный native part на компонент — до 1 046 parts, дорого по файлам/памяти;
- лучший гибрид: один native part на уникальный asset (155) + configuration/body visibility per component, либо derived part для несовпадающих наборов тел.

Рекомендация: **B с asset-level native parts и component-level configurations/derived bodies**, если SOLIDWORKS configuration/body suppression выдержит MiniPC; иначе A с обязательным `ComponentId + BodyOrdinal` occurrence mapping. Перед массовой сборкой нужен cost test на 10–20 компонентах и оценка памяти.

## 16. Предлагаемые изменения Core schema (только проект)

Добавить в `ComponentBodyEntry`:

```text
BodyProjectionSide? bodyProjectionSide   // Top | Bottom | Unknown
LengthValue standoffHeight               // nullable IU/mm pair
LengthValue overallHeight                // nullable IU/mm pair
```

Поведение:

- все новые поля nullable/optional; старый `models.json` 1.0.0 читается без изменений;
- `BodyProjectionSide` не выводить из `Component.Side`; неизвестное — `null`/`Unknown`;
- `StandoffHeight`/`OverallHeight` хранить как `LengthValue` (IU + вычисленные mm), не как independent mm;
- `StandoffHeight` может быть отрицательным (пример C522), поэтому не валидировать как non-negative; если оба значения известны, проверять `Standoff <= Overall` с допуском;
- `SnapPoint` не включать в первый delta: API есть, но fixture-значения не сняты; при необходимости добавить `ModelSnapPoint` с `Index`, `X/Y/Z IU/mm` и `Index=0` origin;
- version bump `models.json` → `1.1.0` при добавлении полей; reader 1.0.0 остаётся backward compatible, writer пишет 1.1.0 только после согласования с PR #13;
- JSON names: `bodyProjectionSide`, `standoffHeightIu/mm`, `overallHeightIu/mm` (или вложенные `LengthValue`-объекты, как принято в текущем schema);
- validation: side/projection mismatch допустим; asset transform не считается полным без board thickness, `GetOrigin.Z`, side transform и rotation order.

## 17. План следующего этапа

Core:

1. Согласовать `BodyProjectionSide`/heights delta и версию `models.json`.
2. Добавить pure math tests для `T_BC`, `T_DM`, `R_state=Rz*Ry*Rx`, bottom candidates.
3. Не добавлять placement до подтверждения Bottom и board origin.

Altium adapter:

1. Читать `BodyProjection`, `StandoffHeight`, `OverallHeight`, `SnapCount/SnapPoint` (snap optional).
2. Сохранять raw IU и mm; не выводить projection из component side.
3. Проверить `GetOrigin.Z`/board origin и `IPCB_Model.Rotation` отдельными controlled tests.

SOLIDWORKS backend (после отдельного решения):

1. Реализовать adapter `Matrix4x4` → `MathTransform` через `ComposeTransform`/`R^T`.
2. Применять transform только к occurrence; STEP asset не зеркалить.
3. Проверить Bottom `Rx(π)` vs `Ry(π)` на асимметричной модели с golden reference.
4. Проверить multi-body/component configuration и MiniPC cost.

## 18. Ограничения R0

- Production PCB assembly builder не создавался.
- Core schema не изменялась.
- PR #13/#14 не изменялись.
- STEP assets не зеркалились.
- Altium PCB не изменялась.
- Автоматические толщина/высота не подставлялись.
- Add-in registration, Registry, installer, UI не добавлялись.
- Merge не выполнялся.

## 19. Проверки

- `dotnet build -c Release` — 0 warnings, 0 errors.
- `dotnet test -c Release --no-build` — Core 140/140, Altium 82/82, всего 222/222.
- `git diff --check` — OK (только informational LF→CRLF warnings).
- Representative SOLIDWORKS transform probe `SwTransformProbe`: basis and component transform passed; results в разделе 9.
- Representative Altium/SOLIDWORKS per-body STEP measurement: volume/bbox passed; results в разделе 8.
- Component rotation sign: pad landmark check passed.
- Board origin: outline/component absolute-frame check passed.
- Full Altium board STEP R2: SHA `7FC3E1…B9894`; board/origin/state/dz/Bottom rot0 checks passed; component RotationDeg not encoded for those fixture bodies.
- R3-FIX library fixture: complete (footprint `BRIDGE_ROT_3D`, one Generic/Embed body, PcbLib SHA `FECA6F37…`).
- R3-FIX-003: SDK placed U101–U104 with correct Designators and one owned Generic body each; PCB SHA `69C62604…3D67` after save/reopen; full STEP SHA `75B8DD8D…8ED` (AP214, mm, 86 154 564 bytes); SOLIDWORKS occurrence transforms, determinants, basis and 5-landmark residuals ≤0.000014 mm.
- Temporary diagnostic handler removed; installed `EcadMcadBridge` extension restored to the clean 0.4.0 build, registration untouched.
- `dotnet build -c Release` and `dotnet test -c Release` repeated on branch after restoring the worktree.
- Full MiniPC assembly не запускался.

## 20. Commit и PR

- Commit: `ab76100` (`docs(bridge-000016): record R3-FIX-003 SDK placement and Top rotation confirmation`); предыдущий blocker commit — `1402e3a`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/15
- Base: `source`
- Merge: запрещён.

## 21. История изменений

- 2026-11-25: R0 report создан; использован controlled fixture `BRIDGE-000011`, добавлены новые SOLIDWORKS transform probes и numerical checks.
- 2026-11-25: R1 добавлены frame composition, delta_D, Rz·Ry·Rx и C62 origin-offset проверки.
- 2026-11-25: R2 добавлен full board STEP reference, board frame, C366 residuals и capability matrix.
- 2026-11-25: R3-FIX-001 создан `BRIDGE_ROT_3D` с одним Generic/Embedded телом; R3-FIX-002 упёрся в UI-автоматизацию свойств (Designator попал в Comment).
- 2026-11-25: R3-FIX-003 выполнен SDK-native placement U101–U104, save/reopen, full STEP и SOLIDWORKS проверка; Top component rotation подтверждён, Bottom остаётся blocked.
