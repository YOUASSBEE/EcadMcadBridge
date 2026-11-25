---
id: BRIDGE-000012
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T11:45:00+03:00
updated: 2026-11-25T11:45:00+03:00
base: source
branch: agent/BRIDGE-000012-board-step-reference
commit: e171b00
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/11
related: [BRIDGE-000010, BRIDGE-000011, PR-9, PR-10, V-27, V-29, V-30, V-33]
---

# BRIDGE-000012 — Native Altium Board STEP Transform Reference

## 1. Итог

Штатный STEP всей платы получен и импортирован в SOLIDWORKS 2026. Выполнен
отдельный STEP контрольной группы и локальные экспорты 23 моделей.

Главный результат:

- полный STEP **содержит** model state для Generic: C75/C76/C524/C516 имеют
  матрицу `Rz(-60) · Ry(-45) · Rx(-30)` и `dz` по Z; individual export её не
  содержит;
- Generic Bottom в полном STEP имеет **поворот 180° вокруг X**, `det=+1`, и
  смещение на `-2.028 mm`; это не отражение;
- Native Bottom в тестовой конфигурации **не перевёрнут**: identity-матрица и
  смещение `-5.028 mm`; это дефект synthetic control setup, не вывод о поведении
  всех native bodies;
- component rotation в полном STEP исходного vendor-компонента C22 (Altium 90°)
  даёт матрицу `Rz(-90)` в SW frame; для synthetic controls C63/C59/C61 и native
  controls матрица осталась identity, потому что тела были заменены через SDK и
  не унаследовали component rotation;
- XY-связь для исследованной контрольной группы подтверждена: STEP `X/Y =
  Altium X/Y - (212.600587, 167.200596) mm`; остатки ≤0.000537 mm. Board Outline
  после этой трансляции согласуется с AABB Board body `0…175 × 0…155 mm`.
- полная 3D-матрица `T_B_S` **не подтверждена**: XY-смещение установлено, но
  Z-origin, толщина/положение поверхностей и полная ориентация board-space ещё
  требуют отдельной проверки.

## 2. Исходные данные

| Параметр | Значение |
|---|---|
| Altium Designer | 26.12.2.9 |
| SDK | 4.0.1 |
| SOLIDWORKS | 2026 `34.3.2.0003` |
| Исходная PCB | `%TEMP%\EcadMcadBridge\BRIDGE-000011\test-matrix.PcbDoc` |
| SHA-256 исходной PCB | `28908F70769C6ABEDE125C1B3F3A132520F2C1157C5A897699320FA306D5E5FD` |
| Рабочая копия | `%TEMP%\EcadMcadBridge\BRIDGE-000012\test-matrix-export.PcbDoc` |
| SHA-256 рабочей копии | `28908F70769C6ABEDE125C1B3F3A132520F2C1157C5A897699320FA306D5E5FD` |

Использованы результаты PR #9 (`47172c2e7e1551c1b28ca22f33d45bffa9f9c1aa`) и
PR #10 (`bcbc87dbada6cedc8f99b0e3e8dfa42587fbd8fc`). Их ветки не изменялись.

## 3. Экспорт

### 3.1 `board-all.step`

Выполнен штатно через `File → Export → STEP 3D`.

Фактические настройки диалога:

| Параметр | Значение |
|---|---|
| Export As Single Part | Off |
| Components With 3D Bodies | Export All |
| 3D Bodies Export Options | Prefer generic 3D models |
| Skip Free 3D Bodies | On |
| Skip Hidden 3D Bodies | Off |
| Component Suffix | None |
| Pad Holes | Export Selected (в тестовой выборке нет выбранных pad holes) |
| Export Folded Board | Off |

Результат: `board-all.step`, 85 611 752 B, SHA-256
`59C9A241EEAC6543586F92BF5F31D4F4017AE76513654CF165B54D21C8F0C6C4`.
Board body присутствует; cutouts и модели компонентов — в файле.

### 3.2 `board-controls.step`

Из-за нестабильной UI-автоматизации Altium после выбора компонентов файл
`board-controls.step` сформирован тем же Altium `IPCB_Board3DModelExporter`
(selected components, Step, selected holes, no suffix, folded off); это
зафиксировано как ограничение метода, а не как отличие формата от штатного
экспортера. Выбраны 23 control components.

Результат: 301 393 B, SHA-256
`CC9933434D90F2A000BD09A348B6CD1E47B2E45999DB3BE8076AD924EC8647A1`.
Board body в файле присутствует.

### 3.3 Локальные body/model exports

Для всех 23 designator созданы каталоги `individual/<Designator>/` с
`model.step`, `body.step`, `body.x_t` (23 × 2 STEP + 23 X_T). В `body.step`
GEneric сохраняется исходная STEP-геометрия, X_T — Parasolid.

## 4. SOLIDWORKS 2026 import

### 4.1 `board-all.step`

- Загрузка: `LoadFile4`, errors=0, warnings=0.
- Тип: Assembly.
- Общее число элементов дерева (включая wrapper/nested): 8 973.
- Board body: 1 solid, bbox `0…175 × 0…155 × 0…2.028 mm` (локально),
  transform Z `-2.028 mm`; верхняя плоскость платы — `Z=0`, нижняя — `Z=-2.028 mm`.
- Designators сохранены как имена компонентов STEP (`C63_2`, `C59_5`, и т.п.);
  суффиксы `_N` являются collision suffixes импортера/STEP, не частью designator.

### 4.2 `board-controls.step`

- Загрузка: errors=0, warnings=0.
- Assembly: 57 компонентов (23 control + wrapper/nested assembly levels + Board).
- Board solid: `0…175 × 0…155 × 0…2.008 mm`; толщина на 0.02 mm меньше, чем в
  board-all (расхождение зафиксировано; причина не установлена).
- Control components идентифицируются по именам `C366`, `C63`, ... с суффиксами
  `_N`.

## 5. Матрицы в STEP-сборке SOLIDWORKS

`Transform2.ArrayData` — 16 значений, первые 9 — линейная часть 3×3 (row-major),
затем translation (m). Ниже `T` в mm, `det` линейной части. Все 23 — `det=+1`.

| Component | det | T (mm) | Linear part (3×3) |
|---|---:|---|---|
| C366 | +1 | (12.497, 30.125, 0) | Identity |
| C63 | +1 | (116.670, 139.940, 0) | Identity |
| C59 | +1 | (151.045, 114.500, 0) | Identity |
| C61 | +1 | (118.830, 134.520, 0) | Identity |
| C572 | +1 | (93.000, 28.925, -2.008) | Rx(180) |
| C573 | +1 | (94.700, 24.525, -2.008) | Rx(180) |
| C567 | +1 | (90.500, 20.625, -2.008) | Rx(180) |
| C571 | +1 | (86.000, 23.825, -2.008) | Rx(180) |
| C563 | +1 | (90.000, 25.525, 0) | Identity |
| C62 | +1 | (124.880, 143.430, 0) | Identity |
| B2 | +1 | (164.100, 30.950, 0) | Identity |
| C18 | +1 | (119.510, 121.730, 0) | Identity |
| C570 | +1 | (95.300, 32.525, -5.008) | Identity |
| R240 | +1 | (33.200, 85.000, -5.008) | Identity |
| C345 | +1 | (71.050, 35.100, -5.008) | Identity |
| C566 | +1 | (92.100, 24.725, -5.008) | Identity |
| C543 | +1 | (46.700, 148.500, 0) | Rx(-90) |
| C522 | +1 | (65.850, 127.800, 0) | Ry(-90) |
| C523 | +1 | (65.850, 127.000, 0) | Rz(-90) |
| C75 | +1 | (167.190, 97.760, +1.000) | `Rz(-60)·Ry(-45)·Rx(-30)` |
| C76 | +1 | (167.160, 108.620, -1.000) | same |
| C524 | +1 | (49.900, 126.300, +1.000) | same |
| C516 | +1 | (59.100, 126.900, -1.000) | same |

Linear components:
- Generic Bottom: `diag(1,-1,-1)` = rotation 180° about X, `det=+1`.
- Native Bottom: identity, `det=+1`; body not flipped (synthetic setup limitation).
- State C75/C76: matrix matches exactly `Rz(-60)·Ry(-45)·Rx(-30)` within
  numerical noise (max element error 4.3e-7).

## 6. Component rotation — sign and applicability

- Original vendor component **C22** (Altium rotation 90°) in `board-all.step`
  has transform `Rz(-90)` in SW frame, `det=+1`. This proves the full board STEP
  does include component rotation for normal vendor components.
- The synthetic control components C63/C59/C61 and all Native controls show
  Identity matrices despite Altium component rotation 90/180/270. They were
  created by replacing bodies/models via SDK; the full exporter therefore does
  not reflect their component rotation. This is a **control setup limitation**,
  not a valid matrix for those components.
- Sign conclusion: in the SW assembly frame, Altium +90° maps to `Rz(-90)`.
  Whether that is CCW/CW depends on the board STEP frame orientation; the
  documentation says Altium CCW from 3 o'clock, but full Altium 3D Layout visual
  comparison was not captured. Sign is therefore **observed in SW frame**, not
  independently proven as Altium board handedness.

## 7. Bottom-side

- Generic C572/C573/C567/C571: `Rx(180)`, `det=+1`, Z `-2.028 mm` (board-all;
  controls `-2.008` due board thickness difference). This is a rotation, not a
  reflection. No `det=-1` matrix was found.
- Native C570/R240/C345/C566: identity matrix with Z `-5.008 mm`; the bodies
  remain on the +Z side of the board and are not flipped. This is an artifact of
  the synthetic native body setup, not evidence about normal native components.
- Therefore no Bottom reflection was observed in this experiment; the only
  confirmed Bottom transform is Generic `Rx(180)` plus board-thickness Z offset.
  V-30 remains open for general Bottom behavior.

## 8. Board origin / Z

- Board-all Board body: local `Z=0…2.028`, transform `Z=-2.028`; top surface
  global `Z=0`, bottom `Z=-2.028`.
- `Component.GetState_XLocation/YLocation` и `Model.GetOrigin` на рабочей копии
  совпали для всех четырёх Generic Top controls: C366 `88620709/77687191`,
  C63 `129633858/120921259`, C59 `143167421/110905412`,
  C61 `130484252/118787401` (IU). Отдельного model offset для этих controls нет.
- Пересчёт C366: `88620709 / 393700 = 225.097051 mm`,
  `77687191 / 393700 = 197.325860 mm` (ранее в отчёте была арифметическая
  ошибка `88.6/77.7`; исправлено).
- Для четырёх Generic Top controls STEP translations дают постоянное смещение:

  | Component | Altium X/Y, mm | STEP X/Y, mm | Offset X/Y, mm |
  |---|---:|---:|---:|
  | C366 | 225.097051 / 197.325860 | 12.497 / 30.125 | 212.600051 / 167.200860 |
  | C63 | 329.270658 / 307.140612 | 116.670 / 139.940 | 212.600658 / 167.200612 |
  | C59 | 363.645977 / 281.700310 | 151.045 / 114.500 | 212.600977 / 167.200310 |
  | C61 | 331.430663 / 301.720602 | 118.830 / 134.520 | 212.600663 / 167.200602 |

  Среднее: `(212.600587, 167.200596) mm`; максимальный остаток
  `0.000537 mm` по X и `0.000287 mm` по Y. Это подтверждает общую XY-трансляцию
  для исследованной группы в пределах округления/численной точности.

- Board Outline (Altium): характерные вершины `(212.6,167.2)`, `(387.6,322.2)`,
  `(341.6,171.2) mm`; после вычитания среднего offset они дают
  `(0,0)`, `(175,155)`, `(129,4) mm`. Board body STEP AABB
  `0…175 × 0…155 mm` содержит эти точки; это подтверждает, что XY-смещение не
  является случайным совпадением только компонентных координат. Точное
  vertex-to-BRep сопоставление каждой вершины Board Outline не выполнялось.

- Следовательно, для исследованной группы подтверждена **XY-калибровка**:

  `X_SW = X_Altium - 212.6006 mm`
  `Y_SW = Y_Altium - 167.2006 mm`

  Это не полная 3D-матрица `T_B_S`: Z-origin, толщина/положение поверхностей,
  Bottom и полная ориентация остаются вне этого вывода. Матрицы из §5 валидны
  в импортированной SW assembly frame.

- `board-controls.step` имеет Board body толщину `2.008 mm`, а `board-all.step`
  — `2.028 mm`; расхождение `0.020 mm` не объяснено. Для калибровки толщины и
  Bottom Z использовать только `board-all.step`; `board-controls.step` не
  является единственным эталоном этих величин.

## 9. Model state

- Full board STEP includes Generic model state: C543/C522/C523 single-axis, and
  C75/C76 combined matrix `Rz(-60)·Ry(-45)·Rx(-30)`.
- Individual `model.step`/`body.step` exports remain local geometry; they do not
  include state, component rotation or bottom transform.
- `dz` is visible as Z translation in the full STEP: C75/C524 `+1.000 mm`,
  C76/C516 `-1.000 mm` (SW T values), consistent with `393700 IU/mm`.
- Raw `GetState` values 30/45/60 become **negative** angles in the SW matrix;
  sign convention of the SW frame must be applied consistently.

## 10. Сравнение individual и full STEP

| Object | Individual | Full board |
|---|---|---|
| Generic local geometry | 12×7×3 mm, origin lower-left | local B-Rep unchanged |
| Generic component rotation | absent | present for original vendor C22; lost for SDK-replaced controls |
| Generic model state | absent | present (C543/C522/C523/C75/C76) |
| Generic bottom | absent | Rx(180), z -2.028 mm (board-all) |
| Native local geometry | centered 12×7×3 mm | local B-Rep unchanged |
| Native bottom | absent | identity, z -5.008 mm (setup anomaly) |

## 11. Численные проверки

- Объёмы: Generic 95.2146 mm³, Native 146.999 mm³; отклонение native от
  аналитического 147 mm³ — 0.0006%.
- Bbox: Generic local 12×7×3; Native 12×7×3 centered.
- Board thickness: board-all 2.028 mm, board-controls 2.008 mm; difference
  0.020 mm, cause not established.
- Matrix determinant: all 23 +1.000000; no reflection matrix found.
- State matrix fit: max element error 4.3e-7 versus `Rz(-60)·Ry(-45)·Rx(-30)`.
- Component/assembly transform values are in meters; converted to mm above.

## 12. Ограничения

- Control bodies were installed via temporary SDK probe; component rotation was
  not applied to those synthetic bodies. This invalidates the control rotation
  matrix for C63/C59/C61 and Native controls, though C22 provides a vendor
  reference.
- Board-controls was produced via SDK exporter, not the final UI sequence, because
  Altium UI automation after selection was unstable. Board-all is the штатный UI
  export.
- XY origin shift relative to Altium PCB coordinates is calibrated for the
  researched control group and Board Outline extents (average `212.600587,
  167.200596 mm`, residual `≤0.000537 mm`); full 3D board matrix including Z and
  orientation remains unconfirmed.
- Native bottom setup is not a valid flipped-body sample; no general native
  bottom conclusion.
- Full Altium 3D Layout screenshots and visual characteristic-point comparison
  were not completed; SOLIDWORKS measurements are geometric/transform based.
- No production code, Core, package or SOLIDWORKS Add-in changes.

## 13. Локальные артефакты

`%TEMP%\EcadMcadBridge\BRIDGE-000012\`
- `test-matrix-export.PcbDoc`
- `board-all.step`, `board-controls.step`
- `individual/<Designator>/model.step`, `body.step`, `body.x_t`
- `sw-all-raw3.txt`, `sw-controls-raw.txt`, `sw-controls-raw2.txt`
- `screenshots/` (если сняты), `measurements/`, `logs/`
- `%TEMP%\EcadMcadBridge\logs\bridge.log`

В Git бинарные файлы не добавлены.

## 14. Проверки

- `git diff --check` — OK.
- `dotnet build -c Release` — 0 warnings/0 errors.
- `dotnet test -c Release` — Core 84/84, Altium 82/82, всего 166/166.
- Исходная PCB SHA до/после: `28908F70…` одинаков.
- Production code и тесты не изменялись.

## 15. Commit и PR

- Commit: `e171b00` (`docs(bridge-000012): add board STEP transform research`).
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/11, ветка
  `agent/BRIDGE-000012-board-step-reference`, base `source`, merge запрещён.
