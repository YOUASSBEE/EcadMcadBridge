---
id: BRIDGE-000016
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T20:30:00+03:00
updated: 2026-11-25T20:30:00+03:00
base: source
branch: task/BRIDGE-000016-coordinate-contract
commit: 20654ac
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/15
related:
  - BRIDGE-000009
  - BRIDGE-000011
  - BRIDGE-000013
  - BRIDGE-000014
  - BRIDGE-000015
---

# BRIDGE-000016-R0 — Altium → SOLIDWORKS coordinate contract and calibration

## 1. Итог

R0 подтвердил математическую модель для `STEP asset → 3D body → Altium component → PCB board → SOLIDWORKS assembly` в объёме, достаточном для следующего этапа, и явно отделил неподтверждённые части.

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

- знак/направление component `RotationDeg` на board (контракт Core задаёт CCW, но 3D golden reference для знака не получен);
- конкретная proper rotation для Bottom (`Rx(180)` vs `Ry(180)`) и необходимость отражения; STEP/body projection не содержат mirror;
- независимый случай `BodyProjection != Component.Side` (в фикстуре все тела совпали со стороной компонента);
- `IPCB_Model.Rotation` как отдельное свойство: на фикстуре он был 0 и не совпал с `GetState` rotZ;
- Z-составляющая `GetOrigin` и явный board origin в Core snapshot;
- `SnapCount`/snap points: API подтверждён документацией, значения на фикстуре не сняты;
- полный Altium board STEP golden reference: не выполнялся; сравнение сделано по per-body STEP, Altium body projection и Z-границам, без выдачи визуального совпадения за численное.

R0 не реализует production placement, не меняет Core schema и не меняет PR #13/#14.

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
| Component rotation | 0/90/180/270 | не входит в STEP/body bbox; body projection остаётся локальным | Sign **Unknown** |
| Bottom | Top vs Bottom | STEP и body projection не зеркалированы; `GetFlippedOnLayer=false` | Transform **Unknown** |

Одиночные 90° меняют extents body projection, но не доказывают знак вращения. Combined order определён по Z-границам; body projection polygon не использовался как единственное доказательство.

## 7. Combined Euler rotation

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

This confirms the order for the tested quadrants and non-zero combined state. It does not independently confirm component `Rz` sign or Bottom axis.

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

- `T_BC = T(x_c, y_c, 0) · Rz(θ_c)` — component position and rotation. `Rz` sign is the Core contract (positive CCW); independent 3D sign validation is **Unknown**.
- `T_CD = T_z(+T/2)` for Top (`BodyProjection=0`); candidate `T_z(-T/2) · Rx(π)` for Bottom (`BodyProjection=1`). Bottom axis/reflection is **Unknown**; `Ry(π)` remains an alternative.
- `T_DM = T(x_m - x_c, y_m - y_c, 0) · R_state · T_z(dz)`, where `R_state = Rz(rz)·Ry(ry)·Rx(rx)` (confirmed by Z bounds), `dz` in mm, and `(x_m,y_m)` is the absolute `GetOrigin` in board coordinates. Subtract component `(x_c,y_c)` once; do not translate by absolute `GetOrigin` after `T_BC`. `GetOrigin` Z is **Unknown** and set to 0 only as a schema placeholder, not as a placement fact.
- `T_MA = I` for the exported STEP asset; STEP is local geometry and no second transform is applied.

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

### 10.3. SOLIDWORKS adapter

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

R0 **не выбирает** между 1 и 2. STEP asset не зеркалится. Полный Altium 3D export golden reference для Bottom не получен; визуальное совпадение не считается численным доказательством.

## 12. Golden reference

Использованы:

1. Altium `BodyProjection`, `StandoffHeight`, `OverallHeight`, `GetOrigin`, `GetState` для каждого control instance.
2. Per-body STEP/X_T exports, импортированные в SOLIDWORKS 2026 с проверкой volume/bbox.
3. Контрольные размеры fixture: 12×7×3 Generic, 12×7×3 Native L, protrusion 2×3×2, hole Ø1 at (2,2).

Полный Altium PCB STEP export (board + placed models) **не выполнялся** в R0. Поэтому:

- numerical checks for geometry/orientation use body projection and SOLIDWORKS bbox/volume;
- no board-level point-to-point comparison against Altium STEP;
- visual screenshots (`sw-generic-top.bmp`) не использованы как численное доказательство.

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

## 14. Неподтверждённые параметры

| Параметр | Статус | Причина |
|---|---|---|
| Component `RotationDeg` sign on board | **Unknown** | нет 3D golden reference; Core contract задаёт CCW, но эксперимент не подтвердил знак |
| Bottom proper rotation axis / reflection | **Unknown** | STEP/body projection не зеркалированы; нет Bottom golden reference |
| `BodyProjection != Component.Side` | **Unknown** | в fixture все тела совпали со стороной компонента |
| `IPCB_Model.Rotation` | **Unknown** | на fixture 0; отдельное свойство, не совпало с state rotZ |
| `GetOrigin.Z` / board origin export | **Unknown** | API даёт X/Y; Core не экспортирует board origin |
| `SnapCount` / `SnapPoint` | **Unknown** | API документирован, значения fixture не сняты |
| Body projection polygon → 3D matrix | **Unknown** | polygon не сопоставлен однозначно с 3D transform; Z bounds использованы отдельно |
| Full board STEP golden reference | **Unknown** | не выполнялся |
| Component height vs body height | Not used | `Component.Height` не равен body Overall (C75: 10.5 vs 4.0 mm) |

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
- Full MiniPC assembly не запускался.

## 20. Commit и PR

- Commit: `20654ac` (`docs(bridge-000016): add coordinate calibration report`).
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/15
- Base: `source`
- Merge: запрещён.

## 21. История изменений

- 2026-11-25: R0 report создан; использован controlled fixture `BRIDGE-000011`, добавлены новые SOLIDWORKS transform probes и numerical checks.
