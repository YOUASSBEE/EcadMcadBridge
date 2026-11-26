---
id: BRIDGE-000024
type: report
status: blocked
from: local-agent
to: operator
created: 2026-11-26T18:06:48+03:00
updated: 2026-11-26T18:06:48+03:00
base: source
branch: task/BRIDGE-000024-readonly-diagnostics
commit: b5d082c80098160d71d3921d3706796e047c4050
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

**Блокер.** Диагностическая команда собрана, но не была выполнена в Altium: установленный в
текущей сессии Add-in не содержит новой команды (EDA `app_find_menu_command` для `Bridge` /
`Export Board` возвращает `count=0`), а `obj_run_process` для неизвестного process name
безопасно ничего не делает. Установка/перезагрузка Add-in и правка реестра требуют отдельного
решения Оператора по AGENTS.md, поэтому в рамках задачи не выполнялись. Все поля, зависящие от
фактического вызова SDK на реальной плате, остаются `unavailable`; D-024 §5 на 9 парах не
проверен.

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
| `footprintSourceUniqueId` | да, `IPCB_LibComponent.GetState_UniqueId()` | да, fail-closed | нет — команда не загружена |
| `bodyUniqueId` | да, `IPCB_ComponentBody.GetState_UniqueId()` + library body enumeration | да, требует совпадения placed/library | нет |
| `bodyOrdinal` | через library/board group iteration | да | нет |
| `modelOriginX/Y` | да, `IPCB_Model.GetOrigin()` | да | нет |
| `modelOriginZ` | нет в SDK для этого вызова | `null` + diagnostic | недоступно по контракту SDK |
| coordinate system / units | да, raw IU + `AltiumUnits.MillimetersPerIu` | да | да (static conversion) |
| STEP asset SHA-256 | package manifest + `PackageFileHasher` | да | да (24/24 файлов) |
| diagnostics unavailable/ambiguous | да | да | да (дизайн кода) |

## 7. Неподтверждённые вопросы и риски

1. **Главный блокер (эскалация).** Нужно разрешение Оператора на установку/перезагрузку
   EcadMcadBridge Add-in в текущем Altium 26.12.2.9, чтобы выполнить
   `EcadMcadBridge:ReadBodyIdentityDiagnostics` на открытом `Dev_board.PcbDoc`.
   - Вариант A (рекомендуется): разрешить временную загрузку собранного Add-in без изменения
     `source` и без коммита DLL; запустить Diagnostics, сохранить response вне Git, затем
     повторить таблицу §5 и проверить D-024 §5.
   - Вариант B: оставить BRIDGE-000024 заблокированным и перенести runtime-проверку в отдельную
     задачу с уже установленным Add-in.
   - Вариант C: использовать bbox proxy как `ModelOrigin`. Не рекомендуется: нарушает D-024 и
     уже показал расхождение с полным STEP.
2. Стабильность library-level `footprintSourceUniqueId` между экземплярами не подтверждена
   runtime-сравнением. Placed `SourceUniqueId` из BRIDGE-000023 instance-specific и не является
   заменой.
3. Стабильность `bodyUniqueId` не подтверждена: `obj_query` для placed bodies возвращает пустые
   `UniqueID`/`SourceUniqueId`; API требует совпадения с library body ID, но это не проверено.
4. `IPCB_Model.GetOrigin()` готовит только XY; D-024 может требовать Z — поле остаётся
   недоступным. Если D-024 нуждается в Z, требуется отдельное решение/ADR; в этой задаче Z не
   выдумывался.
5. Provenance STEP из BRIDGE-000023 (`SdkExport` / `PlacedBodyEffective`) остаётся
   неподтверждённой; эта задача её не меняла.

## 8. Проверки

- Build: `dotnet build -c Release src/EcadMcadBridge.Altium/EcadMcadBridge.Altium.csproj` —
  успешно, `warnings=0 errors=0`.
- Tests: `dotnet test -c Release tests/EcadMcadBridge.Altium.Tests/EcadMcadBridge.Altium.Tests.csproj --nologo`
  — `195 passed, 0 failed, 0 skipped`.
- Package checksums: BRIDGE-000023 research package, `files=24 bad=[]`; STEP не экспортировались
  повторно.
- CAD runtime smoke: **не выполнен** — новая команда не загружена в Altium; причина и варианты
  в §7. Не выполнялся SOLIDWORKS smoke (по условию задачи).
- Среда: AD `26.12.2.9`, EDA bridge script `2026.11.25.1`, .NET 10, `net10.0-windows` (Altium).
- `git diff --check`: см. финальный коммит; секреты/DLL/CAD-данные в Git не добавлены.

## 9. Commit SHA и PR

- Commit, на котором открыт PR: `b5d082c80098160d71d3921d3706796e047c4050`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/23 — открыт из
  `task/BRIDGE-000024-readonly-diagnostics` в `source`; `state=open`, `merged=false`. PR не
  мержится по условию задачи.
- URL PR и финальный commit SHA добавлены отдельным коммитом после создания PR.

## 10. История изменений

1. Собрано доказательство SDK по `Altium.SDK.Interfaces.dll`.
2. Добавлены reader, handler и регистрация команды; исправлены fail-closed условия для body ID и
   `footprintSourceUniqueId`.
3. Build/tests/checksums выполнены; CAD runtime заблокирован отсутствием загруженной команды.
