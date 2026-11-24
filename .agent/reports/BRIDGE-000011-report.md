---
id: BRIDGE-000011
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-25T01:30:00+03:00
updated: 2026-11-25T01:30:00+03:00
base: source
branch: agent/BRIDGE-000011-controlled-transform-validation
commit: 2899a40
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/10
related: [BRIDGE-000010, V-27, V-29, V-30, V-33]
---

# BRIDGE-000011 — Controlled 3D Transform Validation

## 1. Итог

Проведён контролируемый эксперимент с двумя моделями:

- **Generic STEP**: синтетическая асимметричная деталь SOLIDWORKS 2026, STEP AP214,
  origin в нижнем углу основания.
- **Native Extruded**: L-профиль, созданный средствами Altium SDK, высота 3 mm.

На тестовой PCB (копия `MiniPC.PcbDoc`, вне Git) созданы/настроены 16 control
instances: Generic и Native × Top/Bottom × 0/90/180/270, плюс 5 Generic instances
с независимыми `GetState` (RX90, RY90, RZ90, combined ±1 mm) и два дополнительных
dz-контроля.

Подтверждено:

- component rotation **не входит** в `ExportToStep`/`SaveModelToFile` и не
  поворачивает body projection;
- Generic STEP и Native Extruded экспортируются как локальная B-Rep;
- `GetState` для Generic меняет body projection, но **не** меняет экспортированный
  STEP; `GetState` angles — градусы, `dz` — IU;
- `GetOrigin` для Generic control находится в component XY, для Native — в центре
  body bbox; Bottom-side не отражается ни в body projection, ни в STEP/X_T;
- STEP/X_T импортируются в SOLIDWORKS 2026 без ошибок; volume/bbox совпадают,
  но component placement/rotation/Bottom mirror в файлы не включены.

Не подтверждены до конца: порядок combined Euler rotations, полная матрица
Bottom reflection и знак component rotation на board. V-27/V-29/V-30 остаются
`Partially closed`/`Open`; V-33 `Partially closed`.

## 2. Стенд

| Параметр | Значение |
|---|---|
| Altium Designer | 26.12.2.9 |
| Altium SDK | 4.0.1 |
| SOLIDWORKS | 2026, `34.3.2.0003` |
| .NET SDK | 10.0.404 |
| Тестовая PCB | `%TEMP%\EcadMcadBridge\BRIDGE-000011\test-matrix.PcbDoc` |
| SHA-256 test PCB | `28908F70769C6ABEDE125C1B3F3A132520F2C1157C5A897699320FA306D5E5FD` |
| Исходный STEP | `%TEMP%\EcadMcadBridge\BRIDGE-000011\control-generic.step` |
| SHA-256 STEP | `E5F44D66D7124349C5B86934288E0BF4AC6CF80352B12C9154F3127D435477D4` |
| Исходная деталь | `control-generic.sldprt` (вне Git) |

Исходные пользовательские `.PcbDoc` не изменялись. Тестовая PCB создана копией
`MiniPC.PcbDoc`; vendor-проект не менялся.

## 3. Контрольные модели

### 3.1 Generic STEP

Геометрия:

- основание `12 × 7 × 1 mm`, нижняя грань `z=0`, origin `(0,0,0)` в нижнем углу;
- выступ `2 × 3 × 2 mm`: `x=8…10`, `y=4…7`, `z=1…3`;
- сквозное отверстие `Ø1 mm`: центр `(2,2)`, через основание `z=0…1`;
- деталь асимметрична по X/Y/Z; все координаты положительны.

Аналитический объём: `84 + 12 − π·0.5²·1 = 95.2146018 mm³`.
SOLIDWORKS: volume `9.521460183660255E-08 m³ = 95.2146018 mm³`, bbox
`0…0.012 × 0…0.007 × 0…0.003 m`.
STEP AP214, units mm.

### 3.2 Native Extruded

L-профиль Altium, точки в mm: `(0,0),(12,0),(12,2),(5,2),(5,7),(0,7)`;
высота `3 mm`. Площадь профиля `12·2 + 5·5 = 49 mm²`, объём `147 mm³`.
Altium `ModelFactory_CreateExtruded` создал модель типа Extruded; экспорт
`ExportToStep` показывает B-Rep с bbox `[-6,6] × [-3.5,3.5] × [0,3] mm`
(центрирован относительно model origin, см. ограничение ниже).

## 4. Control matrix

Тестовая PCB — копия `MiniPC.PcbDoc`; выбраны существующие компоненты с одним
body и заменены модели. Компоненты размещены в разных координатах; ниже указаны
исходные UID/designator, side и board rotation.

### 4.1 Generic matrix (state 0)

| ComponentId | Designator | Side | Component rot | Body layer | Model origin (IU X,Y) | Body bbox (IU) | `GetState` |
|---|---|---|---:|---:|---|---|---|
| `altium:component:CSQWQOOA` | C366 | Top | 0 | 69 | 88620709,77687191 | 0…4724409 × 0…2755906 | 0,0,0,0 |
| `altium:component:FFPUPAKY` | C63 | Top | 90 | 69 | 129633858,120921259 | 0…4724409 × 0…2755906 | 0,0,360,0 |
| `altium:component:XWMBTJJB` | C59 | Top | 180 | 69 | 143167421,110905412 | 0…4724409 × 0…2755906 | 0,0,360,0 |
| `altium:component:VOWGRRIU` | C61 | Top | 270 | 69 | 130484252,118787401 | 0…4724409 × 0…2755906 | 0,0,360,0 |
| `altium:component:EQHFYMFD` | C572 | Bottom | 0 | 70 | 120314960,77214567 | 0…4724409 × 0…2755906 | 0,0,0,0 |
| `altium:component:AFVBOKBM` | C573 | Bottom | 90 | 70 | 120984252,75482284 | 0…4724409 × 0…2755906 | 0,0,0,0 |
| `altium:component:DAAQGUFG` | C567 | Bottom | 180 | 70 | 119330708,73946850 | 0…4724409 × 0…2755906 | 0,0,0,0 |
| `altium:component:XGDBNSGA` | C571 | Bottom | 270 | 70 | 117559055,75206694 | 0…4724409 × 0…2755906 | 0,0,0,0 |

`Body bbox` в таблице — размеры относительно model origin; `0…4724409 IU = 0…12 mm`,
`0…2755906 IU = 0…7 mm`. Для всех восьми component rotations bbox остался
**локальным 12×7**, то есть component rotation не применяется в body projection.
`GetState_FlippedOnLayer` для Bottom — `false`; side определяется layer 32
(component) / body layer 70.

### 4.2 Native matrix (state 0)

| ComponentId | Designator | Side | Component rot | Body layer | Model origin (IU X,Y) | Body bbox (IU) | `GetState` |
|---|---|---|---:|---:|---|---|---|
| `altium:component:SNLMUCLA` | C563 | Top | 0 | 69 | 119133853,75875982 | −2362200…2362200 × −1377950…1377950 | 0,0,0,0 |
| `altium:component:LKNGDSVW` | C62 | Top | 90 | 69 | 132866137,122295272 | −2362200…2362200 × −1377950…1377950 | 0,0,0,0 |
| `altium:component:FOERFJPY` | B2 | Top | 180 | 69 | 148307082,78011808 | −2362200…2362200 × −1377950…1377950 | 0,0,0,0 |
| `altium:component:VVWRLSLA` | C18 | Top | 270 | 69 | 130751963,113751965 | −2362200…2362200 × −1377950…1377950 | 0,0,0,0 |
| `altium:component:UTNOUUSL` | C570 | Bottom | 0 | 70 | 121220467,78631887 | −2362200…2362200 × −1377950…1377950 | 0,0,0,0 |
| `altium:component:RSDIUJLD` | R240 | Bottom | 90 | 70 | 96771648,99291335 | −2362200…2362200 × −1377950…1377950 | 0,0,0,0 |
| `altium:component:WRXQJYIQ` | C345 | Bottom | 180 | 70 | 111673225,79645666 | −2362200…2362200 × −1377950…1377950 | 0,0,0,0 |
| `altium:component:DLCJQPNK` | C566 | Bottom | 270 | 70 | 119960625,75561021 | −2362200…2362200 × −1377950…1377950 | 0,0,0,0 |

`±2362200 IU = ±6 mm`, `±1377950 IU = ±3.5 mm`; точный bbox из
Altium для L-профиля: `12×7×3 mm` с центром в model origin. Native model origin
оказался **центром bbox**, а не нижним углом профиля (ограничение SDK factory).

### 4.3 Generic model state variants

| Instance | Component | Component rot | `GetState` | Body bbox (mm) | Standoff / overall (IU) |
|---|---|---:|---|---|---|
| G-RX90 | C543 | 0 | 90,0,0,0 | 12 × 3 | 0 / 2755906 |
| G-RY90 | C522 | 0 | 0,90,0,0 | 3 × 7 | −4724408 / 0 |
| G-RZ90 | C523 | 0 | 0,0,90,0 | 7 × 12 | 0 / 1181102 |
| G-XYZ | C75 | 0 | 30,45,60,393700 | ≈12.948 × 14.864 | −2946961 / 2091334 |
| G-XYZ-NEG | C76 | 0 | 30,45,60,−393700 | ≈12.948 × 14.864 | −3734361 / 1303934 |
| G-XYZ rot180 +1 | C524 | 180 | 30,45,60,393700 | ≈12.948 × 14.864 | −2946961 / 2091334 |
| G-XYZ rot180 −1 | C516 | 180 | 30,45,60,−393700 | ≈12.948 × 14.864 | −3734361 / 1303934 |

Dz control (state 0, до замены combined):
- `dz=+393700` → standoff `+393700 IU = +1.000 mm`, overall `1574802 IU = 4.000 mm`;
- `dz=−393700` → standoff `−393700 IU = −1.000 mm`, overall `787402 IU = 2.000 mm`;
- `dz=0` → standoff `0`, overall `1181102 IU = 3.000 mm`.

Это подтверждает `dz` в IU (393700 IU/mm) для state 0 и сдвиг Z.
Для combined rotations standoff/overall — уже трансформированные extents; порядок
Euler не подтверждён.

## 5. `GetState`, `GetRotation`, `GetOrigin`

- `GetState(out rotX,out rotY,out rotZ,out dz)` для Generic: значения в градусах
  (single-axis 90° меняет body projection как X/Y/Z-поворот), `dz` — IU.
- `GetRotation()` на всех проверенных моделях вернул `0` и не совпал с `GetState`
  rotZ (C63/C543 и др.); использовать `GetRotation` для 3D-ориентации нельзя.
- `GetOrigin()`:
  - Generic control: component X/Y (проверено на всех 8 + state instances);
  - Native control: центр body bbox/модели, не нижний угол профиля.
- Body projection Generic после `SetModel`/reload: локальный bbox 12×7, origin =
  component; body projection Native: центрированный L-bbox.
- Component rotation 0/90/180/270 не изменяет экспортированный STEP и body bbox
  размеры; это отдельное преобразование C→B.
- `GetState_FlippedOnLayer()` для Bottom layer 32 вернул `false`; side нужно
  брать из layer ID, не из flip-флага.

## 6. Матрицы преобразования

Соглашение: right-handed, 4×4, столбцы-векторы, `T_A_B` переводит B→A.
`O` — board origin модели, `Rz(θc)` — поворот компонента вокруг board Z,
`R_s` — model state rotation, `S` — локальная STEP-геометрия, `dz` — Z-сдвиг в IU,
`1/393700` mm/IU.

### 6.1 Generic, state = 0 (подтверждено)

```text
T_B_S = T(O_G) · Rz(θc) · S_G
```

- `S_G`: bbox `0…12 × 0…7 × 0…3 mm`, origin в нижнем углу.
- `O_G`: component X/Y (из `GetOrigin`).
- `θc`: component rotation; в экспортированном файле отсутствует, должна быть
  применена потребителем.
- `Rz` знак принят right-handed CCW; независимо визуально не проверен (V-27).

Подтверждение: все 8 Generic instances Top/Bottom × 0/90/180/270 дали
`ExportToStep`/`SaveModelToFile` с bbox 12×7×3 и volume 95.2146 mm³; component
rotation не меняет файл/body bbox.

### 6.2 Generic, state ≠ 0 (частично)

```text
T_B_S ≈ T(O_G) · Rz(θc) · R_s(rx,ry,rz) · T_z(dz) · S_G
```

- `R_s` для одиночных 90° подтверждён по body projection (12×3, 3×7, 7×12).
- `T_z(dz)` подтверждён при state 0 (`dz` = IU).
- Порядок combined X/Y/Z для 30/45/60 **не подтверждён**: body projection
  (4-точечный контур ~12.948×14.864 mm) не совпал с простыми right-handed
  Euler-порядками; STEP-файл состояния не содержит (все экспортируемые файлы
  остались 12×7×3), поэтому matrix state нужно применять отдельно.

### 6.3 Native Extruded, state = 0 (подтверждено)

```text
T_B_S = T(O_N) · Rz(θc) · S_N_centered
```

- `S_N_centered`: `[-6,6] × [-3.5,3.5] × [0,3] mm` (STEP/X_T после
  `ExportToStep`/`SaveModelToFile`).
- `O_N`: центр bbox, полученный `GetOrigin`.
- `θc` не входит в файл/body; применять отдельно.
- `GetState=0`, standoff 0, overall 3 mm; dz 0.

Подтверждение: 8 Native instances Top/Bottom × 0/90/180/270, все
`model.step`/`body.step`/`body.x_t` дали bbox 12×7×3 и volume 146.999 mm³.

### 6.4 Bottom-side

Bottom-компоненты (layer 32) имеют `GetState_FlippedOnLayer=false`; body projection
и STEP/X_T совпадают с Top (no reflection); `GetOrigin` не меняет знак. Следовательно:

- экспорт **не содержит** Bottom reflection;
- reflection должен выполняться MCAD-импортером;
- ось/матрица отражения не доказана (кандидат — `diag(1,1,-1)` в board Z, но это
  гипотеза, не результат). V-30 остаётся `Open`.

## 7. Экспорт и SOLIDWORKS 2026

### 7.1 Файлы

Для representative instances экспортированы `model.step`, `body.step`,
`body.x_t`; каталоги `%TEMP%\EcadMcadBridge\BRIDGE-000011\matrix\<Designator>\`.
Generic `body.step` совпал по размеру/содержанию с исходным control STEP
(27 834 B, SHA `E5F44D66…`), `body.x_t` — Parasolid. Native `body.step`/X_T —
сгенерированные B-Rep.

| File | Size | SHA-256 |
|---|---:|---|
| `C366/model.step` | 16 357 | `2AA407F8B46AC006EBBE4573B9F479365DE787AC48F863E823125996C3F717AD` |
| `C366/body.step` | 27 834 | `E5F44D66D7124349C5B86934288E0BF4AC6CF80352B12C9154F3127D435477D4` |
| `C366/body.x_t` | 10 481 | `3B7E7CE2F9F023C1351848C49E14477F776C45058CD4919A2B7C76DAF0B5D996` |
| `C572/model.step` | 16 357 | `ED12F32369B6996661CC8EDE211720ABA2C8CFFAEAF410743582F7FE8BA60C2C` |
| `C63/model.step` | 16 357 | `F517D4D5478E7233C998D4CFC578EA3B6C791F52F61D9911A4F83F3E1BA6A5E7` |
| `C524/model.step` | 16 358 | `6D3AD7A41037C22C5343BC916E150CB74E2525996AEC50A47BFB1C65FB08D8D6` |
| `C563/model.step` | 13 457 | `B02DD5EC16ABDB2A2AACB11474130761B84E3DA5241339BB7DD2EA10981948CC` |
| `C563/body.step` | 14 449 | `B248B22B0E2BAD1D9CBD73A77C6DADF3FF4A618F30E95098E0238338A54D9C36` |
| `C563/body.x_t` | 7 417 | `B156A7A4464E5F5D66ED87DE881E823A9CA99ECCE2CEBF4B792ADFAD369CCB20` |
| `C570/model.step` | 13 460 | `F3975CB2B7AEB4C3FCD96B525D07D2D551AA0F453D32B27A78EF58AD25243F95` |

SHA сгенерированных STEP может меняться между запусками из-за header timestamp.

### 7.2 Импорт

SOLIDWORKS `LoadFile4` импортировал STEP как wrapper assembly с одним solid part;
X_T аналогично. Измерения на вложенном теле:

| Файл | Volume | BBOX (m) | Результат |
|---|---:|---|---|
| Generic C366 `model.step` | 9.52146018366e-8 m³ = 95.2146 mm³ | 0…0.012 × 0…0.007 × 0…0.003 | OK |
| Generic C366 `body.step` | 95.2146 mm³ | 12×7×3 mm | OK |
| Generic C366 `body.x_t` | 95.2146 mm³ | 12×7×3 mm | OK |
| Generic C572 Bottom `model.step` | 95.2146 mm³ | 12×7×3 mm | OK; no mirror |
| Generic C63 rot90 `model.step` | 95.2146 mm³ | 12×7×3 mm | OK; no component rotation |
| Generic C524 combined `model.step` | 95.2146 mm³ | 12×7×3 mm | OK; state not baked |
| Native C563 `model.step` | 1.46999118e-7 m³ = 146.999 mm³ | −0.006…0.006 × −0.0035…0.0035 × 0…0.003 | OK |
| Native C563 `body.step` | 146.999 mm³ | 12×7×3 mm, centered | OK |
| Native C563 `body.x_t` | 146.999 mm³ | 12×7×3 mm, centered | OK |
| Native C570 Bottom `model.step` | 146.999 mm³ | 12×7×3 mm, centered | OK; no mirror |

Все импорты: errors=0, warnings=0; одна solid body. Отличие native объёма от
аналитического 147 mm³ — 0.0006%, в допуске 0.1%. Generic — в допуске.

### 7.3 Что это доказывает

- STEP и X_T сохраняют локальную B-Rep; component placement, component rotation и
  Bottom mirror в файлы не входят.
- Generic origin — нижний угол; Native origin — центр bbox.
- Модель нельзя размещать в сборке только по файлу: нужны transform metadata из
  Altium (`GetOrigin`, `GetState`, component placement, side).
- Успешный импорт не доказывает правильное положение на плате; он проверяет только
  геометрию.

## 8. Ограничения и открытые вопросы

1. **V-27** — component rotation не входит в экспорт/body projection, но знак
   `Rz` и combined matrix для Generic не подтверждены визуально. `Partially closed`.
2. **V-29** — units single-axis и `dz` подтверждены; combined Euler order не
   подтверждён. `Partially closed`.
3. **V-30** — Bottom reflection не реализован/не найден; `GetFlippedOnLayer=false`;
   `Open`.
4. **V-33** — импорт/объём/bbox подтверждены; полная проверка характерных элементов
   (выступ/отверстие) и положения на плате не выполнена; `Partially closed`.
5. V-26 (linked) и V-32 (побайтовая идентичность) не менялись и остаются открытыми.
6. Тестовая PCB — модифицированная копия MiniPC, не полностью изолированный
   synthetically-built board; выбранные control components имеют разные исходные
   координаты, но остальные vendor components не удалялись.
7. Native Extruded `ModelFactory_CreateExtruded` центрирует bbox; origin профиля
   `(0,0)` не сохраняется как origin модели.
8. Generic combined state body projection — 4-точечный контур, который не удалось
   однозначно сопоставить с простым Euler-порядком; это ограничение API/проекции,
   не доказательство ошибки.
9. Скриншоты SOLIDWORKS 3D-видов сохранены вне Git:
   `%TEMP%\EcadMcadBridge\BRIDGE-000011\sw-generic-top.bmp` и
   `sw-native-top.bmp`; измерения зафиксированы через официальный API и текстовый
   вывод. Локальные артефакты находятся в `%TEMP%`.

## 9. Проверки

- Build: `dotnet build -c Release` — 0 warnings / 0 errors.
- Tests: `dotnet test -c Release` — Core 84/84, Altium 82/82, всего 166/166.
- Source/test code после удаления probe не изменялся.
- SHA-256 исходных пользовательских PCB не изменялись (vendor-проект не
  использовался для записи).
- Тестовая PCB SHA: `28908F70769C6ABEDE125C1B3F3A132520F2C1157C5A897699320FA306D5E5FD`.
- STEP control SHA: `E5F44D66D7124349C5B86934288E0BF4AC6CF80352B12C9154F3127D435477D4`.

## 10. Commit и PR

- Commit: `2899a40` (`docs(bridge-000011): add controlled 3D transform research`).
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/10, ветка
  `agent/BRIDGE-000011-controlled-transform-validation`, base `source`;
  merge запрещён.

## 11. Итоговая классификация вопросов

| Вопрос | Статус | Основание |
|---|---|---|
| V-27 | Partially closed | component rotation отсутствует в export/body; единая combined matrix не доказана |
| V-29 | Partially closed | 90° axes — градусы; `dz` IU/±1 mm; combined order открыт |
| V-30 | Open | Bottom files/projection не отражены; `GetFlippedOnLayer=false`; ось не доказана |
| V-33 | Partially closed | SOLIDWORKS import, volume/bbox STEP/X_T подтверждены; placement/features не полностью |

## 12. Локальные артефакты

```text
%TEMP%\EcadMcadBridge\BRIDGE-000011\
  control-generic.step
  control-generic.sldprt
  test-matrix.PcbDoc
  matrix\<Designator>\model.step|body.step|body.x_t
  matrix-read.txt
  probe logs: %TEMP%\EcadMcadBridge\logs\bridge.log
```

Ни один бинарный файл не добавлен в Git.
