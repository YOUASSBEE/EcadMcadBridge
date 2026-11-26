---
id: BRIDGE-000031
type: report
status: blocked
from: local-agent
to: operator
created: 2026-11-26T22:55:56+03:00
updated: 2026-11-26T23:03:00+03:00
base: source
branch: task/BRIDGE-000031-sdk-diagnostic-host
commit: bd987aab86c3205493283edf84fe5a55d862de0f
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/27
related: [BRIDGE-000031]
---

# BRIDGE-000031 — типизированный read-only SDK-диагностический запрос в Altium Add-in

## 1. Итог

Задача выполнялась по тексту постановки Оператора: файл `.agent/tasks/BRIDGE-000031*.md` в
репозитории отсутствует. База зафиксирована после свежего `git fetch origin --prune`:
`origin/source` = `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`. Ветка
`task/BRIDGE-000031-sdk-diagnostic-host` создана от этой базы; PR #25 и PR #26 в неё не
влиты (`git merge-base --is-ancestor` = 1) и по GitHub API остаются `state=open`,
`merged=false`, `base=source`.

**Выбран вариант B: SDK-маршрут подтверждён и минимальная команда реализована;
runtime-подтверждение в текущей уже запущенной Altium заблокировано.**
Вариант A не выбран, потому что новая команда не была загружена и выполнена в CAD: без
поддерживаемого вызова SDK или перезапуска с установкой новой DLL нельзя получить JSON,
PID/version execution evidence и before/after selection state из самой команды. Вариант C не
выбран, потому что статически подтверждённый публичный типизированный маршрут существует:
регистрация команды, активный документ, текущая плата и чтение выделения доступны в SDK 4.0.1.

Добавлена только диагностическая команда `ReadSelectedObjectDiagnostics`. Она читает текущее
выделение платы и записывает JSON в `%TEMP%\EcadMcadBridge\BRIDGE-000031\diagnostics.json`.
Production placement engine, D-024, fail-closed правила, ADR, SDK ledger, публичные контракты
и калибровки не изменялись. Запрещённые вызовы (`LoadFromLibrary`, `LoadCompFromLibrary`,
`SaveToFile`, STEP export, reflection, `dynamic`, внутренние COM/DISP-id) не добавлены.
Пользовательская плата не сохранялась и не изменялась.

## 2. Изменённые файлы

- `.agent/reports/BRIDGE-000031-report.md` — этот отчёт.
- `src/EcadMcadBridge.Altium/Application/ReadSelectedObjectDiagnosticsHandler.cs` — command handler,
  низкоуровневый вызов и доставка пути к JSON.
- `src/EcadMcadBridge.Altium/Application/SelectedObjectDiagnostics.cs` — record-модель,
  детерминированный JSON writer и writer в `%TEMP%`.
- `src/EcadMcadBridge.Altium/Application/SelectedObjectDiagnosticsReader.cs` — read-only typed SDK
  доступ к документу, board, selection, component/body/model полям.
- `src/EcadMcadBridge.Altium/Bootstrap/BridgeServerModule.cs` — регистрация новой команды.
- `src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.ins` — декларация `Command`.
- `src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.rcs` — `PL` и меню
  `PCB → Tools → Read Selected Object Diagnostics`.
- `tests/EcadMcadBridge.Altium.Tests/SelectedObjectDiagnosticsJsonTests.cs` — 4 focused-теста:
  пустое выделение, `unavailable` с причиной, component/body/model link, путь и LF/UTF-8 writer.

Вне Git diagnostic JSON во время проверок не создавался: runtime-команда не выполнялась.
Тест writer создал и удалил только свой временный `diagnostics.json`.

## 3. Принятые решения и обоснование

### 3.1. Часть A — подтверждённый статический контракт SDK

Источник: установленный version-pinned SDK 4.0.1,
`C:\ProgramData\Altium\Altium Designer {B4282C90-18BF-448F-BF1A-9E36AE887190}\Extensions\Altium Developer\SDK4.0\CSharp\Source Code`;
`Version.txt` = `Altium Designer SDK Version 4.0.1`. Хеши ключевых исходников:

| Файл | SHA-256 |
|---|---|
| `DXP\DXP.Classes.cs` | `A3B1CB671FECC04D39BC58BFBA16AFDCE408F1033619F8F80E8863A34908A0A8` |
| `DXP\DXP.Interfaces.cs` | `C9C1B200B242EAB6E5CD73E265FA0951A0A61B2554AA9881A7A937F0502B80F5` |
| `DXP\DXP.Util.cs` | `7D1414F62C09893AAAD83701E718F2D899CDB83B024B528AE7CEFD26719C5326` |
| `EDP.PCB.Interfaces.cs` | `1DB0C6C0379CD85076E80C2D3B9207C1B4B4C6CC82EA1D525AF8961500CCB05F` |

Ниже «подтверждено» означает: сигнатура прочитана в исходниках SDK 4.0.1 и/или зафиксирована в
`.agent/SDK_API_LEDGER.md`. «Предположение» явно помечено и не используется как факт.

| Область | Точная сигнатура / строка | Источник, ledger | Статус |
|---|---|---|---|
| Entry point | `object InvokePluginFactory(IClient a)`; реализация в репозитории `object InvokePluginFactory(IClient client)` | `Examples\Server Template\Source Code\Main.cs:13-25`; `src/.../Bootstrap/PluginFactory.cs:11-23`; ledger C-01, C-02 | Подтверждено |
| Lifecycle модуля | `public ServerModule(IClient argClient, string argName)`; `public ICommandLauncher CommandLauncher { get; }`; `protected virtual void InitializeCommands()`; `protected abstract IServerDocument NewDocumentInstance(string argKind, string argFileName)` | `DXP\DXP.Classes.cs:1668,1682,1976,1937`; ledger C-03…C-09 | Подтверждено |
| Регистрация команды | `public delegate void CommandProc(IServerDocumentView argContext, ref string argParameters);`; `public void RegisterCommand(string argCommandId, CommandProc argCommandProc, GetStateProc argGetStateProc = null)` | `DXP\DXP.Classes.cs:21,450-456`; ledger C-07…C-09 | Подтверждено |
| Host dispatch | `public void LaunchCommand(string argCommandName, ref string argParameters, object argContext)`; `public static void RunCommand(string argIdString, string argSpecialParameter)` | `DXP\DXP.Classes.cs:438-442`; `DXP\DXP.Util.cs:480-489` | Сигнатура подтверждена; что host вызовет новую команду без перезапуска — не подтверждено |
| Декларация install/меню | `.ins` `Command Name = '...'`; `.rcs` `PL ... Command='EcadMcadBridge:...'` + `Insertion`/`Link` | SDK `Server Template`, `Addon Complete`; ledger C-61…C-64, C-71, C-72 | Подтверждено как статический путь |
| Активный документ команды | `static IServerDocument GetOwnerDocument(this IServerDocumentView serverdocumentviewIntf)`; `string GetKind()`; `string GetFileName()`; `bool GetModified()` | `DXP\DXP.Interfaces.cs:13366,320,314,323`; ledger C-84, C-89, C-66 | Подтверждено |
| Текущая плата | `public static IPCB_ServerInterface PCBServer { get; }`; `static IPCB_Board GetCurrentPCBBoard(this IPCB_ServerInterface)` | `EDP.PCB.Classes.cs:7-29`; `EDP.PCB.Interfaces.cs:16555`; ledger C-16…C-19 | Подтверждено |
| Read-only выделение | `int GetState_SelectecObjectCount()`; `static IPCB_Primitive GetState_SelectecObject(this IPCB_Board pcb_boardIntf, int argI)` | `EDP.PCB.Interfaces.cs:2264,13470`; ledger C-120 | Подтверждено. Mutating `SelectedObjects_Clear/Add/BeginUpdate/EndUpdate` намеренно не вызываются |
| Идентификаторы примитива | `string GetState_ObjectIDString()`; `string GetState_Identifier()`; `string GetState_UniqueId()`; `static TObjectId GetState_ObjectID(this IPCB_Primitive)` | `EDP.PCB.Interfaces.cs:324,327,468,11695`; ledger C-120, C-77 | Подтверждено |
| Component-поля | `GetState_Pattern()` L4157; `GetState_SourceUniqueId()` L4187; `GetState_SourceFootprintLibrary()` L4193; `GetState_SourceComponentLibrary()` L4196; `GetState_VaultGUID()` L4379; `GetState_ItemGUID()` L4382; `GetState_ItemRevisionGUID()` L4385 | `EDP.PCB.Interfaces.cs`; ledger C-33, C-36, C-77 для части полей | Сигнатуры подтверждены исходниками. Ledger не менялся по прямому запрету задачи |
| Body/model link | `static IPCB_Component GetState_Component(this IPCB_Primitive)` L11731; `GroupIterator_Create/Destroy` L11876/L11900; `TObjectId.eComponentBodyObject` `EDP.PCB.Types.cs:177`; `static IPCB_Model GetModel(this IPCB_ComponentBody)` L17636; `string IPCB_Model.GetFileName()` L6182; `static T3DModelType GetState_ModelType(this IPCB_Model)` L15827 | `EDP.PCB.Interfaces.cs`, `EDP.PCB.Types.cs`; ledger C-78 | Сигнатуры подтверждены. Итератор создаётся/уничтожается, но не меняет board/selection |
| Result delivery | `%TEMP%\EcadMcadBridge\BRIDGE-000031\diagnostics.json`; пользователю показывается путь через `DXP.Utils.ShowInfo`; ошибки — `ShowWarning` | `src/.../Core/Diagnostics/SnapshotFileWriter.cs:7-22`; `Application/ExportSmokeSnapshotHandler.cs:34-58`; ledger C-69 | Подтверждено как соглашение репозитория; SDK API для «результата» нет |
| Поток выполнения | В SDK нет `ApartmentState`/`STAThread`/`MTAThread`/правил вызова PCB API из фонового потока | поиск по SDK 4.0.1; ledger V-16 | **Не подтверждено.** Handler не создаёт фоновые потоки; предполагается host-поток команды |
| Build/load | `net10.0-windows`, SDK references `Private=false`, `.ins`/`.rcs` копируются и нормализуются в CRLF | `EcadMcadBridge.Altium.csproj:3-44`; `README.md:151-162`; registry item `ExtensionsRegistry.xml:735-760` | Подтверждено; факт загрузки новой DLL в текущий процесс — нет |

Использовать `GetState_SelectedObjects`, `SelectedObjectIterator`, внутренние DispId или
недокументированные COM-интерфейсы не удаётся: таких публичных контрактов в SDK 4.0.1 нет.

### 3.2. Часть B — реализованная команда

Команда названа ровно `ReadSelectedObjectDiagnostics`; caption — `Read Selected Object Diagnostics`.
Она зарегистрирована в `BridgeServerModule.InitializeCommands()` и объявлена в `.ins`/`.rcs`.

Read-only контур:

1. `view.GetOwnerDocument()` → `GetKind()` — document type; всё обёрнуто в `try/catch`.
2. `PCB.GlobalVars.PCBServer` → `GetCurrentPCBBoard()`.
3. `board.GetState_SelectecObjectCount()` до чтения; перебор только по
   `board.GetState_SelectecObject(index)`. Никаких `Clear`, `Add`, `Select`, `DoFileSave`,
   `Board.AddPCBObject`, `SaveToFile`, STEP export.
4. Повторный `GetState_SelectecObjectCount()` после чтения; в JSON пишутся
   `selectionCountBefore`, `selectionCountAfter` и `readStatus`.
5. Для каждого примитива: `objectType`, `publicInstanceId`, `Identifier`; для компонента —
   `Pattern`, `SourceUniqueId`, `SourceFootprintLibrary`, `SourceComponentLibrary`,
   `VaultGuid`, `ItemGuid`, `ItemRevisionGuid`; для body/model — instance/identifier body,
   owner и filename/type модели. Отсутствующее или пустое свойство пишется как
   `{"status":"unavailable","reason":"..."}`, а не как `null` или выдуманное значение.

JSON содержит `schemaVersion`, `measuredAtUtc`, `documentType`, `objectCount`,
`selectionCountBefore/After`, `readStatus`, `objects`. Пустое выделение даёт
`objectCount=0` и пустой массив. Неподдерживаемый тип даёт `Not applicable to this object type`.

Никакие identity-утверждения не делаются: значения `Pattern`, `SourceUniqueId`, GUID и
идентификаторы body/model только переносятся в JSON как наблюдаемые SDK-поля. D-024 и
BRIDGE-000030 вариант C остаются в силе.

### 3.3. Часть C — что проверено статически

- **Только выбранные объекты:** единственный источник записей — индексированный доступ к
  `GetState_SelectecObject(int)`; board iterator для выбора не используется.
- **Выделение/документ не меняются:** в новом коде нет мутирующих selection/board/document API;
  повторный count служит только диагностике. Runtime before/after не измерен (см. §3.4).
- **Нет file side effects кроме diagnostic JSON:** writer пишет только
  `%TEMP%\EcadMcadBridge\BRIDGE-000031\diagnostics.json`, log пишет только код/статус в
  `%TEMP%\EcadMcadBridge\logs\bridge.log`, пользователю показывается путь. `.PcbDoc`, package
  root, STEP и workspace не трогаются.
- **Пустое/неподдерживаемое/unavailable:** покрыто 4 юнит-тестами; парсер JSON проверяет
  status/reason и наличие полей.
- **Запрещённые API/зависимости:** grep по новым файлам не нашёл `LoadFromLibrary`,
  `LoadCompFromLibrary`, `SaveToFile`, `DoFileSave`, `ExportToStep`, `dynamic`, reflection,
  `DispId`, `Internal_`, `BoardIterator_Create`, `SelectedObjects_Clear/Add`. Новых
  PackageReference/ProjectReference нет.
- **Бинарные артефакты:** в diff нет DLL/PDB/PCB/STEP/SLDPRT/ZIP.

### 3.4. Часть D — почему runtime заблокирован

Наблюдаемая среда: Altium PID `27116`, `X2.EXE` из `E:\Program Files\Altium\AD26\X2.EXE`,
Altium `26.12.2.9`, script bridge `2026.11.25.1`, EDA MCP `0.8.2`, `app_ping.success=true`.

- EDA `tool_catalog`/`tool_guide` (backend `altium`) не предоставляют инструмент загрузки
  скомпилированной SDK-DLL в уже запущенный процесс и не предоставляют произвольный вызов
  публичного SDK-метода. `obj_run_process` — fire-and-forget `RunProcess` и явно запрещает
  scripting family; `app_run_ui_command` работает только по известным menu path и для
  неизвестных путей отказывается.
- Контрольный `app_find_menu_command(query="Save All")` вернул 4 hits, тогда как
  `Export Board Snapshot`, `Export Smoke Snapshot` и `Read Selected Object Diagnostics`
  вернули `count=0`. То есть инструмент поиска работает, но команд расширения в текущем меню нет.
- Установленный `...\Extensions\EcadMcadBridge\EcadMcadBridge.dll` имеет SHA-256
  `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A` и LastWriteTimeUtc
  `26.11.2026 13:00:05`; свежесобранный из этой ветки —
  `362FF809472374BC9F04FFA2010C9589ADF63855997E34D0F010BCCCB7AC729A`. Запись
  `ExtensionsRegistry.xml` (`HRID=EcadMcadBridge`, GUID
  `f2c9b1a4-6d5e-4c8b-9a31-7e0f2b8d4c10`, `Status=0`) уже есть, но загрузка новой DLL и
  registration command требуют копирования файлов и перезапуска Altium по `README.md:151-162`.
- Постановка задачи запрещает restart/close Altium, правки registry/`ExtensionsRegistry.xml`,
  shim/handler/workspace disruption без явного разрешения Оператора. Разрешения нет.

Следовательно новая команда не выполнялась: diagnostic JSON не создан, его SHA-256 и фактические
execution evidence недоступны (`unavailable`). Пользовательская плата
`D:\Git\Projects\mdm15-1v05m_electric\Dev_board.PcbDoc` имела SHA-256
`0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F` до и после работ;
документ не сохранялся и не закрывался. Registry, `ExtensionsRegistry.xml`, shim, workspace,
`Altium_API.PrjScr` и скрипты EDA не изменялись.

### 3.5. Формулировка варианта B

- **A** требует runtime-подтверждения: созданного JSON, его SHA-256, Altium PID/version во
  время выполнения и неизменного документа до/после. Этого нет.
- **C** требует отсутствия подтверждённого SDK-маршрута. Маршрут подтверждён и код добавлен.
- **B** выбран: статический контракт и реализация готовы, но runtime-проверка заблокирована
  средой и ограничением задачи без operator-approved reload/restart.

## 4. Неподтверждённые вопросы и риски

- Требования потока/COM-апартамента для PCB API остаются открытыми (ledger V-16). Handler не
  создаёт фоновый поток, но это не заменяет CAD-проверку.
- Не измерены runtime-значения полей и факт появления пункта меню после перезапуска.
  Статусы `unsupported`/`unavailable` в JSON — проектная обработка, не CAD-измерение.
- Для полной проверки требуется одобренная установка: скопировать DLL/deps/.ins/.rcs в каталог
  расширения и перезапустить Altium; до этого команда в текущем процессе недоступна.
- Файл `diagnostics.json` имеет фиксированное имя и перезаписывается при каждом запуске;
  если нужно сохранить историю, копируйте его вручную.
- Часть новых component/body/model сигнатур подтверждена исходниками SDK, но не добавлена в
  `SDK_API_LEDGER.md`: задача прямо запрещает менять ledger. Если политика требует записи,
  нужна отдельная задача/решение.
- Handler показывает путь через модальный info dialog; это принятое соглашение репозитория,
  но не SDK result API и не проверено в CAD.

## 5. Проверки

| Проверка | Результат |
|---|---|
| `git fetch origin --prune` | выполнено; `origin/source` = `ea817c9b1e52b6a86e5eb541cf814a578887a7d5` |
| Ветка | `task/BRIDGE-000031-sdk-diagnostic-host`, upstream `origin/source`, создана от base |
| PR #25 / #26 | `state=open`, `merged=false`, base `source`; коммиты не являются предками этой ветки |
| `dotnet build -c Release` | успешно, 0 предупреждений, 0 ошибок |
| `dotnet test -c Release --no-build` | успешно: Core 229, Altium 199, SolidWorks 94 — всего 522 passed, 0 failed |
| `git diff --check` | чистый |
| Forbidden API/dependency grep | новых совпадений в изменённых `.cs` нет |
| Binary/CAD artifacts in diff | нет |
| SDK | Altium SDK 4.0.1; `EDP.PCB.Interfaces.cs` SHA-256 `1DB0C6C0379CD85076E80C2D3B9207C1B4B4C6CC82EA1D525AF8961500CCB05F` |
| .NET | `dotnet --version` = `10.0.404`; `global.json` = `10.0.100` + `rollForward: latestFeature` |
| Altium / bridge | PID `27116`; AD `26.12.2.9`; script `2026.11.25.1`; MCP `0.8.2`; `app_ping.success=true` |
| Runtime команды | **заблокировано**: DLL не загружена/команда не найдена в меню; JSON SHA-256 `unavailable` |
| Пользовательский PCB | SHA-256 до/после `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F`; не сохранялся |

## 6. Commit SHA и ссылка на PR

- Коммит, отправленный на ревью: `bd987aab86c3205493283edf84fe5a55d862de0f`.
- PR: <https://github.com/YOUASSBEE/EcadMcadBridge/pull/27> — открыт, `state=open`,
  `merged=false`, base `source` @ `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`, head branch
  `task/BRIDGE-000031-sdk-diagnostic-host`. Коммит, отправленный на ревью, указан выше;
  последующие метаданные PR не меняют проверенный код.

## 7. История изменений

1. Первая версия отчёта: Part A evidence, Part B реализация, Part C статические проверки,
   Part D blocked и выбор варианта B.
2. После создания PR отдельным коммитом добавлены URL PR #27 и reviewed commit SHA.
3. Следующим метаданным коммитом уточнено, что SHA в §6 относится к review-коммиту, а не к
   меняющемуся head PR.
