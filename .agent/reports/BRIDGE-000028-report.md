---
id: BRIDGE-000028
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T21:51:23+03:00
updated: 2026-11-26T21:59:02+03:00
base: source
branch: task/BRIDGE-000028-library-identity-modelorigin-semantics
commit: b967f7142aa1fba8ec490d1b08f6ebf742038521
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/24
related: [BRIDGE-000023, BRIDGE-000024, BRIDGE-000025, BRIDGE-000027, BRIDGE-000028]
---

# BRIDGE-000028 — library identity и семантика `IPCB_Model.GetOrigin()`

## 1. Итог

Исследование выполнено на зафиксированной базе `source`
`bdff118b288454be68c7c590d875142620a139e2`, в ветке
`task/BRIDGE-000028-library-identity-modelorigin-semantics`. Изменён только этот отчёт:
production placement engine, каталог, Exchange Package, SDK ledger, ADR, `.PcbDoc` и CAD-артефакты
не трогались.

**Однозначный вывод по D-024: продолжать контракт без изменений; отдельный ADR по результатам этой
задачи не требуется.** D-024 остаётся `Accepted (contract only; production activation deferred)`;
BRIDGE-000024 и BRIDGE-000025 остаются `blocked`. Отдельный ADR понадобится только если Оператор
решит *изменить* определение ключа калибровки или definition of `ModelOrigin` — например, принять
bbox-corner как origin или заменить library ID на Vault GUID. Данное исследование такого решения не
принимает и production Bottom не активирует.

Подтверждено:

1. **Цепочка идентичности D-024 не собрана.** У всех 15 placed body в реальном `diagnostics.json`
   `bodyUniqueId=null`, `footprintSourceUniqueId=null`, `bodyUniqueIdStatus=unavailable`,
   `footprintSourceUniqueIdStatus=unavailable`. Все три `.PCBLIB` вернули
   `LIBRARY_FOOTPRINT_UNIQUE_ID_UNAVAILABLE`, `sourceUniqueId=null`, `bodyUniqueIds=[]`. Placed
   component ID и placed `SourceUniqueId` присутствуют, но это instance/source-level значения
   (C-36, C-112, V-07) и подменять ими library-level ID запрещено.
2. **Поддерживаемый путь `placed component → library footprint ID` не доказан.**
   `IPCB_LibComponent` наследует `IPCB_Primitive.GetState_UniqueId()`; в BRIDGE-000027 этот вызов на
   library footprint вернул пустоту. `IPCB_LibComponent.GetState_ItemGUID()` /
   `GetState_ItemRevisionGUID()`, `IPCB_Component.GetState_ItemGUID()` и
   `IPCB_Model.GetItemGUID()/GetVaultGUID()/GetChecksum()` существуют в SDK 4.0.1, но в
   BRIDGE-000027 не читались; официальная документация не приравнивает их к
   `footprintSourceUniqueId` / `bodyUniqueId` из D-024. `EDP.ILibraryComponentIdentifier.GetFootprintGUID()`
   доступен только через `IComponentSearchResult.GetLibraryComponent()`, а путь от placed
   `IPCB_Component` к `IComponentSearchResult` в публичных контрактах SDK не найден:
   `IComponentSearch.TrySelectComponent` помечен `EditorBrowsable(Never)`, а helper принимает
   `IManagedComponentIdentifier`, не PCB component. Использовать его как programmatic lookup было бы
   обходом, а не подтверждённым API.
3. **`IPCB_Model.GetOrigin()` возвращает X/Y в IU.** Публичный helper
   `IPCB_ModelHelper.GetOrigin(this IPCB_Model)` возвращает `PCB.CoordPoint`; `ICoordPoint` имеет
   только `GetX()`/`GetY()`, Z в контракте отсутствует. Единицы — IU
   (`millimetersPerIu=2.54e-06` в реальном ответе). В SDK ledger C-98 и BRIDGE-000016 зафиксировано,
   что X/Y — абсолютные координаты платы (board frame), а не model-local origin, не STEP origin и не
   bbox corner. Метка `modelOriginCoordinateSystem = "IPCB_Model.GetOrigin() model-local X/Y"` в
   формате BRIDGE-000024/27 неверна и в будущих диагностиках должна быть заменена на
   «absolute board coordinates; Z unavailable».
4. **Расхождение `e≈0` и bbox-proxy объясняется подстановкой другой величины.** По фактическим
   `GetOrigin()` для 9 пар `e=(-1,0) IU` у C37/C25 и `(0,0) IU` у остальных 8 пар, то есть
   `|e|≤0.00000254 mm`. bbox-proxy из BRIDGE-000023 дал `+0.524002 mm` (CAP), `+0.499999 mm`
   (RES) и `+1.25000004 mm` (SOD323). Это не «два результата для одного поля»: proxy — левый нижний
   угол bbox geometry, а не `ModelOrigin`. Смещение bbox-corner относительно reference point
   зависит от геометрии и при flip не сокращается, поэтому оно добавляет в `e` удвоенные полуразмеры
   тела (≈0.262/0.250/0.625 mm). `e≈0` из `GetOrigin()` показывает только mirror-симметрию reference
   point, а не совпадение геометрии. Независимый landmark residual D-024 §2/§5 с валидным ключом и
   валидным `ModelOrigin` по-прежнему не измерен; `e≈0` не опровергает и не подтверждает правило
   смены знака Y.

## 2. Изменённые файлы

- `.agent/reports/BRIDGE-000028-report.md` — настоящий отчёт.

Не добавлены/не изменены: DLL, STEP, PCB, library-файлы, каталог, Exchange Package, SDK ledger,
ADR, production-код, секреты и данные заказчика.

## 3. Принятые решения и обоснование

### 3.1. Источники доказательств

Использованы только публичные контракты и официальные материалы:

- SDK 4.0.1 (локально, версия зафиксирована):
  `C:\ProgramData\Altium\Altium Designer {B4282C90-18BF-448F-BF1A-9E36AE887190}\Extensions\Altium Developer\SDK4.0\CSharp\Source Code\EDP.PCB.Interfaces.cs`
  и `EDP.PCB.RecordWrappers.cs`, `EDP.ComponentLib.Interfaces.cs`.
- Декомпилированные metadata установленной `Altium.SDK.Interfaces.dll` из BRIDGE-000024
  (`%LOCALAPPDATA%\Temp\opencode\bridge-000024\sdk\`) — только чтобы зафиксировать наличие
  `EDP.ILibraryComponentIdentifier` и `IComponentSearch`; не как основание для вызова
  undocumented/internal DispId.
- Официальные страницы Altium Documentation (прочитаны 2026-11-26):
  - <https://www.altium.com/documentation/altium-dxp-developer/pcb-api-system-interfaces-reference>
  - <https://www.altium.com/documentation/altium-dxp-developer/pcb-api-design-objects-interfaces-reference>
  - <https://www.altium.com/documentation/altium-designer/scripting/examples-reference>
  - <https://www.altium.com/documentation/altium-designer/pcb/3d-bodies>
  - <https://www.altium.com/documentation/altium-designer/pcb/3d-bodies/additional-tools>
- Реальный response BRIDGE-000027:
  `%LOCALAPPDATA%\Temp\EcadMcadBridge\BRIDGE-000025\diagnostics.json`,
  SHA-256 `246AE33E49E4181AFB5D1C6AFEE3ECBE69154F04783F7849ECFCF6E429819173`, `status=ok`,
  `package.verified=true`, `24/24`, `missingRequestedDesignators=[]`.
- BRIDGE-000016 report (board frame для `GetOrigin`), BRIDGE-000023 report и research evidence
  (bbox-proxy, STEP assets), SDK ledger C-36/C-98/C-101/C-110/C-112/C-116/C-118 и V-07.

DelphiScript, Legacy API, reflection, `dynamic`, прямое обращение к DispId и ручной парсинг
`.PcbDoc` не использовались. Ни один CAD-документ не сохранялся.

### 3.2. Подтверждённые API-связи

| Связь | Подтверждённые члены SDK | Что это даёт | Ограничение |
|---|---|---|---|
| Placed component → footprint hint | `IPCB_Component.GetState_Pattern()` L4157, `GetState_SourceUniqueId()` L4187, `GetState_SourceFootprintLibrary()` L4193 | Имя footprint и source-подсказки; совпало с request/`footprintPattern` у всех 15 | Официальная документация: UID «used to match each schematic component to the corresponding PCB component», system-generated; `SourceFootprintLibrary` — «descriptive account of the footprint». Это не library-level ID из D-024; стабильность V-07 не подтверждена |
| Placed component → instance/Vault ID | `IPCB_Primitive.GetState_UniqueId()` L468, `GetState_Identifier()` L327; `IPCB_Component.GetState_VaultGUID()` L4379, `GetState_ItemGUID()` L4382, `GetState_ItemRevisionGUID()` L4385 | Placed ID (`JTETQPJD` и т.п.), human-readable identifier, Vault IDs | Placed ID instance-specific; docs 3D Bodies называют Identifier «human-readable name used to identify the 3D Body object». Равенство Vault ID и D-024 `footprintSourceUniqueId` недоказано и не измерено |
| Placed body → component / board | `IPCB_ComponentBody.GetState_Component()`, `GetState_InComponent()`, `GetState_Board()` (helper, ledger C-110) | Однозначный владелец body; подтверждено на U101–U104 в BRIDGE-000016/C-020 | Не даёт library body ID |
| Placed body → 3D model / projection | `IPCB_ComponentBody.GetModel()` через `IPCB_ComponentBodyHelper`; `GetBodyProjection()`, `GetStandoffHeight()`, `GetOverallHeight()` | `IPCB_Model`, Top/Bottom projection и высоты; ledger C-110 | `GetModel()` — публичный helper над internal member SDK; без reflection/dynamic |
| Model geometry state | `IPCB_Model.GetState(out rotX, rotY, rotZ, out dz)` L6185, `GetRotation()` L6221, `GetFileName()`/`GetEmbed()`/`GetState_ModelType()` (ledger C-101), `ExportToStep(string)` L6230 | Model rotation state, embedded/linked source, экспорт STEP в читаемый актив | `GetDataHash("SHA256")` в ledger C-118/V-26 отвергнут как неподтверждённый; `GetChecksum()` L6263 — `uint` без доказанного соответствия SHA-256 |
| Model origin | `IPCB_ModelHelper.GetOrigin(this IPCB_Model)` L15839 → `PCB.CoordPoint`; `ICoordPoint.GetX()/GetY()` L106/L109 (`EDP.PCB.RecordWrappers.cs`) | Два int в IU; Z отсутствует по контракту | Official PCB API page не описывает `IPCB_Model`, но C-98 и BRIDGE-000016 подтвердили абсолютные board-координаты; это не model-local, не STEP origin и не bbox corner |
| Library footprint enumeration | `IPCB_ServerInterface.GetPCBLibraryByPath()` helper L16561, `LoadPCBLibraryByPath()` L16655, `DestroyPCBLibrary()` L16661; `IPCB_Library.ComponentCount()` L6042, `GetComponentByName()` L15726, `GetComponent(int)`, `LibraryIterator_Create/Destroy()`; `IPCB_LibComponent.GetState_Pattern()` L5941, унаследованный `GetState_UniqueId()` L468, `GetState_ItemGUID()` L5950, `GetState_ItemRevisionGUID()` L5953 | Поддерживаемый путь открыть `.PCBLIB`, перебрать footprint и прочитать его идентификаторы | В реальном прогоне `GetState_UniqueId()` вернул пустоту для всех трёх библиотек; ItemGUID-поля не читались, equivalence не доказана |
| Library body enumeration | `IPCB_Group.GroupIterator_Create()` с фильтром `TObjectSet{eComponentBodyObject}` (ledger C-111), `IPCB_ComponentBody.GetState_UniqueId()` | Теоретический путь к `bodyUniqueId` | `bodyUniqueIds=[]` во всех трёх библиотеках; placed body ID также пуст |
| `EDP` library identifier | `EDP.ILibraryComponentIdentifier.GetFootprintGUID()` (DispId 532993); `IComponentSearchResult.GetLibraryComponent()`; `IComponentSearch.TrySelectComponent(object, bool, out …)` | В установленной сборке интерфейс существует | Публичный SDK 4.0.1 не даёт пути от placed `IPCB_Component` к `IComponentSearchResult`: `TrySelectComponent` — `EditorBrowsable(Never)`, helper ограничен `IManagedComponentIdentifier`. Применение не подтверждено и не используется |
| STEP asset | `IPCB_Model.ExportToStep(string)`, package manifest + SHA-256 (BRIDGE-000023, 24/24) | `stepAssetSha256` доступен и проверен | Asset SHA идентифицирует байты экспорта, а не library footprint/body; повторный экспорт меняет ключ D-024 |

Официальные формулировки, важные для вывода:

- PCB API System Interfaces: “A library is composed of footprints as pages and each footprint is
  represented by the `IPCB_LibComponent` interface”; `GetPCBLibraryByPath` returns `IPCB_Library`
  “only if the path … represents this document”.
- PCB API Design Objects Interfaces, `IPCB_LibComponent Interface Overview`: footprint — «page»
  библиотеки; child-объекты (включая 3D body) читаются через `IPCB_GroupIterator`.
- Там же, `GetState_SourceUniqueId`: “Unique IDs (UIDs) are used to match each schematic component to
  the corresponding PCB component… The Unique Identifier (UID) is a system generated value that
  uniquely identifies the source component”; `GetState_SourceFootprintLibrary`: “descriptive account
  of the footprint”.
- Там же, `IPCB_Component Interface Overview`: “The origin in the library editor defines the
  reference point of a footprint”; “The reference point of a component is set by the X,Y fields
  inherited from `IPCB_Group`”.
- Там же, `IPCB_Primitive Interface Overview`: “A primitive has a bounding rectangle which
  encapsulates the region of the primitive” — bbox описывает геометрию, а не reference point.
- Working with 3D Bodies: Location X/Y — “the current X/Y coordinate of the reference point of the
  3D Body, relative to the current design space origin”; Identifier — “human-readable name used to
  identify the 3D Body object”; Rotation X/Y/Z — углы в градусах; Standoff Height — расстояние от
  поверхности платы до нижней стороны модели; Generic body автоматически resized to enclose the
  chosen model.
- Scripting Examples Reference: `IntLibSearchDemo` демонстрирует поиск footprints and 3D models в
  IntLib и PCBLIB — подтверждает саму возможность библиотечной навигации, но не identity-контракт.

### 3.3. Почему identity недоступна

`diagnostics.json` был получен с уже собранной fail-closed диагностикой BRIDGE-000024/27: она читает
только `GetState_UniqueId()` у library footprint/body и placed body ID. Поэтому «unavailable» — это
результат именно этого набора вызовов на данных файловых `.PCBLIB`, а не доказательство, что в SDK
нет *никаких* других identifiers. Разделение важно:

| Кандидат | Где есть в SDK | Статус для D-024 |
|---|---|---|
| `IPCB_LibComponent.GetState_UniqueId()` | Унаследован от `IPCB_Primitive` L468; вызван в BRIDGE-000027 | Пусто во всех 3 библиотеках → unavailable |
| `IPCB_ComponentBody.GetState_UniqueId()` / placed body ID | `IPCB_Primitive` L468, вызван | Пусто во всех 15 body и в library iteration → unavailable |
| `IPCB_LibComponent.GetState_ItemGUID()` / `GetState_ItemRevisionGUID()` | L5950/L5953, **не вызван** | Поддерживаемый вызов, но нет runtime-значений и нет доказательства, что это D-024 footprint ID |
| `IPCB_Component.GetState_ItemGUID()` / `GetState_VaultGUID()` | L4382/L4379, **не вызван** | Vault-level component ID; не library footprint ID по умолчанию |
| `IPCB_Model.GetItemGUID()` / `GetVaultGUID()` / `GetItemRevisionGUID()` / `GetChecksum()` | L6251/L6245/L6257/L6263, **не вызван** | Кандидаты на identity модели/STEP источника; docs не связывают их с `bodyUniqueId` |
| `EDP.ILibraryComponentIdentifier.GetFootprintGUID()` | metadata `Altium.SDK.Interfaces.dll`, DispId 532993 | Нет поддерживаемого пути от placed PCB component; не применён |
| Placed `UniqueID`, `SourceUniqueId`, `Identifier`, `ObjectId` | вычитаны в BRIDGE-000023/27 | Instance/source/display значения; подмена запрещена D-024 §1 |

Итоговая цепочка D-024 §1:

```text
(footprintSourceUniqueId, bodyUniqueId, bodyOrdinal, stepAssetSha256)
  = (unavailable, unavailable, 0, <verified SHA-256>)   →  Unsupported
```

`bodyOrdinal=0` у всех 15 и `stepAssetSha256` проверен, но absence двух library ID блокирует запись
целиком. Никакие числа не совпадали «по ошибке» и не выдавались за library ID.

### 3.4. Координаты, rotation и STEP origin

Контракт SDK 4.0.1:

```csharp
// IPCB_Model, EDP.PCB.Interfaces.cs
void GetState(out double argRotX, out double argRotY, out double argRotZ, out int argDz); // L6185
double GetRotation();                      // L6221
PCB.ICoordPoint Internal_GetOrigin();      // L6215, EditorBrowsable(Never)
void SetOrigin(PCB.ICoordPoint argValue);  // L6218

// IPCB_ModelHelper, L15839 — публичный вызов, уже в SDK ledger C-98/C-116
public static CoordPoint GetOrigin(this IPCB_Model model) =>
    new CoordPoint(model.Internal_GetOrigin() as PCB.ICoordPoint);

// ICoordPoint, EDP.PCB.RecordWrappers.cs L103
int GetX(); // L106
int GetY(); // L109
```

Подтверждено:

- X/Y — целые IU; `1 IU = 0.00000254 mm`. Z недоступен **по контракту**, поэтому
  `modelOrigin.zIu/zMm=null` — fail-closed, а не ноль.
- X/Y находятся в абсолютной system coordinates платы (ledger C-98; BRIDGE-000016 проверил это
  через полный board STEP и controlled rotations, альтернатива с локальным смещением давала ошибку
  9.497 mm). Это **не** «model-local X/Y», не origin STEP-файла и не угол bbox.
- Component rotation читается отдельно через `IPCB_Component.GetState_Rotation()`. Model rotation
  доступен через `IPCB_Model.GetState(out rotX, rotY, rotZ, out dz)` и отдельно через
  `GetRotation()`; официальная-doc по 3D Bodies описывает Rotation X/Y/Z в градусах. Ledger-уточнение
  C-110/C-115: у *размещённого* body `rotZ` уже включает поворот компонента, поэтому складывать
  `Rz(component.RotationDeg)` и `rotZ` повторно нельзя. `GetRotation()` не подменяет `rotZ`.
- STEP origin / `AssetOriginLocal` — asset-local величина, используемая в формуле D-024 как
  `T(−AssetOriginLocal)`; она не объявляется равной `GetOrigin()` или bbox corner. Экспортированная
  geometry компонента уже содержит placement transform; сырой STEP origin не revived как board pivot.

Фактические значения BRIDGE-000027 согласуются с board frame:

- R25: origin `(15511780,17283445)` IU, component `(15511812,17185039)` IU → `(-32,+98406)` IU;
- C25: origin `(24566896,23090520)` IU, component `(24566929,23090551)` IU → `(-33,-31)` IU;
- D8 и D9: origin == component position → `(0,0)` IU.

### 3.5. Пересчёт `e` по реальным `GetOrigin()` и объяснение расхождения

Формулы D-024 §2 применены без изменений:

```text
d_top           = Rz(−θ_top) · (ModelOrigin_top.XY − ComponentPosition_top.XY)
d_bottom_local  = Rz(−θ_bottom) · (ModelOrigin_bottom.XY − ComponentPosition_bottom.XY)
d_bottom_board  = (d_bottom_local.X, −d_bottom_local.Y)
e               = d_bottom_board − d_top
1 IU = 0.00000254 mm
```

| Bottom | Top | θ bottom | `d_top` IU | `d_bottom_local` IU | `d_bottom_board` IU | `e` IU | `e` mm |
|---|---|---:|---|---|---|---|---|
| C37 | C25 | 180 | `(33,31)` | `(32,−31)` | `(32,31)` | `(-1,0)` | `(-0.00000254,0)` |
| C34 | C25 | 180 | `(33,31)` | `(33,−31)` | `(33,31)` | `(0,0)` | `(0,0)` |
| C36 | C29 | 270 | `(33,31)` | `(33,−31)` | `(33,31)` | `(0,0)` | `(0,0)` |
| C35 | C29 | 270 | `(33,31)` | `(33,−31)` | `(33,31)` | `(0,0)` | `(0,0)` |
| R50 | R25 | 0 | `(-32,98406)` | `(-32,−98406)` | `(-32,98406)` | `(0,0)` | `(0,0)` |
| R43 | R25 | 0 | `(-32,98406)` | `(-32,−98406)` | `(-32,98406)` | `(0,0)` | `(0,0)` |
| R44 | R36 | 90 | `(-32,98406)` | `(-32,−98406)` | `(-32,98406)` | `(0,0)` | `(0,0)` |
| R53 | R26 | 180 | `(-32,98406)` | `(-32,−98406)` | `(-32,98406)` | `(0,0)` | `(0,0)` |
| D9 | D8 | 180 | `(0,0)` | `(0,0)` | `(0,0)` | `(0,0)` | `(0,0)` |

На этих данных `e` из `ModelOrigin` близок к нулю: максимальная компонента `2.54e-06 mm`. Это
вычисленное значение, **не валидная калибровка**: D-024 требует полный ключ identity и независимый
landmark residual `≤0.001 mm`; оба условия не выполнены. Допуск `1e-9 mm` в D-024 §1 относится к
совпадению кэша `e` с пересчитанным `e`, а не к «почти нулевому» значению; кэш в этой задаче не
создавался.

Сравнение с BRIDGE-000023 (bbox corner proxy):

| Footprint | `e` mm по bbox-proxy | `e` mm по `GetOrigin()` | Источник расхождения |
|---|---:|---:|---|
| CAP 0402_1005 | `(0,+0.524002)` | `(-0.00000254…0,0)` | Смещение bbox corner от reference point `(-0.4999,-0.2619) mm`; после flip Y вклад удваивается ≈`0.524 mm` |
| RES 0402_1005 | `(0,+0.499999)` | `(0,0)` | Смещение `(-0.50008,-0.25005) mm`; после flip Y вклад ≈`0.5 mm` |
| SOD323 | `(-0.00000254,+1.25000004)` | `(0,0)` | Смещение `(-1.25,-0.625) mm`; после flip Y вклад ≈`1.25 mm`, `e.x` отличается на 1 IU округления |

Математически это одно и то же явление:

```text
bbox_corner = ModelOrigin + geometry_dependent_offset
e_proxy     = e_origin + (d_bottom_board_offset − d_top_offset)
```

`geometry_dependent_offset` не инвариантен к `Rz(−θ)` и flip, поэтому он не сокращается, а даёт
`2 × half_extent` в соответствующих footprint. BRIDGE-000023 справедливо не назвал это проверкой
D-024: подставленное поле не соответствует определению `ModelOrigin`. По этой же причине bbox-proxy
нельзя использовать ни для «опровержения sign flip», ни для calibration record. Открытый риск:
реальная STEP-геометрия может требовать коррекций ≈0.5–1.25 mm, и `e≈0` из reference point не
доказывает геометрическую корректность Bottom placement. Это требует отдельного landmark-замера, а не
переопределения `e` в текущем ADR.

## 4. Неподтверждённые вопросы и риски

1. **Identity-кандидаты не измеряны.** `GetState_ItemGUID()`, `GetItemGUID()`, `GetVaultGUID()`,
   `GetChecksum()`, `GetState_Identifier()` и pairing `IComponentSearch` не читались в
   BRIDGE-000027. Нельзя ни принять их как замену library ID, ни окончательно объявить
   платформенную невозможность: зафиксирована недоступность *через проверенные вызовы на этих
   файловых PCBLIB*.
2. **Стабильность V-07 не подтверждена.** Placed `SourceUniqueId` различается между экземплярами;
   поведение library-level ID после `LoadFromLibrary`, save/reopen и замены `.PCBLIB` не измерено.
3. **Z недоступен по контракту SDK**, скрытый Z-offset в model origin остаётся непроверенным
   (D-024 §2).
4. **Координатная система `GetOrigin()` документирована в ledger/CAD-экспериментах, но не в official
   PCB API reference.** Метка «model-local» в предыдущем diagnostics-формате неверна; в этой задаче
   код диагностики не менялся.
5. **`e≈0` не равно valid calibration.** Нет ни полного ключа D-024, ни независимого landmark
   residual; bbox residual >0.001 mm показывает лишь неэквивалентность подстановки.
6. **`GetDataHash("SHA256")` неподтверждён** (ledger C-118/V-26): нельзя заменить STEP SHA на
   model data hash без отдельной проверки.
7. **Официальная PCB API documentation частично датирована 2018 годом и не описывает
   `IPCB_Model`**; signature-доказательство взято из version-pinned SDK 4.0.1. Любое расширение
   контракта требует нового доказательства, а не предположения «нет docs — значит нет поведения».

### Минимальный архитектурный вариант, если identity не удаётся получить

Не реализован здесь; предлагается как отдельная read-only задача, без изменения D-024 и production:

1. Расширить `ReadBodyIdentityDiagnostics` (вне production engine) полями-кандидатами:
   `libraryFootprintItemGuid`, `libraryFootprintItemRevisionGuid`, `placedComponentItemGuid`,
   `placedComponentVaultGuid`, `modelItemGuid`, `modelVaultGuid`, `modelChecksum`,
   `bodyIdentifier`, `bodyXLocation/YLocation`. Каждое поле — с explicit `status` и diagnostics;
   пустые значения не подставляются.
2. Повторить замер на 15 body после save/reopen и на повторном placement: проверить стабильность и
   однозначность, сравнить с library enumeration по `Pattern`.
3. Только после подтверждения стабильности внести отдельный ADR о ключе D-024 (или о
   scope-ограничении на Workspace-библиотеки). Production catalog/Exchange Package меняются
   отдельным PR с CAD-доказательствами. Если стабильный ID не найден — D-024 остаётся
   `Unsupported`, а не «первая подходящая запись».

## 5. Проверки

- Build: `dotnet build EcadMcadBridge.sln -c Release --nologo` — успешно, `warnings=0 errors=0`.
- Tests: `dotnet test EcadMcadBridge.sln -c Release --nologo --no-build` — успешно: 195 Altium tests,
  94 SolidWorks tests, 229 Core tests, итого 518, пропусков/ошибок нет; production-код в этой задаче
  не менялся.
- CAD runtime smoke: **не выполнялся** (задача исследовательская; `.PcbDoc` не сохранялся).
  Использован уже существующий реальный response BRIDGE-000027, его SHA-256 совпадает с
  зафиксированным в BRIDGE-000027.
- Package: BRIDGE-000023 research package `24/24`, `verified=true`; STEP не экспортировались
  повторно, активная плата не модифицировалась.
- Среда: AD `26.12.2.9`, .NET 10, `net10.0-windows` (Altium), SDK 4.0.1;
  `millimetersPerIu=2.54e-06`.
- Дифф: только `.agent/reports/BRIDGE-000028-report.md`; SDK ledger, ADR, каталог, PCB, DLL, STEP
  и данные заказчика не добавлены. `git diff --check` — без ошибок.

## 6. Commit SHA и PR

- Commit, на котором открыт PR: `b967f7142aa1fba8ec490d1b08f6ebf742038521`.
- Финальный commit работы: `b967f7142aa1fba8ec490d1b08f6ebf742038521` (production-код не менялся).
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/24 — ветка
  `task/BRIDGE-000028-library-identity-modelorigin-semantics` → `source`; по состоянию на
  `2026-11-26T21:59:02+03:00` `state=open`, `merged=false`.
- PR не мержится по условию задачи; PR #23 не изменяется. URL PR и commit SHA добавлены отдельным
  коммитом после создания PR.

## 7. История изменений

1. Зафиксирован `source` `bdff118b288454be68c7c590d875142620a139e2`, создана ветка задачи.
2. Проверены SDK 4.0.1 и официальные страницы Altium Documentation; собрана таблица API и
   доказательство отсутствия поддерживаемого пути `placed component → library footprint GUID`.
3. Пересчитан `e` по 9 парам из реального `diagnostics.json`; объяснено расхождение с bbox-proxy
   BRIDGE-000023 через geometry-dependent offset.
4. Сформулирован вывод: D-024 без изменений, отдельный ADR не требуется; BRIDGE-000024/25 остаются
   `blocked`; минимальный архитектурный вариант зафиксирован, но не реализован.
5. Выполнены build/tests; создан этот отчёт и PR (ссылки и SHA — отдельным коммитом).
