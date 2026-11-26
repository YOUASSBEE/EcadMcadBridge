---
id: BRIDGE-000035
type: report
status: partial
from: local-agent
to: operator
created: 2026-11-27T00:16:11+03:00
updated: 2026-11-27T00:16:11+03:00
base: source
branch: task/BRIDGE-000035-top-only-smoke
commit: 9c1652319241489f271fddf174ec7fe4f3fe6611
pr: none (no PR created by this task)
related: [BRIDGE-000034, BRIDGE-000028, BRIDGE-000029, BRIDGE-000030, BRIDGE-000033]
---

# BRIDGE-000035 — Top-only identity contract, transform analysis и smoke readiness

## 1. Итог

- **Stage A (identity contract):** `passed` — формализован в
  `.agent/reports/BRIDGE-000035-identity-contract.md`; критерии приёмки зафиксированы до runtime.
- **Stage B (ModelOrigin/Top transform):** `passed` для поддерживаемого класса
  `Top/Top + Generic + Embedded + STEP AP214 + SdkExport + PlacedBodyEffective`; математика записана
  в разделе 3, но **новый runtime-эксперимент не выполнялся**.
- **Stage C (smoke readiness):** `passed` — воспроизводимый план, fixture, ожидаемые Transform’ы,
  12 независимых контрольных точек и численные допуски зафиксированы в разделах 4–5.
- **Stage D (authorization gate):** `passed` — запрос подготовлен в разделе 6; выполнение остановлено.
- **Stage E (runtime smoke):** **не выполнялся** — явного разрешения оператора нет.

**Итоговый статус задачи: `partial` относительно готовой интеграции.** Отсутствие разрешения не является
ошибкой задачи; production Bottom остаётся OFF, D-024 не активирован, V-16 остаётся `unverified`.

Base: `source` @ `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`. PR #25/#26/#27 не включались, не мержились.
В Git добавлены только два Markdown-отчёта; production code не менялся.

## 2. Identity acceptance contract (кратко)

Полный контракт — `.agent/reports/BRIDGE-000035-identity-contract.md`. Ключевое решение:

- Top-only **не требует** library footprint/body identity, если доступны `ModelOrigin.X/Y` из placed body
  и все category A поля.
- `ComponentInstance.Id — внутрипакетный join key`; per-instance `UniqueID`, `SourceUniqueId`, designator,
  Pattern, filename, checksum, `PackageSpecificHash` **не являются** library identity.
- `footprintSourceUniqueId`, `bodyUniqueId`, Item/Vault GUID, `GetChecksum()` остаются blocked/unproven
  и не подменяются; D-024 остаётся contract-only.
- Категория A (обязательная): `ComponentInstance.Id`, `ComponentSide=Top`, позиция/rotation,
  `BodyProjection=Top`, `ModelOrigin.X/Y`, полный model state, `ModelRotationDegRaw=0`,
  `StandoffHeightMm`, Generic/Embedded/StepAp214/SdkExport/PlacedBodyEffective, `BodyOrdinal`,
  STEP asset reference.
- Категория B: `sourceUniqueId`, raw `UniqueID`, designator, Pattern, filename, diagnostic enums.
- Категория C (запрещено как library identity): library ID кандидаты, Instance ID, checksum-кандидаты,
  Pattern/filename, bbox corner, `e≈0` как calibration.

Принятые criteria: любое отсутствующее/неизвестное category A поле → `Unsupported`, zero/identity/reflection
fallback запрещён; category B может быть `null` как диагностика; category C не повышается статусом.

## 3. ModelOrigin и Top transform

### 3.1 Система координат и единицы

- `IPCB_ModelHelper.GetOrigin()` возвращает **абсолютные координаты платы X/Y в IU**; Z в контракте
  SDK 4.0.1 отсутствует (`SDK_API_LEDGER.md` C-98, `BRIDGE-000028` §1/§3.4).
- `1 IU = 0.00000254 mm`; orientation board frame: X вправо, Y вверх, CCW rotation, градусы
  (`DECISIONS.md:210-216`, `ARCHITECTURE.md:177-193`).
- Placement board frame — те же оси, но XY origin смещён в минимум аналитического Outer-контура;
  cutout’ы и fallback `(0,0)` не участвуют (`ARCHITECTURE.md:704-709`, `BRIDGE-000018` §6).
- Substrate Z: Z=0 на верхней поверхности, положительный Z наружу от Top; assembly board transform
  `Tz = Thickness/2 − measuredTopZ` (`BRIDGE-000019` §8). R3 package не содержит `board-3d-model`,
  поэтому новый smoke проверяет component landmarks, а не substrate geometry gate.

### 3.2 Математическое описание

Пусть `O=(Ox,Oy)` — `ModelOrigin` в mm, `C=(Cx,Cy)` — позиция компонента, `θ` — component rotation,
`B=(minX(Outer),minY(Outer))`, `P=C−B`, `d_board=O−C`, `d_local=Rz(−θ)·d_board`, `T` — только
`BoardSnapshot.Properties.Thickness`, `s=StandoffHeight`, `a=AssetOriginLocal=0`,
`q=(rx,ry,rz,dz)` — нормализованный model state.

```text
rz = rawRz − θ                 // только PlacedBodyEffective
rx = rawRx
ry = rawRy
dz = rawDz

R_state = Rz(rz) · Ry(ry) · Rx(rx)
M_base  = T(Px+d_local.x, Py+d_local.y, +T/2+s) · T(0,0,dz) · R_state · T(−a)
M       = T(Px,Py,0) · Rz(θ) · T(−Px,−Py,0) · M_base
```

Эквивалентно: `M = T(O−B, +T/2+s) · Rz(θ) · Tz(dz) · R_state · T(−a)`. Это означает, что pivot модели
оказывается в `O−B`; поэтому в Core передаётся `d_local`, а не `d_board`.

SOLIDWORKS получает матрицу как row-major `Rᵀ` в индексах `0..8`, translation в метрах в `9..11`,
`1` в `12`, нули в `13..15` (`SolidWorksTransformConverter.cs:11-15,29-45`). Readback идёт через
`SetTransformAndSolve3` → `GetTotalTransform(false)`.

### 3.3 Что доказано и что не доказано

- **[статическая семантика + runtime]** `GetOrigin()` — абсолютный board XY; гипотеза model-local давала
  ошибку `9.497 mm` (`BRIDGE-000028` §3.5); bbox-proxy даёт `+0.524002 / +0.499999 / +1.25000004 mm`
  и не является `ModelOrigin`.
- **[runtime, реальная плата]** BRIDGE-000018: MiniPC `20/20`, R3 `4/4`, translation residual max
  `2.8421709430404E-14 mm`, rotation basis `0`, determinant `1`, save/reopen и cleanup `Verified`.
- **[runtime, реальная плата]** BRIDGE-000019/000020: board `21/21`, XY residual `5.2e-07 mm`, volume
  residual `-1.6064e-06 mm³`, `outlineCompleteness=Complete`, cleanup `Verified`.
- **[read-only STEP research]** BRIDGE-000029: 15 placement origins max `0.000004577 mm`, 18 landmark
  точек max `0.000004610 mm` ≤ `0.001 mm`. Это подтверждает математику и достижимость допуска, **но не
  заменяет новый smoke** и не создаёт D-024 calibration.
- **[не доказано]** скрытый Z-offset в `GetOrigin()`, стабильность library ID, кросс-сессионная ECO
  stability, Bottom sign-flip в production. V-16 остаётся `unverified`: STA/thread-2 — runtime fact,
  но не SDK 4.0.1 contract.

Запрещённые допущения: Z из `GetOrigin()` или `0`; сложение `Rz(component)` и placed-body `rotZ`;
подмена `GetRotation()` вместо `GetState.rotZ`; вывод `T` из STEP/nominal/default; `(0,0)` fallback для
board origin; bbox corner как `ModelOrigin`; `e≈0` как calibration; library ID подстановки.

## 4. Smoke readiness plan

### 4.1 Fixture и package

Primary fixture — R3 control, самый маленький уже проверенный Top-only набор:

| Объект | Значение |
|---|---|
| Fixture PCB | `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000018\r3-control.PcbDoc` |
| SHA-256 fixture | `69C62604A25972BDE59C65A98866793BC2D8674C211735748728E4E4A7AC3D67` |
| Package | `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000018\packages-r3\39c0d4a0-935d-491d-bd38-61c6b4152e6c` |
| Package ID | `39c0d4a0-935d-491d-bd38-61c6b4152e6c` |
| `checksums.sha256` | `EF5A5F82D97CF64F0FA9001D177D67EE6EC6C7788E2627F885AFF52A6FC421E7` |
| `manifest.json` | `20971DBB545EFFE2F39192B5D6B1E3B72BC06DAC664B3D98E56D05536BD17C3F` |
| `board.snapshot.json` | `D0D5C4712EC4225F8003246EE17F4B4412A2B3A2EF5C1BDC0419C35D4AB10B4A` |
| `models/models.json` | `FF1BA5DFB1613247D6938055298838F3B3CA445746D8F3B92E9FD5CB624BB56D` |
| STEP asset | `e5f44d66d7124349c5b86934288e0bf4ac6cf80352b12c9154f3127d435477d4.step`, SHA `E5F44D66D7124349C5B86934288E0BF4AC6CF80352B12C9154F3127D435477D4` |

Source SHA в manifest совпадает с SHA-256 fixture. PackageCapabilities не содержат `board-3d-model`;
поэтому substrate geometry gate в этом smoke **не проверяется**. Если оператор требует проверить
подложку, используется MiniPC final package `4a4fb429-c8b5-434f-8653-fd60de329fd6`, но там есть
нестандартные Top standoff’ы `B1/B2`; это отдельный сценарий, не primary plan.

### 4.2 Состав и ожидаемые результаты

```text
snapshot components:        1050
snapshot Top / Bottom:      372 / 678
eligible Top/Top bodies:    363
selected designators:       U101,U102,U103,U104
selected Top components:    4
selected unique STEP assets:1
native SLDPRT:              1
assembly Top occurrences:   4
Bottom occurrences:         0
reopened:                   4/4
cleanup:                    Verified
```

Все четыре выбранных тела: `Side=Top`, `BodyProjection=Top`,
`modelStateSource=PlacedBodyEffective`, `ModelRotationDegRaw=0`, standoff `0`, overall height
`2.99999908 mm`, board thickness `2.00800208 mm`, `Tz=+1.00400104 mm`.

| Designator | Snapshot XY, mm | Rot | ModelOrigin XY, mm | Local offset, mm |
|---|---:|---:|---:|---:|
| U101 | `(240.00000006, 219.99999942)` | 0 | `(235.68565766, 221.74044806)` | `(-4.31434240, +1.74044864)` |
| U102 | `(279.99999880, 219.99999942)` | 90 | `(278.25955016, 215.68565702)` | `(-4.31434240, +1.74044864)` |
| U103 | `(320.00000008, 219.99999942)` | −180 | `(324.31434248, 218.25955078)` | `(-4.31434240, +1.74044864)` |
| U104 | `(359.99999882, 219.99999942)` | −90 | `(361.74044746, 224.31434182)` | `(-4.31434240, +1.74044864)` |

Board-local origin: `(212.59999898, 167.19999834) mm`. Ожидаемые requested transforms в row-major
SOLIDWORKS `ArrayData`:

```text
U101 = [1, 0, 0, 23.08565868,  0, 1, 0, 54.54044972,  0, 0, 1, 1.00400104,  0, 0, 0, 1]
U102 = [6.123233995736766e-17, -1, 0, 65.65955118,  1, 6.123233995736766e-17, 0, 48.48565868,  0, 0, 1, 1.00400104,  0, 0, 0, 1]
U103 = [-1, 1.2246467991473532e-16, 0, 111.7143435,  -1.2246467991473532e-16, -1, 0, 51.05955244,  0, 0, 1, 1.00400104,  0, 0, 0, 1]
U104 = [6.123233995736766e-17, 1, 0, 149.14044848,  -1, 6.123233995736766e-17, 0, 57.11434348,  0, 0, 1, 1.00400104,  0, 0, 0, 1]
```

### 4.3 Независимые геометрические контрольные точки

Три локальные точки выбраны из фактических `CARTESIAN_POINT` STEP asset (не из bbox-подстановки и не из
`GetOrigin()`), распределены по телу:

| Point | Локальная координата STEP, mm | Комментарий |
|---|---|---|
| L1 | `(12, 0, 1)` | экстремум +X, низкий Y |
| L2 | `(0, 7, 0)` | экстремум −X / +Y |
| L3 | `(10, 7, 3)` | +X / +Y / +Z |

Перед smoke точки должны быть перечитаны из **свежеэкспортированного STEP**; если локальные координаты
отличаются от таблицы, run останавливается и требуется отдельное решение, а не подгонка допуска.

Ожидаемые board-local координаты после применения transform (mm):

| Designator | L1 | L2 | L3 |
|---|---|---|---|
| U101 | `(35.08565868, 54.54044972, 2.00400104)` | `(23.08565868, 61.54044972, 1.00400104)` | `(33.08565868, 61.54044972, 4.00400104)` |
| U102 | `(65.65955118, 60.48565868, 2.00400104)` | `(58.65955118, 48.48565868, 1.00400104)` | `(58.65955118, 58.48565868, 4.00400104)` |
| U103 | `(99.71434350, 51.05955244, 2.00400104)` | `(111.71434350, 44.05955244, 1.00400104)` | `(101.71434350, 44.05955244, 4.00400104)` |
| U104 | `(149.14044848, 45.11434348, 2.00400104)` | `(156.14044848, 57.11434348, 1.00400104)` | `(156.14044848, 47.11434348, 4.00400104)` |

Итого **12 control points**, по 3 на каждый из 4 компонентов. Измерение — через imported native body и
`GetTotalTransform`; сравнение с ожидаемыми координатами выполняется независимо от requested matrix.

### 4.4 Способ экспорта и импорта

- **Altium:** текущий установленный build `EcadMcadBridge.dll` SHA
  `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A`; `.ins`/`.rcs` побайтово равны
  исходникам `source`; зарегистрированы только `ExportSmokeSnapshot` и `ExportBoardSnapshot`. Установка
  DLL и перезапуск Altium **не требуются**; перед smoke повторно проверить SHA и `BOOTSTRAP_COMMANDS`.
- Команда `EcadMcadBridge:ExportBoardSnapshot` выполняется на control copy fixture; проверки:
  `GetKind=PCB`, абсолютный путь, `GetModified=false` до/после, неизменный SHA-256 `.PcbDoc`.
- Package валидируется через `ExchangePackageWriter`/validator; source SHA в manifest должен равняться
  SHA-256 fixture. Повторный экспорт STEP может изменить asset SHA из-за header — это не доказательство identity и
  не library identity; требуются только внутренние checksums и совпадение геометрии control points.
- **SOLIDWORKS:** `dotnet run --project tools/EcadMcadBridge.SolidWorks.Smoke -c Release --`
  с `--placed-assembly`, `--package <fresh package>`, `--template <gost-assy.asmdot>`,
  `--output <temp>/BRIDGE-000035/smoke`, `--designators U101,U102,U103,U104`.
- Import: STEP AP214 asset → 1 native `.SLDPRT`; placement: 4 occurrence, save/reopen,
  `SetTransformAndSolve3` → `GetTotalTransform(false)`; cleanup `Verified`.

### 4.5 Подтверждение неизменности PCB

1. SHA-256 fixture до открытия: `69C62604…AC3D67`.
2. SHA-256 сразу после export и после закрытия/деактивации: должен совпадать.
3. `GetModified=false` до и после; `DXP.Utils` не сохраняет документ.
4. Любое расхождение → smoke `failed`, package не принимается.
5. Пользовательский `Dev_board.PcbDoc` не открывается, не сохраняется, SHA не меняется.

## 5. Численные критерии приёмки (фиксируются до smoke)

| Критерий | Порог | Доказательство |
|---|---|---|
| Выбранные Top components | ровно 4: U101–U104 | smoke JSON mapping |
| Bottom occurrences | 0 | smoke JSON diagnostics/count |
| Unique STEP asset / native SLDPRT | 1 / 1 | import plan + smoke JSON |
| Occurrences / reopened | 4 / 4 | smoke JSON |
| Translation residual на occurrence | ≤ `0.001 mm` по каждой оси | requested vs readback |
| Rotation basis residual | ≤ `1e-8` | requested vs readback |
| `|det−1|` | ≤ `1e-8` | requested vs readback |
| Landmark residual, каждая ось X/Y/Z | ≤ `0.001 mm` | 12 control points |
| Landmark Euclidean residual | ≤ `0.001 mm` | 12 control points |
| Save/reopen | `true`, `4/4`, transforms verified | smoke JSON |
| Board/source ownership gate | подтверждён; при отказе — abort | bridge log + handler result |
| Cleanup | `Verified`, `remainingDocuments=0` | smoke JSON |
| `baselinePreserved` | `true` | smoke JSON |
| Fixture SHA до/после | совпадает с `69C62604…AC3D67` | `Get-FileHash` |
| Package checksums | internal validation `true` | `checksums.sha256` |
| Отказ при неизвестном category A поле | обязателен | identity contract |
| V-16 | остаётся `unverified`, новые потоки не создаются | static review + ledger |

Пороги не изменяются после получения результата. Pre-existing residuals из BRIDGE-000018/000029
(`0`, `2.8421709430404E-14 mm`, `0.000004610 mm`) используются только как обоснование достижимости,
**не как результат нового smoke**.

## 6. Runtime authorization gate — запрос разрешения

Работа остановлена здесь. До явного разрешения оператора не выполняются install, restart, runtime smoke
и изменение регистрации расширения.

1. **Требуется ли установка DLL?** Нет. Используется уже установленный build SHA `7D1E21…453A`;
   требуется только повторная read-only проверка SHA и лога `BOOTSTRAP_COMMANDS`.
2. **Требуется ли контролируемый перезапуск Altium?** Нет. Текущий процесс уже загрузил исходный build с
   двумя export command; при расхождении — stop, а не перезапуск без отдельного разрешения.
3. **Будет ли открыт fixture PCB?** Да, только control copy
   `…\BRIDGE-000018\r3-control.PcbDoc`, SHA `69C62604…AC3D67`. Пользовательский PCB не открывается.
4. **Операции в Altium:** открыть копию; подтвердить `modified=false`; выполнить
   `EcadMcadBridge:ExportBoardSnapshot`; записать package и log; закрыть копию без сохранения; проверить SHA.
5. **Операции в SOLIDWORKS:** запустить smoke tool; STEP→SLDPRT import; Top-only placed assembly;
   save/reopen; readback transforms; landmark measurement; cleanup verification. Bottom не размещается.
6. **Создаваемые файлы:** только во временных каталогах `%TEMP%\EcadMcadBridge\BRIDGE-000035\...` —
   свежий package, `smoke-result.json`, `.SLDPRT`, `.SLDASM`, log-копии. В Git — только отчёты.
7. **Сохранение SHA-256:** фиксируются до/после fixture PCB, manifest, checksums, STEP asset, smoke JSON
   и assembly; сравнение до запуска и после завершения; расхождение → failed.
8. **Восстановление окружения:** исходные extension files/registry не трогаются; временные CAD-документы
   закрываются smoke tool; при наличии созданных документов — cleanup до `Verified`; исходный Altium
   процесс не перезапускается.
9. **Категорически запрещено:** install DLL, restart Altium, registry/`.ins`/`.rcs` правки, открытие или
   сохранение пользовательского PCB, Bottom placement/export to MCAD, D-024 активация, ослабление
   fail-closed gate, фоновые SDK API, обход ownership gate, подгонка tolerances после результата,
   добавление CAD-бинарников в Git.

**Дополнительный вопрос для оператора:** в существующем R3 package полный snapshot содержит 678 Bottom body,
но Bottom не попадает в SOLIDWORKS import/placement. Если требование «Bottom-компоненты не экспортировать»
понимается строго как отсутствие Bottom STEP-ассетов в package, нужен отдельный Top-only fixture без Bottom
компонентов; его подготовка потребует редактирования control copy в Altium и отдельного разрешения.

## 7. Прогресс

| # | Checkpoint | Статус | Счёт |
|---|---|---|---|
| 1 | Безопасное получение PCB data | `passed` по BRIDGE-000014/019/020 и исходному коду; при smoke перепроверяется | Yes |
| 2 | Доказанный минимальный identity-контракт Top-only | `passed` — formal contract в отдельном файле с criteria | **Yes** |
| 3 | Подтверждённая семантика ModelOrigin и Top transform | `partial` — математика и прошлые residual’ы есть, новый runtime experiment не проводился | No |
| 4 | STEP export + SOLIDWORKS import в текущем build | `partial` — plan готов, final-commit revalidation отсутствует | No |
| 5 | Независимая проверка совмещения | `partial` — 12 control points определены, измерение не выполнялось | No |

**Выполнено: 3/5. Осталось: 2.**

## 8. Блокеры и следующий шаг

- **Blocker 1:** нет явного разрешения на Stage E.
- **Blocker 2 (conditional):** требуется уточнить interpretation «Bottom не экспортировать» —
  только не размещать в MCAD или не включать в package.
- **Risk:** BRIDGE-000015 описывает COM/RPC нестабильность при долгих сессиях; smoke должен быть
  одноразовым, с cleanup и повторной проверкой SHAs.
- **Next step:** после разрешения выполнить раздел 6 в точном порядке, зафиксировать все SHAs и сделать
  отдельный BRIDGE-000035 runtime-result report; при успехе checkpoint’ы 3–5 закрываются →
  `Выполнено: 5/5. Осталось: 0.`

## 9. Safety verification

- GitHub/Git состояние читалось; создана только ветка `task/BRIDGE-000035-top-only-smoke` от source HEAD.
- PR #25/#26/#27 не мержились и не включались; новый PR не создан.
- Production code, catalog, D-024, fail-closed gate, SDK ledger и ADR не менялись.
- Не запускались CAD-приложения, не выполнялись install/restart, не открывались/не сохранялись PCB.
- Не использованы DelphiScript, Legacy API, reflection, `dynamic`, undocumented API/COM или background
  SDK calls.
- В Git добавляются только Markdown-отчёты; PCB/STEP/SLDPRT/SLDASM/DLL не добавлялись.
- V-16 остаётся `unverified`; library identity и Bottom production остаются вне scope.

## 10. Commit и review

- Content commit: `9c1652319241489f271fddf174ec7fe4f3fe6611`; metadata-only SHA update follows.
- Branch: `task/BRIDGE-000035-top-only-smoke`, base `source` @
  `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`.
- PR: none.
