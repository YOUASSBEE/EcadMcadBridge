---
id: BRIDGE-000035-runtime-result
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-27T00:34:00+03:00
updated: 2026-11-27T00:34:00+03:00
base: source
branch: task/BRIDGE-000035-top-only-smoke
commit: pending
pr: none (no PR created by this task)
related: [BRIDGE-000035, BRIDGE-000018, BRIDGE-000028, BRIDGE-000029]
---

# BRIDGE-000035 — controlled Top-only runtime smoke (Stage E)

## 1. Итог

**Stage E: `passed`.** Разрешённый оператором runtime smoke выполнен на control copy R3 без установки DLL,
перезапуска Altium, правки реестра, сохранения PCB, открытия пользовательской PCB и добавления CAD-бинарников
в Git.

- Экспортирован и проверен свежий package `50da0c77-666a-435a-b120-c3fe5026203d`.
- В SOLIDWORKS импортирован ровно 1 уникальный STEP-ассет и размещены ровно 4 Top-occurrence:
  `U101,U102,U103,U104`. Bottom-occurrence = 0.
- Save/reopen: `4/4` компонентов, `transformsVerified=true`; board occurrence отдельно `1/1`, `fixedFlag=true`.
- Независимые 12 контрольных точек STEP: максимальный residual по осям и Euclidean = `0 mm`
  при fixture-допуске `≤0.001 mm`.
- Translation/rotation residual на каждой occurrence = `0`, `|det−1|=0`.
- Cleanup: `Verified`, `RemainingCreatedDocuments=0`, `baselinePreserved=true`.
- SHA-256 control PCB до и после: `69C62604A25972BDE59C65A98866793BC2D8674C211735748728E4E4A7AC3D67` — без изменений.
- Копия package в default root (`%LOCALAPPDATA%\EcadMcadBridge\Packages`) удалена после проверки;
  рабочая копия остаётся только в `%TEMP%\EcadMcadBridge\BRIDGE-000035\`.

PR **не создан**: требуется отдельное решение оператора. В Git добавлен только этот Markdown-отчёт и обновление
основного отчёта BRIDGE-000035.

## 2. Изменённые файлы

| Файл | Изменение |
|---|---|
| `.agent/reports/BRIDGE-000035-runtime-result.md` | новый отчёт с доказательствами Stage E |
| `.agent/reports/BRIDGE-000035-report.md` | статус Stage E, прогресс 5/5, снятые блокеры, safety-проверки |

Production code, тесты, SDK ledger, DECISIONS, D-024, `.ins`/`.rcs` и каталоги не изменялись.

## 3. Среда и authorization gate

| Проверка | Результат |
|---|---|
| Altium Designer / SDK | AD26, SDK 4.0.1 (согласованный стек без изменений) |
| Установленный `EcadMcadBridge.dll` | SHA `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A`, совпадает с source |
| `BOOTSTRAP_COMMANDS` | `ExportSmokeSnapshot,ExportBoardSnapshot` (последняя запись `2026-11-26T20:34:09Z`) |
| Install / registry / restart Altium | не выполнялись |
| SOLIDWORKS | `34.3.2`, `sw2026_SP03/d260702.008/`, SOLIDWORKS Design Premium 2026 SP3.2 |
| .NET SDK | `10.0.404` |
| Шаблон сборки | `C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gost-assy.asmdot` |
| Ветка | `task/BRIDGE-000035-top-only-smoke` от `source` @ `ea817c9b1e52b6a86e5eb541cf814a578887a7d5` |

Лог экспорта подтверждает ownership/source gate:

```text
2026-11-26T21:24:54Z INFO SOURCE_STATE doc=r3-control.PcbDoc kind=PCB modified=false pathRooted=true
2026-11-26T21:27:13Z INFO PACKAGE_EXPORT_OK addin=0.4.0 cad=26 doc=r3-control.PcbDoc packageId=50da0c77-666a-435a-b120-c3fe5026203d ...
2026-11-26T21:27:13Z INFO DURATION_MS 139122
```

Открывался **только** `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000018\r3-control.PcbDoc`.
Пользовательский `D:\Git\Projects\mdm15-1v05m_electric\Dev_board.PcbDoc` не открывался и не сохранялся;
после smoke его SHA-256 `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F`.
После закрытия control copy активным стал `Feedback.SchDoc`, `modified=false`; Altium не перезапускался.

## 4. Экспорт и валидация package

Команда Altium: `EcadMcadBridge:ExportBoardSnapshot`, параметры пустые. Новый package в default root был
обнаружен по diff с `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000035\packages-before.txt`, скопирован
в `%TEMP%\EcadMcadBridge\BRIDGE-000035\packages\50da0c77-666a-435a-b120-c3fe5026203d` и там проверен.

| Объект | Значение |
|---|---|
| Package ID | `50da0c77-666a-435a-b120-c3fe5026203d` |
| `manifest.json` source | `cad=altium`, `documentSha256=69c62604a25972bde59c65a98866793bc2d8674c211735748728e4e4a7ac3d67` |
| Source SHA == fixture SHA | `true` |
| Capabilities | `board-3d-model`, `board-outline`, `component-3d-models`, `component-placement`, `layers`, `nets`, `pad-stacks`, `pads`, `regions`, `texts`, `tracks`, `vias` |
| Internal checksums | `checked=168`, `bad=0` |
| Файлы / размер package | 169 / 401,371,207 bytes |
| Source root vs temp copy | `diffCount=0` по SHA-256 всех 169 файлов |

SHA-256 ключевых файлов свежей копии:

| Файл | SHA-256 |
|---|---|
| `manifest.json` | `451803D9B6F417A7274BE09A870050CF05CC1E247383C4948BBBF09E5DC26197` |
| `checksums.sha256` | `0CB8DDFBBC74F8E5106E2F81E7EE942ACA906C892893451FAAE69010CCFA10DB` |
| `board.snapshot.json` | `193E749875D2B12C90E3745A9796D6CE18C7DCFE5C7F59F4DA08D9BCDD1AFD5E` |
| `board.model.json` | `08F2CD659CE871F0F11EDE044F94194F0349E3EDCAE1B102A75623AA9D93CF30` |
| `models/models.json` | `8F86BD9E17EA44ED41E33C495FEF9BF881A55C284590A97E42E53B2AF080B055` |
| `assets/3d/e5f44d…d4.step` | `E5F44D66D7124349C5B86934288E0BF4AC6CF80352B12C9154F3127D435477D4` |
| `assets/board/315093…a17b.step` | `31509371894AA6E45BCF48050C02659129C7E59BB4A55D35FB86CB0416F8A17B` |

Отклонение от подготовительного плана: свежий package содержит capability `board-3d-model`, которого не было в
старом package `39c0d4a0-…`. Это не ослабило gate: board STEP был импортирован и объектно проверен (раздел 6).

## 5. SOLIDWORKS Top-only smoke

Команда (SOLIDWORKS была запущена локально для ROT smoke, PID `28192`, и закрыта после завершения):

```text
dotnet run --project tools/EcadMcadBridge.SolidWorks.Smoke -c Release -- \
  --placed-assembly \
  --package C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000035\packages\50da0c77-666a-435a-b120-c3fe5026203d \
  --template C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gost-assy.asmdot \
  --output C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000035\smoke \
  --designators U101,U102,U103,U104
```

Вывод smoke:

```text
mode=placed
packageId=50da0c77-666a-435a-b120-c3fe5026203d
assets=1 imported=1
bodyReferences=19 mapped=19
boardBodies=1 boardBounds=175.00000000000003x155x2.0080020800000002 boardVolume=54097.58403728001 geometryValid=True
revision=34.3.2 build=sw2026_SP03/d260702.008/
```

Счётчики из `smoke-result.json`:

| Поле | Значение | Интерпретация |
|---|---:|---|
| `success` | `true` | общий smoke gate |
| `packageComponents` / `catalogComponents` | `1050 / 1050` | полный snapshot платы; не число размещений |
| `selectedUniqueAssets` / `assets` / `importedAssets` / `nativeParts` | `1 / 1 / 1 / 1` | один уникальный STEP → один SLDPRT |
| `bodyParts` / `mappedBodyParts` | `19 / 19` | все body reference, использующие этот asset; не placements |
| `supportedBodyReferences` / `unsupportedBodyReferences` | `363 / 696` | Bottom/Non-Generic не были выбраны для placement |
| `placement.requests` | `4` | ровно `U101,U102,U103,U104` |
| `placement.selectedDesignators` | `U101 U102 U103 U104` | `missingDesignators` пусто |
| `placedAssembly.requestedOccurrences` | `5` | 4 компонента + 1 board occurrence |
| `placedAssembly.occurrences` | `4` | только U101–U104 |
| Bottom occurrences | `0` | ни одного Bottom-дизайнера в `occurrences` |
| `saved` / `reopened` / `transformsVerified` | `true / true / true` | assembly сохранён и переоткрыт |

`bodyParts=19` включает также конденсаторы, разделяющие тот же STEP-ассет; в assembly попали только четыре
запрошенных designator. Board — отдельный зафиксированный occurrence `board-1`, не component placement.

Диагностика: `697` записей, из них `696` severity 1 (`688 × SW-PLC-006 Only Top component side is supported`,
`8 × SW-PLC-008 Only Generic model representation is supported`) и `1` severity 0
(`SW-IMP-019 Assembly-as-part preference restored to 1`). Severity ≥ 2 отсутствует; диагностика относится к
невыбранным reference и не меняет Top-only результат.

### 5.1 Readback transform’ы

Translations в mm, row-major matrices совпадают с ожидаемыми из основного отчёта.

| Designator | Requested XYZ, mm | Readback XYZ, mm | Translation residual | Rotation basis residual | `det` | Verified |
|---|---|---|---:|---:|---:|---|
| U101 | `(23.08565868, 54.54044972, 1.00400104)` | `(23.08565868, 54.54044972, 1.00400104)` | `0` | `0` | `1` | `true` |
| U102 | `(65.65955118, 48.48565868, 1.00400104)` | `(65.65955118, 48.48565868, 1.00400104)` | `0` | `0` | `1` | `true` |
| U103 | `(111.71434350, 51.05955244, 1.00400104)` | `(111.71434350, 51.05955244, 1.00400104)` | `0` | `0` | `1` | `true` |
| U104 | `(149.14044848, 57.11434348, 1.00400104)` | `(149.14044848, 57.11434348, 1.00400104)` | `0` | `0` | `1` | `true` |

Все четыре request имели `Side=Top`, `BodyProjection=Top`, `ModelStateSource=PlacedBodyEffective`,
`ModelRotationDegRaw=0`, `StandoffHeightMm=0`, `OverallHeightMm=2.99999908`. Запрещённые подстановки
(Pattern, filename, checksum, `PackageSpecificHash`, per-instance `UniqueID`, bbox corner, `e≈0`) не
использовались.

## 6. Независимые измерения STEP-геометрии

Локальные контрольные точки перечитаны из **свежеэкспортированного** STEP-ассета, а не из bbox или `GetOrigin()`:

| Point | Локальная координата, mm | Подтверждение в свежем STEP |
|---|---|---|
| L1 | `(12, 0, 1)` | `#99`, `#196` (line 114, 211) |
| L2 | `(0, 7, 0)` | `#202`, `#254` (line 217, 269) |
| L3 | `(10, 7, 3)` | `#117`, `#209` (line 132, 224) |

Координаты получены применением **readback** `GetTotalTransform(false)` к локальным точкам и сравнением с
зафиксированными до smoke ожидаемыми значениями. Допуск `≤0.001 mm` по каждой оси и Euclidean не менялся.

| Designator | Point | Actual XYZ, mm | Expected XYZ, mm | Axis max | Euclidean |
|---|---|---|---|---:|---:|
| U101 | L1 | `(35.085658680, 54.540449720, 2.004001040)` | `(35.08565868, 54.54044972, 2.00400104)` | `0` | `0` |
| U101 | L2 | `(23.085658680, 61.540449720, 1.004001040)` | `(23.08565868, 61.54044972, 1.00400104)` | `0` | `0` |
| U101 | L3 | `(33.085658680, 61.540449720, 4.004001040)` | `(33.08565868, 61.54044972, 4.00400104)` | `0` | `0` |
| U102 | L1 | `(65.659551180, 60.485658680, 2.004001040)` | `(65.65955118, 60.48565868, 2.00400104)` | `0` | `0` |
| U102 | L2 | `(58.659551180, 48.485658680, 1.004001040)` | `(58.65955118, 48.48565868, 1.00400104)` | `0` | `0` |
| U102 | L3 | `(58.659551180, 58.485658680, 4.004001040)` | `(58.65955118, 58.48565868, 4.00400104)` | `0` | `0` |
| U103 | L1 | `(99.714343500, 51.059552440, 2.004001040)` | `(99.71434350, 51.05955244, 2.00400104)` | `0` | `0` |
| U103 | L2 | `(111.714343500, 44.059552440, 1.004001040)` | `(111.71434350, 44.05955244, 1.00400104)` | `0` | `0` |
| U103 | L3 | `(101.714343500, 44.059552440, 4.004001040)` | `(101.71434350, 44.05955244, 4.00400104)` | `0` | `0` |
| U104 | L1 | `(149.140448480, 45.114343480, 2.004001040)` | `(149.14044848, 45.11434348, 2.00400104)` | `0` | `0` |
| U104 | L2 | `(156.140448480, 57.114343480, 1.004001040)` | `(156.14044848, 57.11434348, 1.00400104)` | `0` | `0` |
| U104 | L3 | `(156.140448480, 47.114343480, 4.004001040)` | `(156.14044848, 47.11434348, 4.00400104)` | `0` | `0` |

Итого: `count=12`, `maxAxisMm=0`, `maxEuclidMm=0` при пороге `0.001 mm`.

### 6.1 Board geometry (дополнительно, без снижения основного gate)

`boardPlacement.supported=true`; board transform `(−212.59999898, −167.19999834, +1.00400104) mm`,
`expectedTopSurfaceZMm=1.00400104`, `resultingTopSurfaceZMm=1.00400104`, diagnostics пусто.
Импортированный `board.SLDPRT`: `SolidBodyCount=1`, `saveSucceeded=true`, `reopenSucceeded=true`,
`WidthMm=175.00000000000003`, `HeightMm=155`, `ThicknessMm=2.0080020800000002`,
`VolumeResidualMm3=-1.6064586816355586E-06`, `XyBoundsResidualMm=5.1999995775986463E-07`,
`ZTopResidualMm=0`, `geometryDiagnostics=[]`. Внутри платы контрольная точка `InsideSolid=true`.

## 7. Cleanup и SHA-256

| Объект | До | После | Результат |
|---|---|---|---|
| `r3-control.PcbDoc` | `69C62604…AC3D67` | `69C62604…AC3D67` | unchanged |
| `Dev_board.PcbDoc` | не открывался | `0B3D67B6…7D167F` | not opened / not saved |
| `manifest.json` | из свежего export | `451803D9…1D6197` | зафиксирован |
| `checksums.sha256` | из свежего export | `0CB8DDFB…0A10DB` | `168/168` valid |
| STEP asset | из свежего export | `E5F44D66…35477D4` | зафиксирован |
| `smoke-result.json` | — | `986A5D5948B90E9EB1E84DE1B7BD031BC36B8C762F935B3E3F896E86E9718201` | зафиксирован |
| `smoke-placed-assembly.SLDASM` | — | `05C429F703FE7C6AC47DDC969D2DD49D3CE9A94000CAC845BA36D7FD620C6276` | зафиксирован |
| component `.SLDPRT` | — | `456B7132B140D9FB07281684CD7C3AF4EC9617C2065B1AA072E039862AD80224` | зафиксирован |
| `board.SLDPRT` | — | `84AE046B8E1D2D27647A79249891CA41CFD6B6FD7E0656710B45DAFDE786BBD8` | зафиксирован |

`cleanupStatus`: `import=Verified`, `placedAssembly=Verified`, `verified=true`;
`createdDocumentsRemaining=0`, `finalSnapshot.count=0`, `baselinePreserved=true`.
Запущенный для smoke процесс SOLIDWORKS закрыт штатно (`CloseMainWindow`, `exited`).
Копия package в default root удалена; проверка temp-копии после удаления: `temp_package_exists=True`.

## 8. Принятые решения и риски

1. **Top-only через selective import/placement.** Package export — whole-board snapshot, поэтому он содержит
   все 1050 компонентов и 1059 model body, включая Bottom. В MCAD через `--designators` попали только
   `U101–U104`; Bottom не импортировался и не размещался. Если требование «не экспортировать Bottom»
   означает полное отсутствие Bottom-данных уже в package, нужен отдельный Top-only fixture и новое решение —
   в этом smoke такой цели не ставилось.
2. `board-3d-model` присутствует в свежем package; board geometry проверена дополнительно и не подменяет
   component landmark measurements.
3. V-16 остаётся `unverified`; library footprint/body identity не доказана и не подменялась.
4. D-024, production Bottom, projection mismatch, Native/External placement, полноценный Add-in host и
   reverse sync не активировались.
5. Severity-1 diagnostics для невыбранных reference не являются failures; gate не ослаблялся, tolerance не менялись.
6. CAD-бинарники, логи с путями заказчика, STEP/PCB/SLDPRT/SLDASM/DLL в Git не добавлялись.

## 9. Проверки

- [x] source SHA в manifest == SHA control fixture
- [x] `checksums.sha256`: 168 файлов, 0 расхождений
- [x] Altium `SOURCE_STATE` + `PACKAGE_EXPORT_OK` для control copy
- [x] 4 requested / 4 placed Top designator, 0 Bottom
- [x] 1 STEP asset / 1 native SLDPRT / save+reopen 4/4
- [x] translation residual `0`, rotation basis `0`, `|det−1|=0`
- [x] 12 landmark residual `0` (axis + Euclidean)
- [x] cleanup `Verified`, baseline preserved
- [x] fixture SHA до/после совпадает
- [x] default-root package удалён после проверки
- [x] SOLIDWORKS shutdown, Altium не перезапускался
- [ ] PR в `source` — не создан по отдельному решению оператора

## 10. Commit и review

- Content commit: `pending` (будет записан после коммита).
- Branch: `task/BRIDGE-000035-top-only-smoke`, base `source` @ `ea817c9…`.
- PR: none. Report готов к review; интеграция требует отдельного решения оператора.
