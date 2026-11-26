---
id: BRIDGE-000024
type: report
status: blocked
from: local-agent
to: operator
created: 2026-11-26T18:06:48+03:00
updated: 2026-11-26T20:21:39+03:00
base: source
branch: task/BRIDGE-000024-readonly-diagnostics
commit: 608a9c1dcc177121ad5f44ecdba283bc50bed14c
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/23
related: [BRIDGE-000024]
---

# BRIDGE-000024 — read-only D-024 diagnostics

## 1. Итог

Реализована read-only диагностическая команда Altium Add-in
`EcadMcadBridge:ReadBodyIdentityDiagnostics`. Она читает активную PCB через существующий
`AltiumBoardSnapshotAdapter`, перебирает `IPCB_ComponentBody`, для каждого body возвращает:

- `footprintSourceUniqueId` (library-level) и статус доступности;
- `bodyUniqueId` (только если library body ID подтверждён совпадением с placed body ID),
  `bodyOrdinal` и placed/library ID для трассировки;
- `modelOriginX/Y` из фактического `IPCB_Model.GetOrigin()` с явным диагностическим полем
  `MODEL_ORIGIN_Z_UNAVAILABLE` (SDK возвращает `ICoordPoint`, только X/Y);
- component position, rotation, side;
- STEP asset path/SHA-256 из squirrel-existing BRIDGE-000023 package с проверкой manifest;
- diagnostics для unavailable/ambiguous/non-proven значений.

Команда **не** экспортирует STEP, **не** пишет `.PcbDoc`, **не** подставляет bbox-координаты
вместо `ModelOrigin`, **не** генерирует фиктивные ID. Production-код, D-022/D-024, Exchange
Package, catalog, transform engine, SDK ledger и `.PcbDoc` не изменены. Bottom placement не
активирован.

**Блокер.** Оператор выбрал временную загрузку собранного Add-in в **текущую** сессию Altium
26.12.2.9 с запуском `ReadBodyIdentityDiagnostics`, без изменения PCB, реестра и постоянной
конфигурации. Проверка показала, что это требование выполнить нельзя, не выйдя за границы
разрешения:

- в текущей сессии загружен старый Add-in: `bridge.log` содержит
  `BOOTSTRAP_COMMANDS commands=ExportSmokeSnapshot,ExportBoardSnapshot`, новой команды нет;
- `app_find_menu_command` для `Bridge` / `Export Board` / `EcadMcadBridge` возвращает `count=0`;
- `obj_run_process` для `EcadMcadBridge:ReadBodyIdentityDiagnostics` сообщает только
  `dispatched=true`, `unlisted_process` и явно предупреждает, что неизвестный process name
  Altium принимает без ошибки; `diagnostics.json` не создан;
- EDA `tool_catalog` / `tool_guide` не содержат поддерживаемой команды загрузки или перезагрузки
  DLL в уже работающий Altium; поиск по `*.ins` / `*.rcs` каталога AD26 не нашёл
  `StartServer` / `StopServer` / `Reload`; `Client:StartServer` / `Client:StopServer` из `IClient`
  не являются dispatchable process в этой сборке и после попытки не дали нового
  `BOOTSTRAP_FACTORY`;
- установленная DLL заблокирована процессом X2 (`FileShare.None` → `LOCKED`), поэтому замена
  сборки требует остановки X2, то есть выхода из текущей сессии. Открытый
  `Dev_board.PcbDoc` помечен `modified=true` (правки были до нас); сохранение или закрытие не
  выполнялись;
- `.ins` и `.rcs` установленного расширения объявляют только две старые команды; добавление новой
  потребовало бы переустановки/постоянной регистрации, что Оператор запретил без нового решения.

Попытка сохранить копию через `File|Save Copy As` была остановлена на модальном диалоге
`Confirm Save Locally for (2) Modified Documents` и отменена кнопкой `Cancel`; файл на диске и
состояние документа не изменены, `modified=true` сохранено. После этого диалогов нет.

Все поля, зависящие от фактического вызова SDK на реальной плате, остаются `unavailable`; D-024
§5 на 9 парах не проверен. Установленные DLL остались исходными:
`EcadMcadBridge.dll` — `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A`,
`EcadMcadBridge.Core.dll` — `1A106CF0DD31F9D194E760392C460E62C39D4BA078A7DCE6A32953677D567F70`.
DLL из текущей ветки (`EcadMcadBridge.dll` — `DF75651010B3CC2680E23D9123D12EAFA531860CD31AB292A0CF56901BDC17BD`,
`EcadMcadBridge.Core.dll` — `13B8848A8DB6044A09F580C12E4E959C5C2FB5F8D8BBC4680F3C0CA79DCD7D11`) в каталог
расширения **не** копировались.

## 2. Изменённые файлы

- `src/EcadMcadBridge.Altium/Adapter/AltiumBodyIdentityDiagnosticsReader.cs` — новый read-only
  reader и JSON-формат ответа.
- `src/EcadMcadBridge.Altium/Application/ReadBodyIdentityDiagnosticsHandler.cs` — command
  handler; параметр команды — абсолютный путь к request JSON, результат пишется в `outputPath`.
- `src/EcadMcadBridge.Altium/Bootstrap/BridgeServerModule.cs` — регистрация команды
  `EcadMcadBridge:ReadBodyIdentityDiagnostics` рядом с Export-командами.
- `.agent/reports/BRIDGE-000024-report.md` — этот отчёт.

Не добавлены: DLL, STEP, PCB, library-файлы, секреты, данные заказчика.

## 3. Найденные SDK-интерфейсы и доказательства

Источник: `E:\Program Files\Altium\AD26\System\Altium.SDK.Interfaces.dll`, декомпиляция
`ilspycmd` 11.2.0.10050 в
`C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000024\sdk\`. Использованы только публичные
helper-методы/интерфейсы SDK, без reflection, `dynamic`, Legacy API, DelphiScript и DispId.

| Возможность | Подтверждённый член SDK | Файл доказательства |
|---|---|---|
| Footprint pattern / library hint placed component | `PCB.IPCB_Component.GetState_Pattern()`, `GetState_SourceFootprintLibrary()`, `GetState_SourceUniqueId()` | `PCB_IPCB_Component.cs` |
| Library-level footprint ID | `PCB.IPCB_Primitive.GetState_UniqueId()`, наследуется `IPCB_LibComponent`/`IPCB_ComponentBody` | `PCB_IPCB_Primitive.cs`, `PCB_IPCB_LibComponent.cs`, `PCB_IPCB_ComponentBody.cs` |
| Placed body → model | `PCB.IPCB_ComponentBody.GetModel()` через `IPCB_ComponentBodyHelper` | `PCB_IPCB_ComponentBodyHelper.cs` |
| Actual model origin | `PCB.IPCB_Model.GetOrigin()` через `IPCB_ModelHelper`; возвращает `CoordPoint` (X/Y) | `PCB_IPCB_ModelHelper.cs`, `PCB_CoordPoint.cs` |
| Открытие PcbLib по пути | `PCB.IPCB_ServerInterface.GetPCBLibraryByPath()`, `LoadPCBLibraryByPath()`, `DestroyPCBLibrary()` | `PCB_IPCB_ServerInterface.cs` |
| Перебор footprint в PcbLib | `PCB.IPCB_Library.GetComponent(int)`, `PCB.IPCB_LibraryHelper.GetComponentByName()` | `PCB_IPCB_Library.cs`, `PCB_IPCB_LibComponentHelper.cs` |
| Перебор body внутри footprint | `PCB.IPCB_GroupHelper.GroupIterator_Create()` на `IPCB_LibComponent` | `PCB_IPCB_GroupHelper.cs` |
| Дополнительно investigated | `EDP.ILibraryComponentIdentifier.GetFootprintGUID()` | `EDP_ILibraryComponentIdentifier.cs` — не применён: не найден поддерживаемый путь от placed PCB component к этому identifier без undocumented/internal вызовов |

`IPCB_Model.GetOrigin()` не имеет Z: `ICoordPoint` содержит только `X`/`Y`. Поэтому
`modelOrigin.zIu/zMm` всегда `null` и сопровождается диагностикой; это не подстановка нуля.

## 4. Реализованный API

### 4.1. Вызов

```text
Process: EcadMcadBridge:ReadBodyIdentityDiagnostics
Parameter: <absolute path to request JSON>
```

Request (пример, файл создан вне Git в
`C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000024\diagnostics-request.json`):

```json
{
  "schemaVersion": "bridge-readonly-diagnostics/1",
  "packageDirectory": "C:\\Users\\Anton\\AppData\\Local\\Temp\\opencode\\bridge-000023\\package",
  "libraryPaths": [
    "D:\\Git\\gortpowerlib\\footprints\\Capacitor - MLCC\\CAP 0402_1005.PCBLIB",
    "D:\\Git\\gortpowerlib\\footprints\\Resistor- Chip\\RES 0402_1005.PCBLIB",
    "D:\\Git\\gortpowerlib\\footprints\\SOD\\SOD323.PcbLib"
  ],
  "designators": ["C37","C34","C36","C35","C25","C29","R50","R43","R44","R53","R25","R36","R26","D9","D8"],
  "outputPath": "C:\\Users\\Anton\\AppData\\Local\\Temp\\opencode\\bridge-000024\\diagnostics.json"
}
```

### 4.2. Формат ответа (схема, не измеренные значения)

```json
{
  "schemaVersion": "bridge-readonly-diagnostics/1",
  "status": "partial",
  "units": {
    "rawUnit": "iu",
    "millimetersPerIu": 2.54e-06,
    "componentPositionCoordinateSystem": "pcb board coordinates, raw IU; rotation in degrees",
    "modelOriginCoordinateSystem": "IPCB_Model.GetOrigin() model-local X/Y, raw IU; GetOrigin has no Z component"
  },
  "package": { "verified": true, "filesVerified": 24, "filesListed": 24 },
  "bodies": [{
    "designator": "C37",
    "bodyOrdinal": 0,
    "footprintSourceUniqueId": null,
    "footprintSourceUniqueIdStatus": "unavailable",
    "bodyUniqueId": null,
    "bodyUniqueIdStatus": "unavailable",
    "placedBodyUniqueId": null,
    "libraryBodyUniqueId": null,
    "modelOrigin": {
      "xIu": null, "yIu": null, "zIu": null,
      "xMm": null, "yMm": null, "zMm": null,
      "status": "unavailable", "units": "iu"
    },
    "stepAsset": { "assetPath": "assets/C37.step", "sha256": "...", "verified": true },
    "diagnostics": ["MODEL_ORIGIN_Z_UNAVAILABLE: IPCB_Model.GetOrigin returns ICoordPoint (X/Y only)."]
  }]
}
```

Показанные `null` — ожидаемый fail-closed результат **`unavailable`**, а не измеренные значения
платы. Фактический response-файл не создавался, потому что команда не была загружена в Altium.

### 4.3. Fail-closed правила

- `footprintSourceUniqueId` заполняется только при однозначном library match и непустом
  `IPCB_LibComponent.GetState_UniqueId()`.
- `bodyUniqueId` заполняется только когда library body ID найден **и** placed body ID непуст и
  совпадает с ним. Если placed ID пуст — `unavailable`; если отличается — `ambiguous`.
- `bodyOrdinal` вычисляется только для components, успешно сопоставленных с snapshot.
- `modelOrigin` заполняется только из `IPCB_Model.GetOrigin()`. При отсутствии модели или
  origin — `unavailable`; Z всегда `unavailable`.
- STEP asset принимается только при совпадении SHA-256 с manifest BRIDGE-000023; path outside
  package root отвергается.

## 5. Проверка D-024 на 9 парах

Проверка **не выполнена**: для всех 9 пар требуемые поля недоступны без запуска новой команды в
Altium. Использовать bbox-corner proxy из BRIDGE-000023 запрещено определением `ModelOrigin`.

| Bottom | Top ref | Footprint | θ bottom | `footprintSourceUniqueId` | `bodyUniqueId` | `modelOrigin` | D-024 e |
|---|---|---|---:|---|---|---|---|
| C37 | C25 | CAP 0402_1005 | 180 | unavailable | unavailable | unavailable | не вычислено |
| C34 | C25 | CAP 0402_1005 | 180 | unavailable | unavailable | unavailable | не вычислено |
| C36 | C29 | CAP 0402_1005 | 270 | unavailable | unavailable | unavailable | не вычислено |
| C35 | C29 | CAP 0402_1005 | 270 | unavailable | unavailable | unavailable | не вычислено |
| R50 | R25 | RES 0402_1005 | 0 | unavailable | unavailable | unavailable | не вычислено |
| R43 | R25 | RES 0402_1005 | 0 | unavailable | unavailable | unavailable | не вычислено |
| R44 | R36 | RES 0402_1005 | 90 | unavailable | unavailable | unavailable | не вычислено |
| R53 | R26 | RES 0402_1005 | 180 | unavailable | unavailable | unavailable | не вычислено |
| D9 | D8 | SOD323 | 180 | unavailable | unavailable | unavailable | не вычислено |

Для справки, BRIDGE-000023 зафиксировал bbox-derived `e` CAP `[0,+0.524002]` mm, RES
`[0,+0.499999]` mm, SOD323 `[-0.00000254,+1.25000004]` mm и no-flip residual ≤0.000006 mm;
эти данные **не** являются проверкой D-024 и в настоящем отчёте не переиспользуются как
`ModelOrigin`.

## 6. Доступность всех требуемых полей

| Поле | SDK-путь есть | Реализовано в API | Проверено на реальной плате |
|---|---|---|---|
| `footprintSourceUniqueId` | да, `IPCB_LibComponent.GetState_UniqueId()` | да, fail-closed | нет — команда не загружена. `obj_query` даёт placed `SourceUniqueId` (`\CMIFTJDY\PTRNHLDH` для C37), но это schematic source ID, не library-level ID |
| `bodyUniqueId` | да, `IPCB_ComponentBody.GetState_UniqueId()` + library body enumeration | да, требует совпадения placed/library | нет — `obj_query` для `eComponentBodyObject` C37 возвращает пустой `UniqueID`; library body ID не прочитан runtime |
| `bodyOrdinal` | через library/board group iteration | да | нет |
| `modelOriginX/Y` | да, `IPCB_Model.GetOrigin()` | да | нет — `obj_query` не имеет свойства `ModelOrigin`; `lib_get_footprint_primitives` / `lib_set_3d_body` дают только bbox-центр и Z/rotation, не `GetOrigin()` |
| `modelOriginZ` | нет в SDK для этого вызова | `null` + diagnostic | недоступно по контракту SDK |
| coordinate system / units | да, raw IU + `AltiumUnits.MillimetersPerIu` | да | да (static conversion) |
| STEP asset SHA-256 | package manifest + `PackageFileHasher` | да | да (24/24 файлов) |
| diagnostics unavailable/ambiguous | да | да | да (дизайн кода) |

Дополнительная проверка доступных EDA-инструментов не дала замены: `obj_query` для
`eComponentObject` C37 возвращает `UniqueID=SQMHHCOO`, `SourceUniqueId=\CMIFTJDY\PTRNHLDH`,
`SourceFootprintLibrary=...CAP 0402_1005.PCBLIB`, но это placed component ID и schematic source
ID; они не являются ни `bodyUniqueId`, ни library footprint `footprintSourceUniqueId`. Bbox-центр
STEP body (`center_x_mm=0.000081`, `center_y_mm=0.000079`) также не является model origin. Эти
значения прочитаны только для фиксации доступности; ни одно из них не использовано как вход
D-024.

## 7. Неподтверждённые вопросы и риски

1. **Главный блокер — новое решение Оператора (эскалация).** Выбранный маршрут «временная
   загрузка в текущую сессию» технически недоступен: замена DLL невозможна, пока X2 держит файл
   заблокированным, а поддерживаемый EDA/Altium command surface не умеет перезагрузить сервер без
   остановки процесса. Постоянная регистрация/реестр не изменялись и не планировались.
   Вопрос: разрешить ли выйти за условие «текущая сессия» и выполнить контролируемый перезапуск
   Altium ради runtime-проверки?
   - **Вариант A (рекомендуется, если нужны фактические данные сейчас):** один раз закрыть X2,
     временно подменить только `EcadMcadBridge.dll` и `EcadMcadBridge.Core.dll` сборками из ветки
     `task/BRIDGE-000024-readonly-diagnostics`, открыть тот же проект, выполнить Diagnostics,
     восстановить исходные DLL по SHA-256. Перед закрытием — явно сохранить копию
     `Dev_board.PcbDoc` во временный файл вне Git или получить подтверждение, что несохранённые
     правки можно не сохранять. `.ins`, `.rcs`, `ExtensionsRegistry.xml`, реестр и `.PcbDoc` не
     трогать; DLL/CAD-артефакты в Git не добавлять.
   - **Вариант B:** оставить BRIDGE-000024 заблокированным и перенести runtime-проверку в
     отдельную задачу с уже установленной диагностической сборкой.
   - **Вариант C:** использовать bbox proxy как `ModelOrigin`. Не рекомендуется: нарушает D-024 и
     уже показал расхождение с полным STEP.
2. Стабильность library-level `footprintSourceUniqueId` между экземплярами не подтверждена
   runtime-сравнением. Placed `SourceUniqueId` из BRIDGE-000023 instance-specific и не является
   заменой.
3. Стабильность `bodyUniqueId` не подтверждена: `obj_query` для placed bodies возвращает пустой
   `UniqueID`; API требует совпадения с library body ID, но это не проверено.
4. `IPCB_Model.GetOrigin()` возвращает только XY; `modelOriginZ` остаётся недоступным по контракту
   SDK. Если D-024 нуждается в Z, требуется отдельное решение/ADR; в этой задаче Z не выдумывался.
5. Provenance STEP из BRIDGE-000023 (`SdkExport` / `PlacedBodyEffective`) остаётся
   неподтверждённой; эта задача её не меняла.
6. Несохранённые правки `Dev_board.PcbDoc` (`modified=true`) не были сохранены или отброшены;
   диалог подтверждения сохранения отменён. Любой перезапуск требует отдельного решения об этих
   правках.

## 8. Проверки

- Build: `dotnet build -c Release src/EcadMcadBridge.Altium/EcadMcadBridge.Altium.csproj` —
  успешно, `warnings=0 errors=0`.
- Tests: `dotnet test -c Release tests/EcadMcadBridge.Altium.Tests/EcadMcadBridge.Altium.Tests.csproj --nologo`
  — `195 passed, 0 failed, 0 skipped`.
- Package checksums: BRIDGE-000023 research package, `files=24 bad=[]`; STEP не экспортировались
  повторно.
- CAD runtime smoke: **не выполнен** — новая команда не загружена в Altium; причина и варианты
  в §7. Не выполнялся SOLIDWORKS smoke (по условию задачи).
- Попытка runtime-загрузки: `obj_run_process` для `EcadMcadBridge:ReadBodyIdentityDiagnostics`
  вернул `dispatched=true`, но `unlisted_process`; `diagnostics.json` отсутствует; `BOOTSTRAP_COMMANDS`
  в логе остались прежними. Попытка `File|Save Copy As` отменена, PCB не изменён.
- Состояние CAD после проверок: X2 PID `32260`, активный документ `Dev_board.PcbDoc`,
  `modified=true`, открытых диалогов нет. Установленные DLL и реестр не изменялись.
- Среда: AD `26.12.2.9`, EDA bridge script `2026.11.25.1`, .NET 10, `net10.0-windows` (Altium).
- `git diff --check`: см. финальный коммит; секреты/DLL/CAD-данные в Git не добавлены.

## 9. Commit SHA и PR

- Commit, на котором открыт PR: `b5d082c80098160d71d3921d3706796e047c4050`.
- Финальный commit: `608a9c1dcc177121ad5f44ecdba283bc50bed14c` (обновление отчёта после
 runtime-расследования; будет заменён SHA этого коммита после push).
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/23 — открыт из
  `task/BRIDGE-000024-readonly-diagnostics` в `source`; `state=open`, `merged=false`, head
  `608a9c1dcc177121ad5f44ecdba283bc50bed14c`. PR не мержится по условию задачи.
- URL PR и финальный commit SHA добавлены отдельным коммитом после создания PR.

## 10. История изменений

1. Собрано доказательство SDK по `Altium.SDK.Interfaces.dll`.
2. Добавлены reader, handler и регистрация команды; исправлены fail-closed условия для body ID и
   `footprintSourceUniqueId`.
3. Build/tests/checksums выполнены; CAD runtime заблокирован отсутствием загруженной команды.
4. Оператор выбрал временную загрузку Add-in. Проверены install/load route, `obj_run_process`,
   `app_find_menu_command`, блокировка DLL процессом X2 и доступность полей через EDA tools.
   Загрузка в текущую сессию без перезапуска/постоянной регистрации признана невозможной; PCB и
   установленные DLL не изменены; отчёт переведён в новую эскалацию §7.
