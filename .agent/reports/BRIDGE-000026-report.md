---
id: BRIDGE-000026
type: report
status: blocked
from: local-agent
to: operator
created: 2026-11-26T20:54:03+03:00
updated: 2026-11-26T21:04:13+03:00
base: source
base_sha: bdff118b288454be68c7c590d875142620a139e2
branch: task/BRIDGE-000026-live-addin-loading
commit: dfdaf311471788f7d2f62785fe357993455ad10a
pr: none
related:
  - BRIDGE-000024
  - BRIDGE-000025
  - PR-23
---

# BRIDGE-000026 — Live Add-in Loading Without Altium Restart

## 1. Итог

Статус: **`blocked`**.

В текущей сессии Altium Designer 26.12.2.9 нет применимого поддерживаемого способа загрузить
диагностический C#-код без перезапуска, замены загруженной DLL, machine-wide регистрации,
изменения реестра и постоянной конфигурации. Altium публикует Debug-механизм hot reload, но он
требует заранее установленного и зарегистрированного shim-расширения и однократного перезапуска;
в работающем X2 такого shim нет. Runtime-диагностика BRIDGE-000025 **не выполнялась**.

Агент не завершал и не перезапускал X2, не менял PCB, DLL, `.ins`/`.rcs`, реестр и конфигурацию.
Все действия были read-only, за исключением создания текстовых файлов задачи и отчёта в Git.

BRIDGE-000024 и PR #23 остаются открытыми/`blocked`. PR #23 не мержился.

## 2. Проверенное исходное состояние

| Параметр | Значение |
|---|---|
| Repository | `YOUASSBEE/EcadMcadBridge` |
| `source` | `bdff118b288454be68c7c590d875142620a139e2` |
| Ветка задачи | `task/BRIDGE-000026-live-addin-loading`, создана от `origin/source` |
| BRIDGE-000025 HEAD | `40ed61706f6d96f68f89146c4eb9e717aefb5ae5` |
| PR #23 | `state=open`, `merged=false`, head `e28881486c69f0fb689f3181f9abda38deefe481` |
| Диагностическая реализация | commit `0b407e3f2d0a59fc300e4e0929633a406e9f0ffb` |
| Altium Designer | `26.12.2.9` (`app_get_version`) |
| Altium SDK | `4.0.1` (`...\SDK4.0\Version.txt`) |
| .NET SDK | `10.0.404`; bootstrap add-in `0.4.0`, CLR `10.0.10` |
| EDA bridge | mcp_server `0.8.2`, bundled script `2026.11.25.1` |

## 3. Этап A — обследование работающего процесса

### 3.1. X2 до и после

| Момент | PID | Путь | Время создания | Состояние |
|---|---:|---|---|---|
| До обследования | `33732` | `E:\Program Files\Altium\AD26\X2.EXE` | `2026-11-26 20:38:02+03:00` | `running=true`, `attached=true`, `candidate_count=1` |
| После обследования | `33732` | тот же | тот же | без изменений |

`other_servers=[]`, второй X2-процесс не найден. Открытых диалогов нет (`dialog_count=0`).
Активный документ — `Dev_board.PcbDoc`, `modified=false`; SHA-256 после обследования
`0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F`.
Агент Altium не закрывал, не перезапускал и не сохранял.

### 3.2. Фактически загруженные/установленные DLL

Каталог расширений:

`C:\ProgramData\Altium\Altium Designer {B4282C90-18BF-448F-BF1A-9E36AE887190}\Extensions\EcadMcadBridge`

| Название | Статус наблюдения | Путь | SHA-256 | Версия | Блокировка |
|---|---|---|---|---|---|
| `EcadMcadBridge.dll` | подтверждено как загруженный модуль процесса (`Get-Process -Module`) | `< каталог выше >\EcadMcadBridge.dll` | `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A` | File `1.0.0.0`, Product `1.0.0+836d93dd8c8dd1006a5d25efd1282ee75e3872e4` | `FileShare.None` + `ReadWrite` → отказ: файл используется другим процессом; чтение с `FileShare.ReadWrite` успешно |
| `EcadMcadBridge.Core.dll` | установленная зависимость из `.deps.json`; отдельно в списке нативных модулей процесса не отображается; поддерживаемого API для запроса managed assembly list нет | `< каталог выше >\EcadMcadBridge.Core.dll` | `1A106CF0DD31F9D194E760392C460E62C39D4BA078A7DCE6A32953677D567F70` | File `1.0.0.0`, Product `1.0.0+836d93dd8c8dd1006a5d25efd1282ee75e3872e4` | `FileShare.None` + `ReadWrite` → успешно; чтение с `FileShare.ReadWrite` → успешно |

Вывод: загружена **старая** сборка; диагностическая команда в ней отсутствует. Блокировка основной
DLL означает, что её замена на диске невозможна без остановки процесса, что прямо запрещено.

### 3.3. Каталог расширений и регистрация

Содержимое каталога: `EcadMcadBridge.dll`, `EcadMcadBridge.Core.dll`, `EcadMcadBridge.deps.json`,
`EcadMcadBridge.ins`, `EcadMcadBridge.rcs`.

`ExtensionsRegistry.xml` содержит запись:

- `HRID="EcadMcadBridge"`;
- `Path=C:\ProgramData\Altium\Altium Designer {B4282C90-18BF-448F-BF1A-9E36AE887190}\Extensions\EcadMcadBridge`;
- `Status=0`, `Title=EcadMcadBridge`.

`EcadMcadBridge.ins` объявляет две команды: `ExportSmokeSnapshot`, `ExportBoardSnapshot`.
`EcadMcadBridge.rcs` содержит только эти два `Command`/`PL` и их меню. `ReadBodyIdentityDiagnostics`
в постоянной регистрации отсутствует.

Лог моста после последнего bootstrap (`2026-11-26T17:38:07Z` = `20:38:07+03:00`):

```text
BOOTSTRAP_FACTORY addin=0.4.0 clr=10.0.10
BOOTSTRAP_COMMANDS commands=ExportSmokeSnapshot,ExportBoardSnapshot
BOOTSTRAP_MODULE module=EcadMcadBridge
```

Поиск меню через EDA (`app_find_menu_command`):

- `EcadMcadBridge`, `Bridge`, `Export Board`, `Body Identity`, `ReadBodyIdentityDiagnostics` → `count=0`;
- `Extensions` → найдена только `ExtensionManager:ManageExtensions`;
- `Scripting`/`Run Script` → найдена только `ScriptingSystem:RunScript`.

`obj_run_process` в этой задаче **не применялся**: ранее он возвращал `dispatched=true` для
неизвестного process name и не является доказательством регистрации или исполнения.

Дополнительно проверено отсутствие предпосылок Altium Debug hot reload: в списке модулей X2 нет
модуля с именем `HotReload`; в каталоге расширений нет shim/implementation-проектов и файлов
`*.Impl.dll`; среди current-user named pipe нет pipe с `Ecad`, `HotReload` или `Altium` в имени;
`.ins`/`.rcs` не содержат generic `<Extension>:Run` с параметром команды. Файлы не создавались.

### 3.4. Подтверждение неизменности конфигурации

До и после обследования сравнены SHA-256:

- 5 файлов в каталоге `EcadMcadBridge`;
- 444 конфигурационных/регистрационных файла в трёх Altium-корнях (ProgramData, Roaming, Local),
  включая `ExtensionsRegistry.xml`, `DXP.RCS`, `.ins`/`.rcs` файлы;
- `Dev_board.PcbDoc`.

Результат: `CHANGED=0`, `MISSING=0`. Реестр читался только в режиме чтения: найдены ключи
`...\Preferences\ExtensionManager`, `...\Preferences\ExtensionPublisher`; правок не было.

## 4. Этап C — per-user Extensions

| Проверка | Результат |
|---|---|
| Поддерживаемый per-user каталог для AD26 в официальной документации | не найден |
| Официальный путь расширений | `\ProgramData\Altium\Altium Designer <GUID>\Extensions`; корневой `ExtensionsRegistry.xml` учитывает установленные расширения |
| Per-user каталоги `Extensions` на этой машине | `PER_USER_EXTENSION_DIR_COUNT=0` в `%APPDATA%\Altium`, `%LOCALAPPDATA%\Altium`, `%USERPROFILE%\Documents\Altium` |
| Подхватывается ли текущей установкой гипотетический per-user каталог | не подтверждено; текущая установка использует machine-wide ProgramData и его `ExtensionsRegistry.xml` |
| Можно ли загрузить дополнительное расширение из per-user каталога в текущую сессию | нет подтверждённого механизма |
| Требуется ли постоянная конфигурация / перезапуск | штатная установка расширения требует регистрации и перезапуска |
| Действия с файлами | каталоги и файлы не создавались, DLL не копировались |

Официальная документация прямо указывает только ProgramData-путь; сам факт наличия какого-либо
пользовательского каталога не означал бы, что Altium его использует. Здесь такого каталога нет.
Опция шаблона `--extensions-dir` — параметр deploy-инструмента, а не документированное
автообнаружение пользовательских расширений.

## 5. Этап B — исследованные официальные механизмы

| Механизм | Официальное название / API / UI | Поддержка | Регистрация | Перезапуск | Изменение конфигурации / реестра | Без завершения X2? | Вердикт |
|---|---|---|---|---|---|---|---|
| Установка расширения через UI | `Extensions and Updates`, команда `ExtensionManager:ManageExtensions` | Altium Designer 26, документация `Extending Your Altium Designer Installation` | да, установка в ProgramData + `ExtensionsRegistry.xml` | **да**, прямо требуется документацией | да, machine-wide конфигурация | нет | отвергнуто: требует перезапуск и постоянную регистрацию |
| Debug hot reload | `Altium.Designer.HotReload` 1.0.1 и `AltiumDeveloper/ad-extension-template`: shim + implementation в collectible `AssemblyLoadContext`, current-user named pipe, generic `<Extension>:Run` | официальный шаблон Altium: AD 24+, `net10.0-windows` для AD 26.12; только Debug | **однократно**: shim копируется в ProgramData и регистрируется в `ExtensionsRegistry.xml`; в каждой сессии нужна первая команда | **да, один раз** для shim; дальше implementation меняется без перезапуска | да при начальном deploy | **нет сейчас**: в X2 нет shim, pipe и generic `Run`; prerequisite отсутствует | отвергнуто для текущей сессии: потребовало бы новую permanent registration и перезапуск, а также разделение PR #23 на shim+implementation |
| Команда внутри SDK-модуля | `PluginServerModule.InitializeCommands()` → `CommandLauncher.RegisterCommand(...)` | SDK 4.0.1, C# Quick Start | команда регистрируется только при создании уже загруженного серверного модуля; сам модуль устанавливается расширением | **да** для установки расширения | да | нет | отвергнуто: диагностический модуль не загружен; регистрация требует расширения |
| Живой старт уже установленного сервера | `IClient.StartServer(string moduleName)` / `StopServer` | SDK 4.0.1, `DXP.Interfaces.cs:1797-1884`; docs `IClient interface` | сервер уже должен быть установлен/известен; аргумент — имя модуля, не путь DLL | для полностью новой DLL всё равно нужна установка | косвенно, через установку | не для загрузки новой DLL | отвергнуто: не загружает произвольную DLL; EDA-мост не предоставляет вызов; текущая registry указывает на старую DLL |
| API менеджера расширений | `IDXPExtensionManager`, `IDXPExtensionManagerSettings` | SDK 4.0.1, `DXP.Interfaces.cs:8263-8284` | интерфейс имеет только `Internal_GetState_Settings`, `Load/Save` настроек частоты проверки | — | настройки extension manager | не даёт загрузки DLL | отвергнуто: нет install/load API |
| Скриптовая система | `ScriptingSystem:RunScript`, `*.PrjScr` | Altium Designer 26, docs `Running Scripts` | скрипт-проект; глобальные скрипты — постоянная настройка | для глобальных проектов — при запуске | может менять Preferences | запуск существующего скрипта возможен, но... | отвергнуто: это не загрузка скомпилированного C# Add-in; потребовало бы переписывания диагностики/reflection, запрещено задачей |
| Per-user каталог расширений | — | не найдено в официальной документации; локально отсутствует | — | — | — | — | отвергнуто |
| Подмена загруженной DLL | — | не поддерживаемый механизм | — | потребовалась бы остановка X2 | замена machine-wide файла | **нет** | прямо запрещено; основная DLL заблокирована |
| `obj_run_process` | generic dispatch | EDA bridge | не регистрирует команду | — | — | — | не считается доказательством; не применялся |

`Client:StartServer/StopServer` намеренно **не** рассматривается как механизм загрузки Add-in: по
SDK и документации он стартует уже установленный сервер по имени модуля. Попыток dispatch или
изменения registered server record не было.

`Altium.Designer.HotReload` — единственный найденный официально опубликованный механизм, который
реально меняет C#-код в работающем AD без перезапуска. Однако он работает только внутри заранее
установленного Debug shim: AD загружает shim как обычное расширение, shim читает implementation
из байтов в collectible `AssemblyLoadContext` и открывает pipe текущего пользователя. В текущем
X2 нет ни shim, ни implementation, ни pipe, ни generic `Run`; установка shim потребовала бы
запрещённые permanent registration и перезапуск. Поэтому этот механизм подтверждает принципиальную
возможность hot reload, но не снимает блокер BRIDGE-000026.

## 6. Документальные основания

| Источник | Что подтверждает |
|---|---|
| https://www.altium.com/documentation/altium-designer/installation-management/extending | «Extension folders can be found along the following path: `\ProgramData\Altium\Altium Designer <GUID>\Extensions`»; root содержит `ExtensionsRegistry.xml`; «A restart of Altium Designer is required after an extension finishes installing…» |
| https://www.altium.com/documentation/altium-dxp-developer/server-configuration-files | DLL, `.rcs`, `.ins` сосуществуют в `C:\ProgramData\Altium\Altium Designer {GUID}\Extensions\MyExtension`; Registration основана на INS |
| https://www.altium.com/documentation/altium-developer-center/quick-starts/ad-sdk | «Install the extension, then restart Altium Designer when prompted»; `RegisterCommand` вызывается внутри `InitializeCommands()` уже установленного модуля |
| https://github.com/AltiumDeveloper/ad-extension-template/blob/main/README.md | «Install once, restart AD once»; deploy.ps1 регистрирует расширение в `ExtensionsRegistry.xml`; «AD ignores unregistered folders»; implementation swapping без перезапуска доступен только после установки shim; ADP-версия 26.12 → `net10.0-windows` |
| https://github.com/AltiumDeveloper/ad-extension-template/blob/main/source/Altium.Designer.HotReload/README.md | Debug-only hot reload: implementation загружается из байтов в collectible `AssemblyLoadContext`, current-user named pipe, «Build Debug, install the shim, and restart AD once»; Release не содержит hot reload |
| https://www.nuget.org/packages/Altium.Designer.HotReload/1.0.1 | пакет автором указан `Altium`, версия `1.0.1`, назначение — hot reload расширений AD без перезапуска после первоначальной настройки |
| https://www.altium.com/documentation/altium-dxp-developer/iclient-interface | `StartServer(AModuleName)` «load a server in Altium Designer if it has not already»; `GetServerRecordCount` считает installed servers по `.INS` |
| https://www.altium.com/documentation/altium-designer/scripting/running-scripts | Script system работает с `*.PrjScr` и исходными скриптами, запускается через `File » Run Script`; это не загрузка DLL |
| `...\SDK4.0\CSharp\Source Code\DXP\DXP.Interfaces.cs:1797-1884` | сигнатура `IClient.StartServer(string)`/`StopServer(string,bool)` без параметра пути |
| `...\SDK4.0\CSharp\Source Code\DXP\DXP.Interfaces.cs:8263-8284` | `IDXPExtensionManager` не содержит install/load methods |
| `...\SDK4.0\CSharp\Examples\Message Panel\Source Code\Main.cs:13-39` | `InvokePluginFactory(IClient)` и `RegisterCommand` внутри `PluginServerModule` |
| `...\SDK4.0\CSharp\Source Code\EDP.ComponentLib.Classes.cs:15-23` | `StartServer("UnifiedComponent")` вызывается по имени уже известного сервера |
| EDA `tool_catalog` / `tool_guide` | в поверхности моста нет инструмента live-загрузки DLL/extension; `app_find_menu_command` показывает только `ExtensionManager:ManageExtensions` и `ScriptingSystem:RunScript` |

## 7. Этап D — решение о runtime-проверке

Условие этапа D не выполнено: ни один применимый в текущей сессии механизм не удовлетворяет всем
шести требованиям одновременно. Debug hot reload обеспечивает цикл без перезапуска только после
первоначальной установки и перезапуска, которые здесь запрещены. Поэтому:

- диагностическая DLL **не** собиралась заново в рамках BRIDGE-000026 (ранее собранная из PR #23
  остаётся вне Git);
- команда `ReadBodyIdentityDiagnostics` **не** запускалась;
- `diagnostics.json` **не** создан; путь
  `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000025\diagnostics.json` отсутствует;
- 15 designators не обработаны; 9 Bottom/Top пар не посчитаны.

## 8. Runtime-результаты и доступность полей

| Проверяемое | Результат |
|---|---|
| Команда фактически исполнена | нет |
| `diagnostics.json` | absent |
| Designators | `C37, C34, C36, C35, C25, C29, R50, R43, R44, R53, R25, R36, R26, D9, D8` — 15 запрошены в BRIDGE-000025, **0 обработаны** |
| Bottom/Top пары | **0 из 9** |
| `bodyUniqueId` | `unavailable` |
| library-level `footprintSourceUniqueId` | `unavailable` |
| `IPCB_Model.GetOrigin().X/Y/Z` | `unavailable` (Z по SDK-контракту всё равно `null`) |
| `ambiguous` | не фиксировалось: нет ответа, в котором значение можно было бы оценить |
| Bbox-подстановка | не применялась |
| Фиктивные ID / `(0,0)` | не генерировались |

## 9. Что исследовано, а что реально выполнено

**Реально выполнено (read-only, без изменения Altium):**

1. Проверены `source`, PR #23 и remote-HEADы BRIDGE-000024/000025.
2. Создана ветка от `origin/source` и добавлены текстовые файлы задачи/отчёта.
3. Зафиксированы PID, путь, версия, загруженный модуль, хэши, блокировки файлов.
4. Проверены каталог расширений, `.ins`/`.rcs`, `ExtensionsRegistry.xml`, bootstrap-лог и меню.
5. Проверены per-user каталоги, реестр (только чтение), 449 хэшей конфигурации/расширений и PCB.
6. Проверено отсутствие shim, implementation, hot-reload pipe и generic `Run`.
7. Изучены официальная документация Altium, Altium Developer template/HotReload и SDK 4.0.1.

**Только исследовано и отвергнуто без применения:**

- `ExtensionManager:ManageExtensions` / установка расширения;
- `Altium.Designer.HotReload` / Debug shim с однократным install/restart;
- `ScriptingSystem:RunScript` / любой script-подход;
- `IClient.StartServer` / `StopServer`;
- `IDXPExtensionManager`;
- per-user `Extensions`;
- замена заблокированной DLL;
- `obj_run_process` как доказательство исполнения;
- reflection, Legacy API, DLL injection, memory patching, обход file locks.

## 10. Конкретная причина `blocked`

1. Единственный расширенный каталог — machine-wide ProgramData; per-user каталог не поддерживается
   документально и отсутствует на машине.
2. Текущая сессия держит `EcadMcadBridge.dll` заблокированной; замена требует остановки X2.
3. Штатные механизмы установки расширения требуют постоянной machine-wide регистрации в
   `ExtensionsRegistry.xml` и перезапуска Altium.
4. Поддерживаемый Altium Debug hot reload требует заранее установленного shim и однократного
   перезапуска; текущий X2 использует обычный Release-модуль без shim, implementation и pipe.
5. SDK `StartServer` работает только с уже установленным сервером по имени и не принимает путь к
   другой DLL; EDA-мост не предоставляет вызов для живой загрузки сборки.
6. Скриптовая система не загружает скомпилированный Add-in, а переписывание диагностики на скрипт
   выходит за scope и нарушает запрет на DelphiScript/reflection.
7. `ReadBodyIdentityDiagnostics` отсутствует в загруженной сборке и в постоянной регистрации;
   dispatch неизвестного process name не является исполнением.

## 11. Рекомендованный следующий шаг

Для снятия блокера нужно одно из решений Оператора:

1. подготовить отдельную машину/сессию Altium 26.12.2.9 с изолированным каталогом расширений,
   куда диагностическая сборка или Debug hot-reload shim установлены штатно заранее, и не трогать
   текущий X2; либо
2. выдать новое явное решение, меняющее текущие ограничения (например, контролируемый перезапуск с
   однократной установкой shim или временной подменой двух DLL и безусловным восстановлением
   исходных), если это допустимо; либо
3. оставить BRIDGE-000024 и PR #23 `blocked` и перенести проверку D-024 в среду, где диагностика
   установлена официально.

Пока такое решение не принято, bbox-наблюдения BRIDGE-000023 остаются гипотезой, а Y-sign rule
D-024 — непроверенной.

## 12. Проверки

| Проверка | Результат |
|---|---|
| PR #23 | `open`, `merged=false`, head `e28881486c69f0fb689f3181f9abda38deefe481` |
| PID X2 до/после | `33732` / `33732` |
| Altium version | `26.12.2.9` |
| Загруженная DLL SHA-256 | `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A` |
| Core DLL SHA-256 | `1A106CF0DD31F9D194E760392C460E62C39D4BA078A7DCE6A32953677D567F70` |
| Конфигурация/расширения до-после | `CHANGED=0`, `MISSING=0` |
| PCB SHA-256 | `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F` |
| Открытые диалоги | `dialog_count=0` |
| Debug hot reload prerequisite | shim/implementation/pipe/generic `Run` — не найдены |
| Runtime command | не выполнялась |
| `diagnostics.json` | absent |
| DLL/PCB/STEP/CAD в Git | не добавлялись |
| D-022/D-024, production placement, catalog, Exchange Package, SDK ledger | не изменялись |

## 13. Commit SHA и PR

- Коммит отчёта: `dfdaf311471788f7d2f62785fe357993455ad10a`. Следующий meta-коммит меняет только
  ссылки на SHA в этом разделе и front matter.
- PR для BRIDGE-000026 **не создаётся** по условию задачи.
- PR #23: https://github.com/YOUASSBEE/EcadMcadBridge/pull/23 — `open`, `merged=false`,
  head `e28881486c69f0fb689f3181f9abda38deefe481`.

## 14. История изменений

1. Проверены SHA базы, PR #23 и веток; создана ветка от `origin/source`, добавлен task-файл.
2. Проведено read-only обследование X2, расширений, команд и конфигурации; зафиксированы хэши.
3. Изучены официальная документация Altium и SDK 4.0.1; подтверждено требование перезапуска при
   штатной установке.
4. После первичного отчёта исследование выявило официальный `Altium.Designer.HotReload`; механизм
   проверен по Altium Developer template/NuGet и добавлен в отчёт как неприменимый в текущей сессии
   из-за отсутствия заранее установленного shim и запрета на install/restart.
5. Подготовлен отчёт со статусом `blocked`; runtime-диагностика не выполнялась.
