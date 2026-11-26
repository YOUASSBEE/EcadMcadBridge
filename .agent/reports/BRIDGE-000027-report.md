---
id: BRIDGE-000027
type: report
status: awaiting_operator_approval
from: local-agent
to: operator
created: 2026-11-26T21:14:11+03:00
updated: 2026-11-26T21:20:00+03:00
base: source
base_sha: bdff118b288454be68c7c590d875142620a139e2
branch: task/BRIDGE-000027-controlled-hot-reload
commit: 2edd26a87531347e85cd3814ff43cd3da6314254
pr: none
related:
  - BRIDGE-000024
  - BRIDGE-000025
  - BRIDGE-000026
  - PR-23
---

# BRIDGE-000027 — Controlled Altium Restart and Hot Reload Bootstrap

## 1. Итог

Статус: **`awaiting_operator_approval`**.

Фаза A завершена. Подготовлен контролируемый план однократного перезапуска Altium Designer и
загрузки диагностики PR #23 через официальный Altium Debug Hot Reload. Фаза B **не выполнялась**:
X2 не завершался и не перезапускался, shim не устанавливался, DLL не заменялись, регистрация
Add-in не менялась, PCB не открывался повторно и не сохранялся.

Текущая сессия остаётся рабочей: PID `33732`, Altium Designer `26.12.2.9`, активный документ
`Dev_board.PcbDoc` с `modified=false`, открытых диалогов нет. `diagnostics.json` по-прежнему
отсутствует; runtime-данные для BRIDGE-000024/BRIDGE-000025 не получены.

Для перехода к Фазе B требуется **отдельное явное разрешение Оператора** на:

- штатное завершение и повторный запуск X2;
- временную установку Debug HotReload shim в ProgramData;
- однократную запись shim в `ExtensionsRegistry.xml`;
- запуск диагностической команды внутри перезапущенной сессии Altium.

PR #23 остаётся открытым и не мерженным.

## 2. Изменённые файлы

В Git добавлены только Markdown-файлы:

- `.agent/tasks/BRIDGE-000027.md`
- `.agent/reports/BRIDGE-000027-report.md`

Вне Git созданы только подготовительные артефакты:

- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\backup\` — проверенные копии PCB,
  DLL, `.ins`, `.rcs`, `ExtensionsRegistry.xml` и трёх registry-экспортов;
- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\pr23\` — detached worktree PR #23;
- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\refs\` — локальные копии отчётов
  предыдущих задач для чтения;
- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\meta\` — временная утилита чтения
  метаданных SDK (не используется в CAD и не добавляется в Git).

Ни один DLL, PCB, STEP, лог CAD или бинарный артефакт в Git не добавлен.

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
| `diagnostics.json` | absent |
| Текущий пользователь | `DESKTOP-UCBT4G1\Anton`, `ADMIN_ELEVATED=False` |

### 3.1. Резервирование

В каталоге `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\backup\` созданы и проверены:

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

PCB-копия побайтово совпадает с оригиналом. После подготовительных операций исходные файлы расширения и
`ExtensionsRegistry.xml` сравнены с копиями: `EXT_CHANGED=`, `EXT_MISSING=`, `REG_MATCH=True`.

### 3.2. Диагностическая сборка PR #23

PR #23 содержит только диагностический код; после commit `0b407e3` изменился только report-файл.
Это проверено через `git diff --name-only 0b407e3 e288814`.

Release-сборка из detached worktree PR #23 выполнена с явным путём SDK:

```text
dotnet build ...\src\EcadMcadBridge.Altium\EcadMcadBridge.Altium.csproj -c Release --nologo -p:AltiumSdkDir="E:\Program Files\Altium\AD26\System"
```

Результат: `0 warnings`, `0 errors`. Полученные хэши:

- `EcadMcadBridge.dll` — `48F51E2944FC218BD7C9D6DBE422E90A95043AD8BEA7459522C076C415CEE1DA`
- `EcadMcadBridge.Core.dll` — `60EF27A3BC01C196D68C1577E79D92B13310CB9887B5F17F151CF5DF578CB93D`

В BRIDGE-000025 для diagnosis commit были зафиксированы другие хэши:

- `EcadMcadBridge.dll` — `1C0E17428981217A78CE2976EE84428E06323F5E20120C730A91A2CABF052732`
- `EcadMcadBridge.Core.dll` — `C253FBE3CAD79DF13FF4B216E611168A7B034B9399F2E0E3177CCB494A8A5F62`

Причина различия хэшей при неизменном исходном коде не установлена (возможно, отличающаяся среда
сборки или артефакты obj/bin). Это не блокер: в Фазе B будут собраны и зафиксированы новые
Debug-артефакты HotReload implementation; расхождение заносится в риски.

Release-сборка использовалась только для проверки компилируемости. Официальный HotReload требует
**Debug**-сборки implementation; Release-режим hot reload не поддерживает.

## 4. Обоснование выбора механизма

BRIDGE-000026 показал, что в живой сессии X2 нет shim, implementation, hot-reload pipe и generic
`Run`; заменить заблокированную DLL без остановки X2 невозможно. Единственный официально
опубликованный механизм, который после однократной установки позволяет менять C#-код без
перезапуска, — Altium Debug Hot Reload.

### 4.1. Подтверждённая совместимость

| Проверка | Доказательство | Вывод |
|---|---|---|
| Пакет shim | NuGet `Altium.Designer.HotReload` `1.0.1`, author `Altium`, license `MIT` | использовать `1.0.1`, не `1.0.0` |
| Шаблон | NuGet `Altium.Designer.Extension.Templates` `1.1.0`, author `Altium`, license `MIT` | использовать official template |
| Версия Altium | Official README: `Altium Designer 24 or later` | AD 26.12.2.9 покрывается |
| Target framework | Official README: `.NET SDK 10` for `Altium Designer 26.12 and later`; `net10.0-windows` for 26.12 | Branch TFM `net10.0-windows` корректен |
| SDK | Official docs: `Altium SDK assemblies are always provided by the running AD instance: reference, never copy` | SDK 4.0.1 не может быть заменён; сборка против `E:\Program Files\Altium\AD26\System` |
| Режим | `It exists in Debug builds only: Release builds contain none of it` | shim и implementation собирать в Debug |
| Установка | `Install once, restart AD once`; deploy копирует shim в `Extensions\<Name>` и регистрирует его в `ExtensionsRegistry.xml`, сохраняя `.bak` | требуется однократный restart и постоянная запись в registry |
| Сессия | `AD starts an extension on its first command, so click a MyTools command once per AD session` | после restart нужно выполнить одну команду shim |
| Pipe | `\\.\pipe\<name>.HotReload.<pid>`, `PipeOptions.CurrentUserOnly` | диагностику запускать из-под того же пользователя `Anton` |
| Отказ | `A build that fails to compile, or that AD rejects, never replaces the running one` | плохая сборка не портит предыдущее поколение |
| Uninstall | `close AD, delete Extensions\MyTools, and remove its <Item HRID="MyTools"> element` | откат возможен вручную |

Источники:

- https://raw.githubusercontent.com/AltiumDeveloper/ad-extension-template/main/README.md
- https://raw.githubusercontent.com/AltiumDeveloper/ad-extension-template/main/source/Altium.Designer.HotReload/README.md
- https://www.nuget.org/packages/Altium.Designer.HotReload/1.0.1
- https://www.nuget.org/packages/Altium.Designer.Extension.Templates/1.1.0
- https://raw.githubusercontent.com/AltiumDeveloper/ad-extension-template/main/source/Altium.Designer.Extension.Templates/content/ad-extension/tools/deploy.ps1
- https://raw.githubusercontent.com/AltiumDeveloper/ad-extension-template/main/source/Altium.Designer.HotReload/src/HotReloadHost.cs
- https://www.altium.com/documentation/altium-dxp-developer/system-api

Точный patch `26.12.2.9` и SDK `4.0.1` в документации HotReload поименно не указаны; совместимость
выводится из правила AD 26.12 → `net10.0-windows` и сборки против установленного SDK AD26.

### 4.2. Схема временного диагностического расширения

Для загрузки кода PR #23 без изменения production-кода планируется временное расширение
`BridgeDiag` вне Git, созданное из официального шаблона:

```text
Altium Designer
 └─ BridgeDiag.dll          Debug shim, загружается AD один раз после restart
     └─ BridgeDiag.Impl.dll Debug implementation, hot-reloadable
         ├─ HotEntry.Invoke(...)
         ├─ скопированные исходники диагностики PR #23
         └─ ProjectReference на EcadMcadBridge.Core из worktree PR #23
```

Причина wrapper: pipe-вызов `hotreload.ps1 run` передаёт `Context=null`, а
`AltiumBodyIdentityDiagnosticsReader.Read` требует `IServerDocumentView`. Поэтому:

1. Shim при создании `PluginFactory.InvokePluginFactory(IClient)` сохраняет `IClient`.
2. В shim `Implementation.Invoke` подставляет `client.GetCurrentView()`, если `view == null`.
3. `HotEntry.Invoke` получает view из `env["Context"]` и напрямую вызывает
   `ReadBodyIdentityDiagnosticsHandler.Execute(view, ref parameters)`.

Метаданные установленного SDK подтверждают сигналтуру:
`DXP.IClientHelper::GetCurrentView -> IServerDocumentView; params=IClient`.
Reflection, `dynamic`, Legacy API и undocumented DispId не используются. Production API не меняется.

## 5. План Фазы B — выполнять только после разрешения Оператора

### 5.1. Проверки до остановки X2

1. Повторно зафиксировать PID, exe, версию, активный документ и `dialog_count`.
2. Проверить `modified=false`; при `modified=true` — остановиться и запросить решение.
3. Повторно сверить SHA-256 PCB и резервной копии.
4. Проверить, что все rescue-файлы из раздела 3.1 доступны и хэши совпадают.
5. Зафиксировать список модулей X2 и имена current-user pipe до установки.

### 5.2. Подготовка shim и implementation

1. Создать временный проект из `Altium.Designer.Extension.Templates` `1.1.0` с именем `BridgeDiag`
   и `--altium-system-dir "E:\Program Files\Altium\AD26\System"`.
2. Зафиксировать факт установки/использования NuGet-шаблона; при использовании
   `dotnet new install` записать это как изменение user profile и удалить шаблон при откате.
3. Собрать shim в Debug с `Altium.Designer.HotReload` `1.0.1`.
4. В implementation скопировать диагностические исходники PR #23 и добавить `HotEntry`.
5. Собрать implementation в Debug; зафиксировать SHA-256 `BridgeDiag.dll`,
   `BridgeDiag.Impl.dll`, `EcadMcadBridge.Core.dll`.
6. Не добавлять временный проект, DLL и PDB в Git.

### 5.3. Установка

1. Запустить официальный `tools\deploy.ps1` из временного проекта в Debug.
2. Ожидаемые изменения:
   - создаётся `...\Extensions\BridgeDiag\` с shim, `.ins`, `.rcs`, `.deps.json`;
   - в `...\Extensions\ExtensionsRegistry.xml` добавляется `<Item HRID="BridgeDiag">`;
   - deploy создаёт `ExtensionsRegistry.xml.bak-<stamp>`;
   - при блокировке файлов deploy может переименовать их в `*.old-<stamp>`;
   - создаётся `%LOCALAPPDATA%\Altium.Designer.HotReload\BridgeDiag.log`.
3. HKLM `SOFTWARE\Altium\Builds` используется только для чтения.
4. Текущий пользователь не elevated; ACL ProgramData Extensions и EcadMcadBridge даёт
   `Modify` для `Все`, поэтому elevated-права, вероятно, не нужны. Если запись отказана — остановиться.

### 5.4. Перезапуск

1. Штатно завершить X2; зафиксировать время и старый PID `33732`.
2. Штатно запустить Altium; зафиксировать новый PID и версию.
3. Проверить появление меню/команд `BridgeDiag`.
4. Выполнить одну команду `BridgeDiag` в UI (например, через `app_find_menu_command` +
   `app_click_menu`) для старта расширения и pipe. `obj_run_process` не используется как доказательство.
5. Проверить pipe `BridgeDiag.HotReload.<newpid>` через `hotreload.ps1 ping` и `status -gc`.

Если pipe не появился — диагностику не запускать; перейти к откату или запросить решение.

### 5.5. Запуск диагностики

1. Проверить, что активный PCB всё ещё `Dev_board.PcbDoc`, `modified=false`, SHA-256 совпадает.
2. Выполнить:

```text
powershell -ExecutionPolicy Bypass -File tools\hotreload.ps1 -Name BridgeDiag -Pid <newpid> reload
powershell -ExecutionPolicy Bypass -File tools\hotreload.ps1 -Name BridgeDiag -Pid <newpid> run ReadBodyIdentityDiagnostics "C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000025\diagnostics-request.json"
```

3. Дождаться фактического вывода команды и появления
   `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000025\diagnostics.json`.
4. Проверить JSON, `status`, `sourceDocument`, `package`, `libraries`, `bodies`, `summary`.
5. После получения `diagnostics.json` вычислить 9 Bottom/Top пар отдельным локальным анализом
   из зафиксированных позиций, вращений и model origin; бbox не подставлять.
6. Сохранить логи HotReload и вывод pipe вне Git.

### 5.6. Критерии успеха

Успех фиксируется только при одновременном выполнении:

- `diagnostics.json` существует и является валидным JSON;
- обработаны все 15 designators: `C37, C34, C36, C35, C25, C29, R50, R43, R44, R53, R25, R36, R26, D9, D8`;
- для каждого Bodies заполнены `bodyUniqueId`, library-level `footprintSourceUniqueId`;
- `modelOrigin.X/Y` получены через `IPCB_Model.GetOrigin()`;
- зафиксированы coordinate system и units (`rawUnit=iu`, `millimetersPerIu`);
- STEP asset SHA-256 из package совпадает с ожидаемым;
- `modifiedInEditor=false` и SHA PCB совпадает с оригиналом;
- посчитаны все 9 пар: `C37→C25`, `C34→C25`, `C36→C29`, `C35→C29`, `R50→R25`,
  `R43→R25`, `R44→R36`, `R53→R26`, `D9→D8`;
- нет synthetic ID, `(0,0)` placeholder и bbox-подстановки.

`dispatched=true`, появление меню и `generation > 0` сами по себе успехом не считаются.
Отсутствие Z у `IPCB_Model.GetOrigin()` является ограничением SDK и должно быть явно отражено,
а не заменено bbox.

## 6. Откат

Откат выполняется, если shim несовместим, установка не удалась, pipe не стартовал, сборка
отвергнута, диагностика не выдала `diagnostics.json`, результат противоречит ожиданиям или
Оператор потребовал остановить работу.

Порядок:

1. Сохранить diagnostic log, вывод pipe и созданный `diagnostics.json` вне Git.
2. Штатно завершить Altium.
3. Удалить `...\Extensions\BridgeDiag\` или восстановить каталог из резервной копии.
4. Восстановить `ExtensionsRegistry.xml` из `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000027\backup\ExtensionsRegistry.xml`
   или из `.bak-<stamp>`, созданного deploy.
5. Восстановить исходные `EcadMcadBridge.dll`, `EcadMcadBridge.Core.dll`, `.ins`, `.rcs`,
   `deps.json` из резервной копии и сверить SHA-256 с таблицей раздела 3.1.
6. Если менялись registry-настройки — восстановить соответствующий `.reg` из backup.
7. Если устанавливался NuGet-шаблон — `dotnet new uninstall Altium.Designer.Extension.Templates::1.1.0`.
8. Штатно запустить Altium, зафиксировать PID/версию и подтвердить загрузку исходного Add-in.
9. Проверить `Dev_board.PcbDoc`: `modified=false`, SHA-256 без изменений.

Откат не удаляет пользовательские данные и не перезаписывает оригинальный PCB.

## 7. Неподтверждённые вопросы и риски

1. Точная совместимость HotReload 1.0.1 с patch `26.12.2.9` не заявлена поименно; доказательство
   косвенное через правило AD 26.12 → `net10.0-windows`.
2. HotReload-документация не называет SDK 4.0.1; сборка идёт против assemblies установленного AD26.
3. Настоятельно рекомендуется зафиксировать версии пакетов `1.0.1` / `1.1.0` в Фазе B, чтобы
   исключить непреднамеренное обновление.
4. Различие хэшей Release-сборок BRIDGE-000025 и текущей Фазы A не объяснено.
5. Debug shim открывает current-user pipe, который может выполнять команды расширения внутри AD;
   это локальный development-only механизм и не должен распространяться.
6. WPF удерживает первую загрузку каждой новой сборки (`status -gc` покажет Pending); это
   известное ограничение official package, не признак ошибки диагностики.
7. Установка шаблона NuGet может изменить user profile; изменение и откат должны быть зафиксированы.
8. Текущий процесс не elevated; если ACL ProgramData запретит запись, Фаза B останавливается.
9. Временный wrapper `BridgeDiag` не является частью PR #23 и не будет закоммичен; его исходники
   и хэши должны сохраняться вне Git до завершения проверки.
10. Pair computation для 9 пар выполняется вне diagnostic handler; ошибка в этой post-processing
    логике должна отличаться от отсутствия runtime-данных.

## 8. Проверки

| Проверка | Результат |
|---|---|
| Фаза A без перезапуска X2 | выполнено; PID `33732` до и после |
| PCB `modified=false` | подтверждено до и после Фазы A |
| PCB backup SHA-256 | совпадает с оригиналом |
| Extension backup hashes | совпадают с текущими файлами |
| `ExtensionsRegistry.xml` | совпадает с backup |
| PR #23 Release build | `0 warnings`, `0 errors` |
| HotReload docs / NuGet | проверены; версии `1.0.1` и `1.1.0` |
| SDK metadata | `IClient.GetCurrentView -> IServerDocumentView` подтверждено |
| Shim install / restart | **не выполнялось** |
| Diagnostic command | **не выполнялась** |
| `diagnostics.json` | absent |
| DLL/PCB/STEP/CAD artifacts in Git | нет |
| D-022/D-024, placement engine, catalog, Exchange Package, SDK ledger, production API | не изменялись |
| PR #23 | `open`, `merged=false` |

Окружение: Altium Designer `26.12.2.9`, SDK `4.0.1`, .NET SDK `10.0.404`,
EDA bridge `mcp_server 0.8.2` / bundled script `2026.11.25.1` (по данным BRIDGE-000026).

`dotnet test` для репозитория не запускался: в ветке изменены только Markdown-файлы, production code
не затрагивался. Release-сборка диагностического кода PR #23 выполнена отдельно вне Git.

## 9. Commit SHA и PR

Заполняется после коммита и push ветки:

- branch: `task/BRIDGE-000027-controlled-hot-reload`
- commit content: `2edd26a87531347e85cd3814ff43cd3da6314254`
- commit meta: `<pending>`
- PR для BRIDGE-000027: не создаётся по условию задачи
- PR #23: https://github.com/YOUASSBEE/EcadMcadBridge/pull/23 — `open`, `merged=false`,
  head `e28881486c69f0fb689f3181f9abda38deefe481`

## 10. История изменений

1. Проверены `origin/source`, PR #23 и состояние X2; создана ветка
   `task/BRIDGE-000027-controlled-hot-reload`.
2. Создан task-файл BRIDGE-000027 и выполнены только read-only проверки Фазы A.
3. Собраны резервные копии PCB, extension DLL, `.ins`, `.rcs`, `ExtensionsRegistry.xml` и registry
   outside Git; все хэши проверены.
4. Проверена компилируемость диагностического кода PR #23; зафиксированы хэши сборки и расхождение
   с предыдущей сборкой.
5. Изучены official HotReload docs, NuGet metadata, deploy/pipe scripts и SDK metadata; подготовлен
   конкретный shim/implementation wrapper `BridgeDiag`.
6. Подготовлены сценарии установки, запуска, успеха, отката и список требуемых разрешений.
7. Report переведён в `awaiting_operator_approval`; Фаза B не начиналась.
