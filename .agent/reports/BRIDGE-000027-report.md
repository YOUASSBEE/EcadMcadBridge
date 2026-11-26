---
id: BRIDGE-000027
type: report
status: blocked
from: local-agent
to: operator
created: 2026-11-26T21:14:11+03:00
updated: 2026-11-26T21:35:27+03:00
base: source
base_sha: bdff118b288454be68c7c590d875142620a139e2
branch: task/BRIDGE-000027-controlled-hot-reload
commit: 1fa649b9103932783ec32f588002c626af9a4cf8
pr: none
related:
  - BRIDGE-000024
  - BRIDGE-000025
  - BRIDGE-000026
  - PR-23
  - D-024
---

# BRIDGE-000027 — Controlled Altium Restart and Hot Reload Bootstrap

## 1. Итог

Статус: **`blocked`**.

После явного разрешения Оператора выполнена Фаза B: Altium Designer был штатно
перезапущен, временный Debug- shim `BridgeDiag` установлен через официальный
`Altium.Designer.HotReload`, диагностический код PR #23 реально выполнен в живой сессии
AD 26.12.2.9 и создал `diagnostics.json`.

Фактически получено:

- `status=ok`, `schemaVersion=bridge-readonly-diagnostics/1`;
- все 15 запрошенных designators обработаны, `missingRequestedDesignators=[]`;
- `sourceDocument.modifiedInEditor=false`, SHA-256 PCB совпадает с исходным;
- `IPCB_Model.GetOrigin().X/Y` получены для всех 15 body, Z ожидаемо недоступен;
- package BRIDGE-000023 проверен: `24/24` файлов, `verified=true`;
- STEP SHA-256 для всех 15 designators совпадают с `checksums.sha256`;
- 9 пар D-024 посчитаны из фактических positions/rotations/model origins, без bbox-подстановки.

Но критерии приёмки BRIDGE-000025 **не выполнены**, поэтому результат зафиксирован как
`blocked`:

1. `bodyUniqueId` равен `null` со статусом `unavailable` для всех 15 body.
2. Library-level `footprintSourceUniqueId` равен `null` для всех 15 body; все три
   `.PCBLIB` вернули `LIBRARY_FOOTPRINT_UNIQUE_ID_UNAVAILABLE` и пустые `bodyUniqueIds`.
   Placed component `SourceUniqueId`/`UniqueId` не подставлялись.
3. Фактические `e` для 9 пар близки к нулю (`0` или `-0.00000254 mm`), тогда как
   bbox-наблюдения BRIDGE-000023 дают `0.524002 mm` (CAP), `0.499999 mm` (RES) и
   `1.25000004 mm` (SOD323). Отклонение от точки сравнения составляет `0.524…1.25 mm`,
   поэтому критерий сопоставления `≤0.000006 mm` из BRIDGE-000025 §6 не выполнен.
   D-024 не подтверждён и не опровергнут окончательно: нужна review формулы/данных.

После диагностики выполнена полная откатка: shim удалён, `ExtensionsRegistry.xml`
восстановлен из backup, исходные DLL/`.ins`/`.rcs`/`deps.json` проверены по SHA-256,
Altium снова запущен в одной сессии, `BridgeDiag` в меню и pipe отсутствуют. PCB не
изменялся и не сохранялся. PR #23 остаётся `open`, `merged=false`; PR для BRIDGE-000027
не создавался.

## 2. Изменённые файлы

В Git изменены только Markdown-файлы ветки
`task/BRIDGE-000027-controlled-hot-reload`:

- `.agent/tasks/BRIDGE-000027.md` — постановка, добавлена коммитом
  `2edd26a87531347e85cd3814ff43cd3da6314254`;
- `.agent/reports/BRIDGE-000027-report.md` — настоящий отчёт.

Не добавлены и не изменены: production code, D-022/D-024, placement engine, Component
Model Catalog, Exchange Package, SDK ledger, `.PcbDoc`, DLL, STEP, SLDPRT/SLDASM, логи
CAD, registry-файлы и какие-либо бинарные артефакты.

Вне Git созданы/использованы:

- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\backup\` — проверенные копии
  PCB, исходных DLL, `.ins`, `.rcs`, `deps.json`, `ExtensionsRegistry.xml` и registry-экспортов;
- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\pr23\` — detached worktree PR #23;
- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\bridgediag\` — временный проект
  из официального шаблона с wrapper `HotEntry`;
- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\evidence\` — HotReload log,
  `diagnostics.json`, `ExtensionsRegistry.with-BridgeDiag.xml` и копия установленного shim;
- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\meta\` — временная утилита
  чтения метаданных SDK (в CAD не загружалась).

NuGet-шаблон `Altium.Designer.Extension.Templates` `1.1.0` уже был установлен в профиле
до начала работы; он не устанавливался и не удалялся агентом.

## 3. Исходное состояние и проверки Фазы A

| Параметр | Значение |
|---|---|
| Repository | `YOUASSBEE/EcadMcadBridge` |
| `origin/source` | `bdff118b288454be68c7c590d875142620a139e2` |
| Ветка | `task/BRIDGE-000027-controlled-hot-reload` |
| PR #23 | `open`, `merged=false`, head `e28881486c69f0fb689f3181f9abda38deefe481` |
| Диагностический commit PR #23 | `0b407e3f2d0a59fc300e4e0929633a406e9f0ffb` |
| BRIDGE-000025 HEAD | `40ed61706f6d96f68f89146c4eb9e717aefb5ae5` |
| BRIDGE-000026 HEAD | `713d9d1c1d30c709e1025fa81c9ac6b0dfba4fd0` |
| Altium Designer | `26.12.2.9` |
| SDK | `4.0.1` |
| .NET SDK | `10.0.404` |
| X2 PID до/после Фазы A | `33732` / `33732` |
| X2 exe | `E:\Program Files\Altium\AD26\X2.EXE` |
| Активный документ | `D:\Git\Projects\mdm15-1v05m_electric\Dev_board.PcbDoc`, `modified=false` |
| Открытые диалоги | `dialog_count=0` |
| PCB SHA-256 | `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F` |
| Request JSON | `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000025\diagnostics-request.json`, SHA-256 `9A661D2120C4DFF08AC9E45C85887CCC9E1AB210FD3FAB9C401C825CED8FA1C8` |
| Текущий пользователь | `DESKTOP-UCBT4G1\Anton`, `ADMIN_ELEVATED=False` |

### 3.1. Резервирование

В `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\backup\`:

| Файл | SHA-256 |
|---|---|
| `Dev_board.PcbDoc` | `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F` |
| `EcadMcadBridge.dll` | `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A` |
| `EcadMcadBridge.Core.dll` | `1A106CF0DD31F9D194E760392C460E62C39D4BA078A7DCE6A32953677D567F70` |
| `EcadMcadBridge.deps.json` | `A16481FF966EF03A83B35D2987ED2725B2C84967CAD5956CEA1D142905294D86` |
| `EcadMcadBridge.ins` | `938DE6DE8DF84DC74107E603EBE69865C429487A5A8F12674D728578DF73918A` |
| `EcadMcadBridge.rcs` | `C6123EB93B9EF87133739A55321EB20C5C7335010890A996A9137F8900768074` |
| `ExtensionsRegistry.xml` | `21DB6994F47627A871DEA4FDD8D6CDF51650C3BFFE737B0EB743D99664BB83C9` |
| `Altium-Builds.reg` | `AC008EB5E988B6FF95BB90A5920D20B041146D66403BC5723F006824C8C6DBBA` |
| `Altium-ExtensionManager-Prefs.reg` | `9BF6B7A4A60DA7AA6421121FA7D10640C0FBBC322EE04FBDF22478B9FB97FD1C` |
| `Altium-ExtensionPublisher-Prefs.reg` | `DAE184D516B4A69394303912794E85BAF555C5134713304070550A29C4972A14` |

PCB-копия побайтово совпадала с оригиналом. До перезапуска исходные файлы расширения и
`ExtensionsRegistry.xml` совпадали с backup: `EXT_CHANGED=`, `EXT_MISSING=`,
`REG_MATCH=True`.

### 3.2. Release-сборка PR #23

`git diff --name-only 0b407e3 e288814` показал только изменения report-файла; код
diagnostics не менялся.

Release-сборка из detached worktree PR #23:

```text
dotnet build ...\src\EcadMcadBridge.Altium\EcadMcadBridge.Altium.csproj -c Release --nologo -p:AltiumSdkDir="E:\Program Files\Altium\AD26\System"
```

Результат: `0 warnings`, `0 errors`. Хэши:

- `EcadMcadBridge.dll` — `48F51E2944FC218BD7C9D6DBE422E90A95043AD8BEA7459522C076C415CEE1DA`
- `EcadMcadBridge.Core.dll` — `60EF27A3BC01C196D68C1577E79D92B13310CB9887B5F17F151CF5DF578CB93D`

В BRIDGE-000025 были зафиксированы другие хэши (`1C0E1742…` / `C253FBE3…`); причина
расхождения не установлена и отнесена к рискам. Release-сборка использовалась только для
проверки компилируемости; official HotReload требует Debug implementation.

## 4. Обоснование выбора механизма

BRIDGE-000026 показал, что в живой сессии X2 нет shim, implementation, hot-reload pipe и
generic `Run`; заменить заблокированную DLL без остановки X2 невозможно. Единственный
официально опубликованный механизм, позволяющий после однократной установки менять C#
код без перезапуска, — Altium Debug Hot Reload.

| Проверка | Доказательство | Вывод |
|---|---|---|
| Пакет shim | NuGet `Altium.Designer.HotReload` `1.0.1`, author `Altium`, license `MIT` | использован `1.0.1` |
| Шаблон | NuGet `Altium.Designer.Extension.Templates` `1.1.0`, author `Altium`, license `MIT` | использован official template |
| Версия Altium | Official README: `Altium Designer 24 or later` | AD 26.12.2.9 покрывается |
| Target framework | Official README: .NET SDK 10 / `net10.0-windows` для 26.12 | Branch TFM корректен |
| SDK | Official docs: SDK поставляется running AD instance | сборка против `E:\Program Files\Altium\AD26\System` |
| Режим | `Debug builds only`; Release не содержит hot reload | shim и implementation собраны в Debug |
| Установка | `Install once, restart AD once`; deploy создаёт `.bak` | выполнено и later откачено |
| Сессия | первая команда расширения запускает pipe | pipe подтверждён после `BridgeDiag|Hello` |
| Pipe | `\\.\pipe\<name>.HotReload.<pid>`, current-user only | диагностика из-под `Anton` |
| Отказ | плохая сборка не заменяет рабочую generation | зафиксировано в логе |
| Uninstall | закрыть AD, удалить папку, убрать `<Item>` | выполнено при откате |

Источники: official README и `HotReloadHost.cs` из
`https://github.com/AltiumDeveloper/ad-extension-template`, NuGet metadata packages
`Altium.Designer.HotReload/1.0.1` и `Altium.Designer.Extension.Templates/1.1.0`,
`tools/deploy.ps1`, `tools/hotreload.ps1`.

## 5. Фаза B — фактическое выполнение

### 5.1. Сборка и установка

Создан временный проект `BridgeDiag` из official template. В implementation скопированы
диагностические исходники PR #23 через `Compile Include` без изменения самих файлов;
добавлен только `HotEntry` с BCL-сигнатурами. В shim сохранён `IClient`, переданный в
`PluginFactory.InvokePluginFactory`, и добавлен `Implementation.ResolveView` как fallback
для `IServerDocumentView`.

Debug-сборка: `0 warnings`, `0 errors`. Финальные хэши:

| Артефакт | SHA-256 |
|---|---|
| `BridgeDiag.dll` (shim) | `F05DBB48EEFA9E8012F08A8BC820DB7891424027B5C795C3761045193A5D693A` |
| `BridgeDiag.Impl.dll` (final generation 4) | `C1DB4F01A7D015BFDD78021EB71F49D852A7BA41EB452D3BC04627B1E60D048B` |
| `BridgeDiag.Impl.dll` (initial generation 1) | `58B169CC2F750386BF5C6B83C677671CB4EE2EEC4DCAC8D2547A8176A230ED50` |
| `EcadMcadBridge.Core.dll` во временном implementation | `E459091D20B584115F1969EC6B112A466FC40102825D192EB463021B7D81357D` |

`tools\deploy.ps1` установил shim в
`...\Extensions\BridgeDiag\`, добавил `<Item HRID="BridgeDiag"
Guid="84df56f4-b415-4f3a-bb9b-750e20f58f5e">` в `ExtensionsRegistry.xml` и создал
`ExtensionsRegistry.xml.bak-20261126212043` с исходным хэшем
`21DB6994F47627A871DEA4FDD8D6CDF51650C3BFFE737B0EB743D99664BB83C9`.
Original extension `EcadMcadBridge` не перезаписывался.

### 5.2. Перезапуск и запуск pipe

- PID `33732` завершён штатно через `CloseMainWindow()`; `WaitForExit=True`; PCB hash не изменился.
- Новый X2: PID `26144`, start `2026-11-26T21:21:47.8198801+03:00`, version `26.12.2.9`.
- Polling loop EDA-моста после рестарта не стартовал; выполнен `app_recover_script(force=true)`,
  loop ответил (`bridge version 2026.11.25.1`).
- В `app_ui_snapshot` появилось top-level меню `BridgeDiag`; `app_click_menu` показал
  пункты `Hello` и `Developer`.
- `BridgeDiag|Hello` запустил extension: pipe `\\.\pipe\BridgeDiag.HotReload.26144`,
  log `C:\Users\Anton\AppData\Local\Altium.Designer.HotReload\BridgeDiag.log`,
  generation 1 hash `58B169CC2F75`.
- `hotreload.ps1 ping/status/list` подтвердили контракт v1 и команды
  `Hello`, `ReadBodyIdentityDiagnostics`.

### 5.3. Выполнение диагностики

Первый pipe-вызов `run ReadBodyIdentityDiagnostics` завершился ошибкой:
`No active IServerDocumentView is available`. Причина: official `HotReloadHost.Invoke`
передаёт `Context=null`, а `IClient.GetCurrentView()` в этом контексте вернул `null`.
Обход выполнен только во временном implementation без изменения shim и без второго
restart: команда `Hello`, вызванная из UI, получает реальный `IServerDocumentView` и
сохраняет его в статическом поле текущей generation; pipe-команда использует сохранённый
view. После hot reload generation 4 (`C1DB4F01A7D0`) UI `BridgeDiag|Hello` вернул
`uiView=captured`.

Повторный вызов:

```text
powershell -ExecutionPolicy Bypass -File tools\hotreload.ps1 -Name BridgeDiag -ProcessId 26144 -ReadTimeoutMs 600000 run ReadBodyIdentityDiagnostics C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000025\diagnostics-request.json
```

Результат: `diagnostics.json written … status=ok bytes=32727`, exit code `0`.
SHA-256 output: `246AE33E49E4181AFB5D1C6AFEE3ECBE69154F04783F7849ECFCF6E429819173`.
HotReload log сохранён в `evidence\BridgeDiag.log` с SHA-256
`4106269F95705DE39D11F0A8E2B058A725D70E1275177271FED133B2DB7C0ADB`.

### 5.4. Факты из `diagnostics.json`

| Поле | Факт |
|---|---|
| `status` | `ok` |
| `sourceDocument.path` | `D:\Git\Projects\mdm15-1v05m_electric\Dev_board.PcbDoc` |
| `sourceDocument.sha256OnDisk` | `0b3d67b6144aa7d766c850daa797287319ca17aab8345be4557bd0d8807d167f` |
| `sourceDocument.modifiedInEditor` | `false` |
| `units.rawUnit` | `iu` |
| `units.millimetersPerIu` | `2.54E-06` |
| `componentPositionCoordinateSystem` | `pcb board coordinates, raw IU; rotation in degrees` |
| `modelOriginCoordinateSystem` | `IPCB_Model.GetOrigin() model-local X/Y, raw IU; GetOrigin has no Z component` |
| `package.verified` | `true` |
| `package.filesVerified / filesListed` | `24 / 24` |
| `summary.bodiesEnumerated` | `130` |
| `summary.bodiesReported` | `15` |
| `summary.missingRequestedDesignators` | `[]` |
| `libraries` | 3 library entries, 3 footprint matches, all `sourceUniqueId=null`, `bodyUniqueIds=[]`, `LIBRARY_FOOTPRINT_UNIQUE_ID_UNAVAILABLE` |

Доступность required-полей по всем 15 designators:

| Designator | Side | θ° | componentId | bodyOrdinal | `bodyUniqueId` / status | library-level `footprintSourceUniqueId` / status | `modelOrigin.X/Y` в IU | `modelOrigin` status | STEP verified |
|---|---|---:|---|---:|---|---|---|---|---|
| C37 | Bottom | 180 | `altium:component:SQMHHCOO` | 0 | `null` / unavailable | `null` / unavailable | `32283430 / 27571116` | `xy-available-z-unavailable` | true |
| C34 | Bottom | 180 | `altium:component:IEHSNVJO` | 0 | `null` / unavailable | `null` / unavailable | `32263747 / 26131918` | `xy-available-z-unavailable` | true |
| C36 | Bottom | 270 | `altium:component:SVOLUPTW` | 0 | `null` / unavailable | `null` / unavailable | `29292769 / 26496030` | `xy-available-z-unavailable` | true |
| C35 | Bottom | 270 | `altium:component:KUUSBQGN` | 0 | `null` / unavailable | `null` / unavailable | `26889000 / 26968471` | `xy-available-z-unavailable` | true |
| C25 | Top | 180 | `altium:component:IKPQYLNG` | 0 | `null` / unavailable | `null` / unavailable | `24566896 / 23090520` | `xy-available-z-unavailable` | true |
| C29 | Top | 270 | `altium:component:RKOWLKUD` | 0 | `null` / unavailable | `null` / unavailable | `21023653 / 21318865` | `xy-available-z-unavailable` | true |
| R50 | Bottom | 0 | `altium:component:LDESCJFJ` | 0 | `null` / unavailable | `null` / unavailable | `30196816 / 27007890` | `xy-available-z-unavailable` | true |
| R43 | Bottom | 0 | `altium:component:TKEBPVOU` | 0 | `null` / unavailable | `null` / unavailable | `30196816 / 26023638` | `xy-available-z-unavailable` | true |
| R44 | Bottom | 90 | `altium:component:OVDOTPWB` | 0 | `null` / unavailable | `null` / unavailable | `28908193 / 26496031` | `xy-available-z-unavailable` | true |
| R53 | Bottom | 180 | `altium:component:LGNSDMJH` | 0 | `null` / unavailable | `null` / unavailable | `32204754 / 26713307` | `xy-available-z-unavailable` | true |
| R25 | Top | 0 | `altium:component:YPIMABWM` | 0 | `null` / unavailable | `null` / unavailable | `15511780 / 17283445` | `xy-available-z-unavailable` | true |
| R36 | Top | 90 | `altium:component:JHHTJMYM` | 0 | `null` / unavailable | `null` / unavailable | `26141751 / 21289339` | `xy-available-z-unavailable` | true |
| R26 | Top | 180 | `altium:component:JTETQPJD` | 0 | `null` / unavailable | `null` / unavailable | `14724441 / 15216555` | `xy-available-z-unavailable` | true |
| D9 | Bottom | 180 | `altium:component:DFSPFOXI` | 0 | `null` / unavailable | `null` / unavailable | `27864479 / 25697359` | `xy-available-z-unavailable` | true |
| D8 | Top | 180 | `altium:component:SOKNTAMC` | 0 | `null` / unavailable | `null` / unavailable | `21181103 / 19350695` | `xy-available-z-unavailable` | true |

`modelOrigin.zIu` и `zMm` равны `null` во всех строках с диагностикой
`MODEL_ORIGIN_Z_UNAVAILABLE: IPCB_Model.GetOrigin returns ICoordPoint (X/Y only).`
Это ограничение SDK, а не подстановка.

Пример полного STEP SHA-256 из package (совпадает с `checksums.sha256`):
`C37 → 59a5b7320690042655df55350c4df28365352e19b882a200ff63b72766534284`,
`C25 → fb14c286fee877e4fa5a14d084c0be11ef6630593f63df471ad1a8934e79c7ea`,
`R50 → 0ed90260900d03a2f7b7b3172c43b5dca66197bbb5159fd9107d05ea4b503e06`,
`R25 → 73f96897363b7f57322c07e7e0fa17cedcda08c417b184b21d8d9ca7f5ab956d`,
`D9 → eb4c22355753db4298c9661717d77c334a6c60004a4fbb01916f8c322d0091af`,
`D8 → 46fc003220ca754fa2da79e7bc31264e9d2ad9cd0d46f3689eb644e5450b867d`.
Остальные 9 SHA-256 находятся в `diagnostics.json` и в `checksums.sha256`.

### 5.5. Девять пар D-024

Расчёт выполнен по формуле BRIDGE-000025 §6 с фактическими `rotationDeg`,
`modelOrigin.X/Y` и `componentPosition.X/Y`; все значения переведены в мм через
`millimetersPerIu = 2.54E-06`:

```text
d_top          = Rz(-θ_top) · (ModelOrigin_top.XY - ComponentPosition_top.XY)
d_bottom_local = Rz(-θ_bottom) · (ModelOrigin_bottom.XY - ComponentPosition_bottom.XY)
d_bottom_board = (d_bottom_local.X, -d_bottom_local.Y)
e              = d_bottom_board - d_top
```

| Bottom | Top ref | Footprint | θ bottom | θ top | `d_top`, mm | `d_bottom_local`, mm | `d_bottom_board`, mm | `e`, mm | Residual к bbox-наблюдению, mm |
|---|---|---|---:|---:|---|---|---|---|---:|
| C37 | C25 | CAP 0402_1005 | 180 | 180 | `(0.000083820, 0.000078740)` | `(0.000081280, -0.000078740)` | `(0.000081280, 0.000078740)` | `(-0.000002540, 0.000000000)` | `0.524002000` |
| C34 | C25 | CAP 0402_1005 | 180 | 180 | `(0.000083820, 0.000078740)` | `(0.000083820, -0.000078740)` | `(0.000083820, 0.000078740)` | `(0.000000000, 0.000000000)` | `0.524002000` |
| C36 | C29 | CAP 0402_1005 | 270 | 270 | `(0.000083820, 0.000078740)` | `(0.000083820, -0.000078740)` | `(0.000083820, 0.000078740)` | `(0.000000000, 0.000000000)` | `0.524002000` |
| C35 | C29 | CAP 0402_1005 | 270 | 270 | `(0.000083820, 0.000078740)` | `(0.000083820, -0.000078740)` | `(0.000083820, 0.000078740)` | `(0.000000000, 0.000000000)` | `0.524002000` |
| R50 | R25 | RES 0402_1005 | 0 | 0 | `(-0.000081280, 0.249951240)` | `(-0.000081280, -0.249951240)` | `(-0.000081280, 0.249951240)` | `(0.000000000, 0.000000000)` | `0.499999000` |
| R43 | R25 | RES 0402_1005 | 0 | 0 | `(-0.000081280, 0.249951240)` | `(-0.000081280, -0.249951240)` | `(-0.000081280, 0.249951240)` | `(0.000000000, 0.000000000)` | `0.499999000` |
| R44 | R36 | RES 0402_1005 | 90 | 90 | `(-0.000081280, 0.249951240)` | `(-0.000081280, -0.249951240)` | `(-0.000081280, 0.249951240)` | `(0.000000000, 0.000000000)` | `0.499999000` |
| R53 | R26 | RES 0402_1005 | 180 | 180 | `(-0.000081280, 0.249951240)` | `(-0.000081280, -0.249951240)` | `(-0.000081280, 0.249951240)` | `(0.000000000, 0.000000000)` | `0.499999000` |
| D9 | D8 | SOD323 | 180 | 180 | `(0.000000000, 0.000000000)` | `(0.000000000, 0.000000000)` | `(0.000000000, 0.000000000)` | `(0.000000000, 0.000000000)` | `1.250000040` |

Все пары имеют `bodyOrdinal=0` с обеих сторон; paired IDs — `componentId` из таблицы
выше, STEP SHA-256 — из `diagnostics.json`/`checksums.sha256`. Bbox-величины
`CAP e≈[0,+0.524002]`, `RES e≈[0,+0.499999]`, `SOD323 e≈[-0.00000254,+1.25000004]`
использованы **только как точка сравнения**, не как вход расчёта.

Вывод: 9/9 пар посчитаны по фактическим значениям, но D-024 не подтверждён: отклонение
от bbox-наблюдений на 3–6 порядков больше ожидаемого `≤0.000006 mm`. Причины требуют
отдельного решения: это может быть несовместимость формулы с семантикой
`IPCB_Model.GetOrigin()`, неверная интерпретация bbox-proxy или особенность выбранных
library assets. Bbox-величины не подставлялись в расчёт. Ни D-024, ни код не менялись.

### 5.6. Откат

Откат выполнен после сохранения evidence:

1. `diagnostics.json`, HotReload log, `ExtensionsRegistry.with-BridgeDiag.xml` и копия
   установленного shim скопированы в `...\bridge-000027\evidence\`.
2. X2 PID `26144` завершён штатно; PCB SHA-256 до/после совпал.
3. Удалён `...\Extensions\BridgeDiag\`.
4. `ExtensionsRegistry.xml` восстановлен из backup; итоговый SHA-256
   `21DB6994F47627A871DEA4FDD8D6CDF51650C3BFFE737B0EB743D99664BB83C9`;
   `REG_BRIDGEDIAG_ITEMS=0`.
5. Original DLL/`.ins`/`.rcs`/`deps.json` расширения `EcadMcadBridge` сверены с backup:
   все `MATCH=True`.
6. Altium запущен повторно. При старте появились два X2 (`27116` и `31400`); лишний
   `31400` закрыт штатно. Итоговая сессия: PID `27116`, version `26.12.2.9`,
   `candidate_count=1`.
7. Polling loop EDA-моста снова восстановлен через `app_recover_script(force=true)`.
8. Финальная проверка: `BridgeDiag` в menu bar отсутствует, pipe отсутствует,
   `dialog_count=0`, активный документ `Dev_board.PcbDoc` с `modified=false`,
   PCB SHA-256 `0B3D67B6…7D167F`.

NuGet-шаблон не удалялся, так как был установлен до начала работы. Registry user profile
не менялся.

## 6. Откат — статус

Откат завершён полностью. Permanent конфигурация Altium восстановлена к исходному
состоянию:

| Проверка | Значение после отката |
|---|---|
| X2 PID | `27116` (одна сессия) |
| Altium version | `26.12.2.9` |
| `Extensions\BridgeDiag` | absent |
| `ExtensionsRegistry.xml` item BridgeDiag | `0` |
| `ExtensionsRegistry.xml` SHA-256 | `21DB6994F47627A871DEA4FDD8D6CDF51650C3BFFE737B0EB743D99664BB83C9` |
| HotReload pipe | absent |
| Original add-in files | совпадают с backup |
| PCB `modified` | `false` |
| PCB SHA-256 | `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F` |
| Диалоги | `dialog_count=0` |

`diagnostics.json` и логи сохранены вне Git как доказательство и не удалялись.

## 7. Неподтверждённые вопросы и риски

1. Library-level unique IDs недоступны для всех трёх указанных `.PCBLIB`. Причина
   (версия библиотеки, способ чтения, отсутствие ID в файле) не установлена. Требуется
   решение Оператора, какой источник library identity считать валидным.
2. Placed body IDs также недоступны (`placedBodyUniqueId=null`,
   `BODY_ID_NOT_PROVEN_LIBRARY_STABLE`). Подстановка placed component UniqueID не
   применялась.
3. Фактические `e` не совпадают с bbox-наблюдениями BRIDGE-000023. Не меняя D-024,
   нельзя сделать вывод о применимости Y-sign rule; требуется review формулы §6 и
   семантики `ModelOrigin`.
4. Точная совместимость HotReload `1.0.1` с patch `26.12.2.9` поименно не заявлена;
   доказательство косвенное через AD 26.12 → `net10.0-windows`. Фактически shim
   загрузился и работал.
5. Различие Release-хэшей BRIDGE-000025 и Фазы A при неизменном коде не объяснено.
6. Debug pipe — локальный development-only механизм current-user; после отката удалён.
7. Capture `IServerDocumentView` через UI `Hello` — временный приём только в
   implementation вне Git; production API и PR #23 не менялись.
8. `IPCB_Model.GetOrigin()` не возвращает Z по контракту SDK; Z оставлен `null`.
9. При повторном старте Altium автоматически создал второй X2; лишний процесс закрыт,
   финально одна сессия. Причина авто-старта не исследовалась.
10. WPF удерживает первую загрузку сборок; `status -gc` показывал Pending generations —
    известное ограничение official package, не ошибка диагностики.

### 7.1. Эскалация

Конкретный вопрос Оператору: что делать с BRIDGE-000025 и PR #23, если реальный
`diagnostics.json` создан, но library-level `footprintSourceUniqueId` и `bodyUniqueId`
недоступны для всех 15 body, а вычисленные `e` отличаются от bbox-наблюдений BRIDGE-000023
на `0.524…1.25 mm`?

Варианты:

1. Зафиксировать текущий результат как insufficient evidence, оставить BRIDGE-000024 и
   PR #23 `open`/`blocked`, BRIDGE-000025 — `blocked`, без активации production Bottom.
2. Постановить отдельную research-задачу по стабильному library-level identity и
   фактической семантике `IPCB_Model.GetOrigin()`, затем повторить read-only runtime в
   отдельной контролируемой сессии.
3. Инициировать отдельный ADR по пересмотру формулы BRIDGE-000025 §6 / эталона
   bbox-proxy, если будет доказано, что comparison-величины не являются корректной
   точкой сравнения; в этой задаче D-024 и код не трогать.

Рекомендация: вариант 1 как текущая фиксация плюс вариант 2 как следующий шаг;
PR #23 не мержить, production Bottom/Native/External не активировать до принятия
решения Оператора.

## 8. Проверки

| Проверка | Результат |
|---|---|
| Фаза A без перезапуска X2 | выполнено; PID `33732` до и после |
| Approvals | отдельное разрешение Оператора получено до Фазы B |
| PCB hash до/после Phase B | совпадает; `modified=false` |
| Debug shim build | `0 warnings`, `0 errors` |
| Deploy official `deploy.ps1` | выполнен; `.bak` создан |
| Restart X2 | выполнен; PID `33732` → `26144` → финально `27116` |
| HotReload pipe / generation | подтверждены; `BridgeDiag.HotReload.26144`, generation 4 |
| Diagnostic command | **реально выполнена**, exit code `0` |
| `diagnostics.json` | создан, `status=ok`, SHA `246AE33E…819173` |
| 15 designators | `15/15`, `missing=[]` |
| Package BRIDGE-000023 | `24/24`, `verified=true` |
| STEP SHA-256 | `15/15` совпали с manifest/checksums |
| `bodyUniqueId` | **unavailable для 15/15** |
| library-level `footprintSourceUniqueId` | **unavailable для 15/15** |
| `IPCB_Model.GetOrigin().X/Y` | получены для 15/15, не bbox |
| `IPCB_Model.GetOrigin().Z` | `null` по контракту SDK |
| 9 пар | посчитаны `9/9`, residual не укладывается в `≤0.000006 mm` |
| Rollback shim/registry/DLL | выполнен и проверен |
| PR для BRIDGE-000027 | не создавался |
| PR #23 | `open`, `merged=false` |
| D-022/D-024, placement engine, catalog, Exchange Package, SDK ledger, production API | не изменялись |
| DLL/PCB/STEP/CAD artifacts in Git | нет |
| `dotnet test -c Release` | не запускался: в репозитории только Markdown; production code не менялся |

Окружение: Altium Designer `26.12.2.9`, SDK `4.0.1`, .NET SDK `10.0.404`,
HotReload `1.0.1`, template `1.1.0`, bridge script `2026.11.25.1`.

## 9. Commit SHA и PR

Заполняется после финального коммита:

- branch: `task/BRIDGE-000027-controlled-hot-reload`
- content commit: `2edd26a87531347e85cd3814ff43cd3da6314254`
- meta commit 1: `062b6fd5bc01d9055c8cfed190fcad571a211879`
- final report content commit: `1fa649b9103932783ec32f588002c626af9a4cf8`
- meta commit 2 (фиксация SHA): текущий HEAD; его SHA не включается в текст во избежание рекурсии
- PR для BRIDGE-000027: не создаётся по условию задачи
- PR #23: https://github.com/YOUASSBEE/EcadMcadBridge/pull/23 — `open`, `merged=false`,
  head `e28881486c69f0fb689f3181f9abda38deefe481`

## 10. История изменений

1. Проверены `origin/source`, PR #23 и состояние X2; создана ветка
   `task/BRIDGE-000027-controlled-hot-reload`.
2. Создан task-файл BRIDGE-000027; выполнены только read-only проверки Фазы A.
3. Собраны и проверены backup PCB, DLL, `.ins`, `.rcs`, `ExtensionsRegistry.xml` и
   registry-экспорты вне Git.
4. Проверена компилируемость диагностики PR #23; зафиксированы хэши и расхождение с
   BRIDGE-000025.
5. Изучены official HotReload docs, NuGet metadata, deploy/pipe scripts и SDK metadata;
   подготовлен wrapper `BridgeDiag`.
6. Подготовлены сценарии установки, запуска, успеха, отката и список требуемых разрешений;
   report переведён в `awaiting_operator_approval`.
7. Получено явное разрешение Оператора. Выполнена Фаза B: shim собран, установлен, X2
   перезапущен, pipe запущен, диагностика PR #23 реально выполнена.
8. Зафиксирован частичный успех: `diagnostics.json` создан, 15/15 body и 9/9 пар; hard
   blocker — недоступные library/body unique IDs и расхождение `e` с bbox-наблюдениями.
9. Выполнен полный откат shim/registry; Altium возвращён в исходную конфигурацию, PCB
   не изменён. Status переведён в `blocked`.
