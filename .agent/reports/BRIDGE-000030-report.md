---
id: BRIDGE-000030
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T22:36:49+03:00
updated: 2026-11-26T22:42:37+03:00
base: source
branch: task/BRIDGE-000030-native-library-identity
commit: 8673ee3c3a945790e48b0bb10cc78ba82b33c82a
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/26
related: [BRIDGE-000016, BRIDGE-000024, BRIDGE-000025, BRIDGE-000028, BRIDGE-000029, BRIDGE-000030]
---

# BRIDGE-000030 — нативный `IPCB_Component.LoadFromLibrary()` и library identity

## 1. Итог

Исследование выполнено после свежего `git fetch origin` на зафиксированной базе `source`
`ea817c9b1e52b6a86e5eb541cf814a578887a7d5` в ветке
`task/BRIDGE-000030-native-library-identity`. Файл задачи `.agent/tasks/BRIDGE-000030*.md`
в репозитории отсутствует; работа выполнялась по постановке Оператора. Изменён только этот отчёт:
production placement engine, Exchange Package, каталог, SDK ledger, ADR/public contract,
`.PcbDoc` и CAD-артефакты в Git не изменялись.

**Итог: вариант C — identity недоступна на текущих доказательствах (fail-closed).**
Это не утверждение платформенной невозможности, а вывод по доступному подтверждённому
контракту и доступным инструментам. Стабильный ключ
`placed component → library footprint → library 3D body` не подтверждён: `LoadFromLibrary()`
реально подтверждён как публичный SDK-вызов, но он загружает примитивы в компонент и не
возвращает / не публикует исходный `IPCB_LibComponent`; доступный EDA-мост не предоставляет
поддерживаемого способа вызвать произвольный `IPCB_Component.LoadFromLibrary()` на тестовом
компоненте, а `pcb_place_components` использует тот же вызов внутри и поэтому не является
независимым контролем.

Части B–D (runtime-сравнение нативного пути и control, save/reopen, delete/replace, две ревизии
библиотеки) **не выполнялись**: вместо симуляции нативного пути зафиксирована конкретная
ограниченность инструментария и работа остановлена на статическом анализе, как требовалось
постановкой. Варианты A и B не объявлены, потому что для них нет доказательств стабильности
library footprint ID и связи library body ID с placement. D-024 остаётся
`Accepted (contract only; production activation deferred)`; fail-closed правила, калибровки
Bottom, production Bottom и записи калибровки не тронуты. Рекомендации для отдельной
архитектурной задачи не подготовлены: они требовались только для вариантов A/B.

## 2. Изменённые файлы

- `.agent/reports/BRIDGE-000030-report.md` — новый отчёт.

Вне Git файлы не создавались и не изменялись. Пользовательская плата
`D:\Git\Projects\mdm15-1v05m_electric\Dev_board.PcbDoc` читалась только для контроля SHA-256 и
осталась неизменной: `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F`
до и после работ. Фикстурная копия BRIDGE-000029 не использовалась, новая копия не создавалась.

PR #25 (`task/BRIDGE-000029-identity-landmark-validation`,
<https://github.com/YOUASSBEE/EcadMcadBridge/pull/25>) остаётся открытой и независимой:
ветка создана от `origin/source`, коммиты PR #25 в эту ветку не мержились
(`git merge-base --is-ancestor` = 1). GitHub API на дату отчёта подтверждает
`state=open`, `merged=false`.

## 3. Принятые решения и обоснование

### 3.1. Часть A — подтверждённый статический контракт SDK

Источник: установленный version-pinned SDK 4.0.1,
`C:\ProgramData\Altium\Altium Designer {B4282C90-18BF-448F-BF1A-9E36AE887190}\Extensions\Altium Developer\SDK4.0\CSharp\Source Code\EDP.PCB.Interfaces.cs`,
SHA-256 `1DB0C6C0379CD85076E80C2D3B9207C1B4B4C6CC82EA1D525AF8961500CCB05F`.
Дополнительно использована публичная документация SDK:
`.../ad-sdk-docs/reference/pcb/IPCB_Component.md`,
`.../ad-sdk-docs/samples/ExportPcbLibComponentToStep.md`,
`.../ad-sdk-docs/samples/PlaceAndMovePcbComponent.md`.
Новые API в `SDK_API_LEDGER.md` не добавлялись; ниже цитируются только уже зафиксированные
C-107/C-108/C-112/C-116 и внешней документации.

| API | Сигнатура / строка | Контекст и side effects | Что даёт для identity |
|---|---|---|---|
| `IPCB_Component.LoadFromLibrary` | `bool LoadFromLibrary(string argParams)`, L4304; `DispId(PCBIPCBComponentSDKLoadFromLibraryDispId)` | Документация: `argParams` — пары `Name=Value`, разделённые `\|`, пример `FileName=<library path>\|Footprint=<footprint name>`; `true` при успехе. Загружает примитивы footprint в компонент. Примеры создают компонент через `PCBObjectFactory(eComponentObject, eNoDimension, eCreate_Default)`, назначают `Board`, вызывают метод, затем задают Layer/X/Y и добавляют через `Board.AddPCBObject`. Без `Board` пример фиксирует access violation в `ADVPCB.DLL`; `.PcbLib` открывать не обязателен, но файл должен быть сохранён; отсутствующий footprint даёт `false`. | Не возвращает `IPCB_LibComponent` и не публикует library ID. Подтверждает только загрузку геометрии/примитивов. |
| `IPCB_Component.LoadCompFromLibrary` | `bool LoadCompFromLibrary()`, L4301; `DispId(...LoadCompFromLibraryDispId)` | Документация: перезагружает примитивы из `Pattern`, читает `SourceFootprintLibrary`, при неудаче ищет open library docs; сохраняет location/layer/rotation; копирует из library footprint `SourceFootprintLibrary`, `FootprintDescription`, `DefaultPCB3DModel`, `VaultGUID`, `ItemGUID`, `ItemRevisionGUID`, `ComponentKind` и `Height` (если не 0). | Показывает, что ItemGUID **может** быть library-derived для этого другого вызова, но не доказывает то же для `LoadFromLibrary()` и не даёт связи с library body. |
| `IPCB_Component` source-поля | `GetState_Pattern()` L4157, `GetState_SourceUniqueId()` L4187, `GetState_SourceFootprintLibrary()` L4193, `GetState_SourceComponentLibrary()` L4196, `GetState_ItemGUID()` L4382, `GetState_ItemRevisionGUID()` L4385, `GetState_VaultGUID()` L4379 | Публичные get/set; документированы как имя footprint, schematic UID, пути библиотек и item/vault GUID размещённого объекта. | Разные владельцы/scope; Path и Pattern не стабильный ID; `SourceUniqueId` — schematic instance. ItemGUID на placed объекте не доказывает library footprint ID без provenance. |
| `IPCB_LibComponent` | `GetState_Pattern()` L5941, `GetState_ItemGUID()` L5950, `GetState_ItemRevisionGUID()` L5953, `TransferAllPrimitivesOntoBoard()` L5974, `SaveToFile(string)` L5977 | Library-side объект; Item/Revision GUID — identity library item, а не placed instance. | Кандидат library identity, но **нет документированного маршрута** `IPCB_Component → IPCB_LibComponent`. |
| Получение library-объекта | `IPCB_Library.GetState_CurrentComponent()` extension L15688 → `IPCB_LibComponent`; `IPCB_Library.GetComponentByName(string)` L15726; `IPCB_ServerInterface.LoadCompFromLibrary(string pattern, string libPath)` L16537 → `IPCB_LibComponent`; `GetPCBLibraryByPath` L16561 | Все — library-side точки входа; требуют имя/путь библиотеки и не принимают placed component. | Не замыкают цепочку placed → library. |
| `IPCB_ComponentBody.GetModel` | `static IPCB_Model GetModel(this IPCB_ComponentBody)` L17636; `SetModel` L17642; `ModelFactory_FromVault(title, vaultGuid, itemGuid, itemRevisionGuid)` L17719 | Body → model уже доступен; `FromVault` создаёт модель по GUID, но не доказывает, что placed body хранит library body GUID. | Link body → model есть, link body → **library body ID** нет. |
| `IPCB_Model` | `GetFileName()` L6182, `GetItemGUID()` L6251, `GetItemRevisionGUID()` L6257, `GetVaultGUID()` L6245, `GetChecksum()` L6263, `GetDataHash(string)` L6236 | Model asset metadata; checksum — `uint`, тип не совпадает с STEP SHA-256; `GetDataHash("SHA256")` в ledger C-118 отвергнут как `Hash type "SHA256" not supported`. | Кандидаты ассета, не proof library footprint/body identity. |

Отдельно: документация описывает `IPCB_Component.ModelHash` как hash 3D-моделей body с
учётом origin/rotations/dz, но в исходниках SDK 4.0.1 `GetState_ModelHash` не найден, а ledger
C-116 фиксирует, что ModelHash меняется после записи/повторного открытия. Поэтому он не
используется как стабильный identity key; замена identity геометрическим хэшем без отдельного
решения запрещена.

**Вывод Части A.** `LoadFromLibrary()` — подтверждённый публичный вызов с известным контекстом
(`Board` + `FileName`/`Footprint`) и известными side effects (загрузка примитивов). Однако его
документированный контракт не содержит возврата library-объекта, library ID или гарантии
копирования ItemGUID в placed компонент. Маршрута `IPCB_Component → IPCB_LibComponent` в
публичном интерфейсе нет.

### 3.2. Часть B — почему runtime-сравнение не выполнялось

- `tool_catalog` / `tool_guide` (backend `altium`, document kind `board`) не дают прямого
  инструмента для произвольного вызова `IPCB_Component.LoadFromLibrary()`; ближайшие
  производные инструменты — `pcb_place_components` и
  `pcb_update_footprints_from_library`.
- Реализация `pcb_place_components` (`...\eda_agent\scripts\PCB.pas`, комментарий L16909–16916)
  прямо называет `PCBObjectFactory(eComponentObject) + IPCB_Component.LoadFromLibrary` и
  сообщает, что размещается только геометрия без ECO/schematic linkage. Вызов на L16997
  использует строку `SourceLibReference=...|FootPrint=...|SourceComponentLibrary=<PcbLib>`,
  а не документированную пару `FileName|Footprint`; `SourceUniqueId` при необходимости
  проставляется вручную на L17011–17015. То есть это **тот же нативный вызов с
  недокументированными ключами**, а не независимый control.
- `pcb_update_footprints_from_library` (L17652–17685) перебирает три варианта загрузки:
  документированную пару `FootPrint|FileName` (L17673), `LoadCompFromLibrary` (L17679) и
  тот же `SourceComponentLibrary`-вариант (L17685). При этом комментарий L17659–17662 явно
  отклоняет результат `PCBServer.LoadCompFromLibrary` как library-собственный footprint,
  который board не должен принимать. Это дополнительное подтверждение отсутствия надёжного
  placed → library-object маршрута.
- EDA-мост не предоставляет вызова произвольного SDK-метода; reflection, `dynamic`,
  DelphiScript, Legacy API и внутренние COM-интерфейсы запрещены постановкой. Временный
  диагностический handler в Add-in не собирался и не устанавливался.

Поэтому нативный путь не имитировался. Зафиксирована конкретная инструментальная ограниченность:
в доступной поддерживаемой инструментальной поверхности нет способа вызвать `LoadFromLibrary()`
на отдельном тестовом компоненте и прочитать его library-side identity. Это **не** доказывает,
что в Altium SDK/платформе такой путь принципиально невозможен; это означает, что в рамках текущего
инструментария и без архитектурного решения доказательство недоступно.

### 3.3. Части C/D — статус проверок provenance/equality

В этой задаче не выполнялись: чтение начального состояния фикстуры, сохранение, close/reopen,
удаление тестового placement, замена из той же библиотеки, повторное размещение и сравнение
двух ревизий. Причины — §3.2. Компоненты CAP 0402, RES 0402, SOD323, Generic 3D body и
multi-body не размещались.

Для контекста можно использовать только результаты предыдущей задачи BRIDGE-000029, явно как
**внешние по этой задаче** наблюдаемые данные: `Pattern` и `PackageSpecificHash` стабильны
для типа, но не имеют доказанного provenance; `UniqueID` — per-instance; `SourceUniqueId` —
schematic instance и пуст у новых placement; `body UniqueID` пуст; ItemGUID/VaultGUID/checksum
через `obj_query` дают `Not a PCB property`. Эти данные не превращают перечисленные поля в
library identity и не закрывают требование Части C.

### 3.4. Формулировка варианта C

- A не выбран: нет доказательств, что library footprint ID и library body ID полностью подтверждены.
- B не выбран: нет доказательств, что library footprint ID стабилен для нативного `LoadFromLibrary()`
  пути; стабильность `Pattern`/`PackageSpecificHash` не является library identity, а library
  body linkage не подтверждена даже на статическом контракте.
- C выбран как fail-closed: identity unavailable на доступных доказательствах. D-024,
  fail-closed правила и калибровки не меняются; отдельный ADR не требуется.

## 4. Неподтверждённые вопросы и риски

- Не измерены runtime-значения `IPCB_LibComponent.GetState_ItemGUID()`,
  `GetState_ItemRevisionGUID()`, `IPCB_Model.GetItemGUID()/GetItemRevisionGUID()/GetChecksum()`
  и `IPCB_Component.GetState_ItemGUID()` в этой среде; предыдущая попытка `obj_query` вернула
  `Not a PCB property`.
- Не проверена стабильность library identity при save/reopen, повторном placement, мутации
  instance, разных библиотеках и ревизиях; не проверен multi-body случай.
- Не проверено, копирует ли именно `LoadFromLibrary()` ItemGUID/ItemRevisionGUID из library
  footprint: документация это утверждает только для `LoadCompFromLibrary()`.
- `GetDataHash("SHA256")` не подтверждён (ledger C-118); `GetChecksum()` имеет тип `uint` и
  не совпадает с STEP SHA-256 по типу и семантике.
- Если Оператор захочет перейти к A/B, требуется отдельная задача/решение с поддерживаемым
  диагностическим маршрутом и CAD-подтверждением; эта задача такой маршрут не создаёт.

## 5. Проверки

| Проверка | Результат |
|---|---|
| `git fetch origin --prune` | выполнено; `origin/source` = `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`, неизменна |
| Ветка | `task/BRIDGE-000030-native-library-identity`, создана от `origin/source`, upstream `origin/source` |
| `dotnet build -c Release` | успешно, 0 предупреждений, 0 ошибок |
| `dotnet test -c Release --no-build` | успешно: Core 229, Altium 195, SolidWorks 94 — всего 518 passed, 0 failed |
| `git diff --check` | чистый (после финальной правки отчёта) |
| Smoke в CAD | не выполнялся по причине §3.2: static-only, production-код и документы не менялись |
| Altium | `26.12.2.9`; bridge script `2026.11.25.1`; MCP `0.8.2`; `app_ping`.success=true |
| SDK | Altium SDK 4.0.1, source SHA-256 `1DB0C6C0379CD85076E80C2D3B9207C1B4B4C6CC82EA1D525AF8961500CCB05F` |
| .NET | `dotnet --version` = `10.0.404`; `global.json` = `10.0.100` + `rollForward: latestFeature` |
| Пользовательский PCB | SHA-256 до/после `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F`; не сохранялся |

## 6. Commit SHA и ссылка на PR

- Коммит с содержимым отчёта, на котором создан PR: `8673ee3c3a945790e48b0bb10cc78ba82b33c82a`.
- PR: <https://github.com/YOUASSBEE/EcadMcadBridge/pull/26> — открыт, `merged=false`, base
  `source` @ `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`, head
  `task/BRIDGE-000030-native-library-identity`; GitHub API подтверждает `state=open`,
  `mergeable=true`, `commits=1`.
- PR #25 остаётся открытым и не смерженным; в эту ветку он не влит.

## 7. История изменений

1. Первая версия отчёта: статический контракт SDK, ограниченность EDA-маршрута, вариант C,
   проверки build/test.
2. Отдельным коммитом после создания PR добавлены URL PR #26 и SHA коммита,
   отправленного на ревью.
