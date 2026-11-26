---
id: BRIDGE-000029
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T22:40:00+03:00
updated: 2026-11-26T22:40:00+03:00
base: source
branch: task/BRIDGE-000029-identity-landmark-validation
commit: PENDING
pr: PENDING
related: [BRIDGE-000016, BRIDGE-000017, BRIDGE-000018, BRIDGE-000022, BRIDGE-000023, BRIDGE-000024, BRIDGE-000025, BRIDGE-000027, BRIDGE-000028, BRIDGE-000029]
---

# BRIDGE-000029 — проверка identity-кандидатов и независимая геометрическая проверка Top/Bottom

## 1. Итог

Исследование выполнено на зафиксированной базе `source`
`ea817c9b1e52b6a86e5eb541cf814a578887a7d5` в ветке
`task/BRIDGE-000029-identity-landmark-validation`. Изменён только этот отчёт:
production placement engine, каталог, Exchange Package, SDK ledger, ADR, `.PcbDoc`
и CAD-артефакты в Git не изменялись.

**Итог: вариант B.** Стабильный library footprint ID и стабильный library body ID
по-прежнему недоступны, поэтому D-024 остаётся
`Accepted (contract only; production activation deferred)`, а калибровки для
Bottom остаются `Unsupported`. Никакие fail-closed правила D-024 не ослаблены,
production Bottom не активируется, отдельный ADR не требуется.

Геометрическая часть проверки при этом прошла: на 15 реальных телах
`Dev_board.PcbDoc` placement origin из полного STEP совпал с
`IPCB_Model.GetOrigin()` с максимальным residual `0.000004577 mm`; на шести
Top/Bottom наблюдениях с тремя независимыми landmark-точками на модель
максимальный residual составил `0.000004610 mm`. Оба значения меньше порога
`≤ 0.001 mm` из D-024 §2. Это подтверждает математику transform для проверенного
класса, но **не заменяет обязательный identity key** D-024 §1.

## 2. Изменённые файлы

- `.agent/reports/BRIDGE-000029-report.md` — новый отчёт.

Вне Git, для воспроизводимости измерений:

- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000029\fixture\Dev_board_copy.PcbDoc` —
  копия пользовательской платы, созданная `pcb_save_copy_as`; SHA-256
  `04E1AC387D6DDFD68AA04AF3E592D12265C85C0AC94EA66F0735B40064772197`.
- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000029\*.py` — временные
  read-only STEP-парсеры для landmark-проверки.

Пользовательский `D:\Git\Projects\mdm15-1v05m_electric\Dev_board.PcbDoc`
не сохранялся и не изменялся: SHA-256 до и после работ
`0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F`.

## 3. Принятые решения и обоснование

### 3.1. Статическая проверка SDK 4.0.1 (Part A)

Источник: установленный version-pinned SDK 4.0.1,
`...\SDK4.0\CSharp\Source Code\EDP.PCB.Interfaces.cs`, SHA-256
`1DB0C6C0379CD85076E80C2D3B9207C1B4B4C6CC82EA1D525AF8961500CCB05F`;
тип `T3DModelType` — `EDP.PCB.Types.cs` L1047–L1053. Ни один новый API в
`SDK_API_LEDGER.md` не добавлялся.

| Кандидат | Сигнатура / доказательство | Владелец и identity scope | Доступность, фактический результат, null/empty/error | Повторяемость | Связь placed → footprint → body |
|---|---|---|---|---|---|
| `IPCB_LibComponent.GetState_ItemGUID()` | `string`, L5950 | Library item в Workspace/vault; не designator и не instance | Статически подтверждён; через EDA-мост runtime-значение недоступно (`Not a PCB property`) | Не измерена | Теоретически library-side; runtime-доказательства нет |
| `IPCB_LibComponent.GetState_ItemRevisionGUID()` | `string`, L5953 | Revision library item | Статически подтверждён; runtime недоступен | Не измерена | Теоретически library-side; runtime-доказательства нет |
| `IPCB_LibComponent.GetState_UniqueId()` | `string`, унаследовано от `IPCB_Primitive`, L468 | UniqueId примитива внутри библиотечного документа | Через `lib_probe_footprint()` не возвращается; diagnostics: `footprintSourceUniqueId=null`, `LIBRARY_FOOTPRINT_UNIQUE_ID_UNAVAILABLE` | Неприменимо: значение недоступно | Не доказывает стабильный ID между placed и library |
| `IPCB_Component.GetState_ItemGUID()` | `string`, L4382 | Item ID размещённого managed component | Статически подтверждён; runtime недоступен | Не измерена | Не является library footprint ID |
| `IPCB_Component.GetState_ItemRevisionGUID()` | `string`, L4385 | Revision размещённого managed component | Статически подтверждён; runtime недоступен | Не измерена | Не является library footprint ID |
| `IPCB_Component.GetState_VaultGUID()` | `string`, L4379 | Vault managed item | Статически подтверждён; runtime недоступен | Не измерена | Идентифицирует хранилище, а не footprint/body |
| `IPCB_Component.GetState_SourceUniqueId()` | `string`, L4187 | Schematic source symbol instance (`\ROOT\CHILD`) | Runtime: C37 `\CMIFTJDY\PTRNHLDH`, R50 `\CMIFTJDY\HQCSGDHA`, D8 `\WVHXMFKW\VXVFGORV`; новые CZZ1/CZZ2/RZZ1/DZZ1 — пусто | Стабилен при save/reopen для исходных экземпляров; не воспроизводится новым placement | Schematic instance, не library ID |
| `IPCB_Component.GetState_Pattern()` | `string`, L4157 | Имя footprint: `CAP 0402_1005`, `RES 0402_1005`, `SOD323` | Runtime: непустое имя для всех наблюдаемых объектов | Стабильно для типа и после save/reopen | Не уникально, не GUID; link к library footprint не доказан |
| `IPCB_Model.GetItemGUID()` | `string`, L6251 | Item ID модели/ассета | Статически подтверждён; runtime недоступен | Не измерена | Может идентифицировать model asset; не proof library footprint ID |
| `IPCB_Model.GetItemRevisionGUID()` | `string`, L6257 | Revision модели | Статически подтверждён; runtime недоступен | Не измерена | Не proof library footprint ID |
| `IPCB_Model.GetVaultGUID()` | `string`, L6245 | Vault модели | Статически подтверждён; runtime недоступен | Не измерена | Хранилище ассета, не footprint/body |
| `IPCB_Model.GetChecksum()` | `uint`, L6263 | Числовой checksum модели в Altium | Статически подтверждён; runtime недоступен | Не измерена | Тип не совпадает с STEP SHA-256; provenance не доказана |
| `IPCB_Model.GetFileName()` | `string`, L6182 | Путь/имя STEP-модели | Статически подтверждён; путь в diagnostics не является identity | Зависит от файловой системы | Путь является указателем, не стабильным ID |
| `IPCB_Model.GetEmbed()` | `bool`, L6191 | Признак embedded модели | Статически подтверждён; runtime-значение не используется как identity | Состояние документа | Не identity |
| `IPCB_Model.GetState_ModelType()` | `T3DModelType`, extension L15827; internal `int` L6203 | Классификация: `Extruded`, `Generic`, `Cylinder`, `Sphere` | Статически подтверждена; runtime `GetState_ModelType` через мост недоступен | Не измерена | Классификация, не identity |
| `IPCB_ComponentBody.GetState_UniqueId()` | `string`, унаследовано от `IPCB_Primitive`, L468 | UniqueId примитива body в PCB-документе | Runtime: пусто для C37/CZZ1/CZZ2/R50/RZZ1/D8/DZZ1 и после save/reopen | Пусто стабильно, но бесполезно как ключ | Не даёт library body ID |
| `IPCB_ComponentBody.GetState_Identifier()` | `string`, унаследовано от `IPCB_Primitive`, L327 | Имя/идентификатор геометрии body | Runtime: `0402 cap`, `0402 resistor`, пусто для SOD323 | Стабильно для типа | Не уникально, не library body ID |
| `IPCB_ComponentBody.GetState_Component()` | `IPCB_Component`, extension L11731; internal object L375 | Владеющий placed component | Runtime `Component`: `C37`, `R50`, `D8`, `CZZ1` и т.д. | Стабильно для placement | Даёт link body → placed component, но не library footprint |

Это статическое доказательство наличия вызова и доступных runtime-значений,
а не доказательство стабильности значения на конкретных библиотеках и платах.
**Не отождествляем** ItemGUID, VaultGUID, model GUID, checksum, filename,
Pattern, placed UniqueId и library UniqueId без отдельного provenance/contract
доказательства: у них разные владельцы и scope.

### 3.2. Runtime-значения и доступность (Part A/B)

Использовались только read-only/фикстурные операции EDA bridge. Прямой вызов
ItemGUID/ItemRevisionGUID/VaultGUID/checksum/model type из доступного
диагностического интерфейса отсутствует; попытка прочитать их через
`obj_query` дала явную ошибку `Not a PCB property` для `ItemGUID`,
`ItemRevisionGUID`, `VaultGUID`, `GetState_ItemGUID` и т.п. Для этих кандидатов
фиксируется «сигнатура подтверждена, runtime не measured», а не выдуманное
значение.

Фактические read-only значения:

| Объект / поле | C37 | R50 | D8 | Новые размещения | Статус / scope |
|---|---|---|---|---|---|
| `eComponentObject.Pattern` | `CAP 0402_1005` | `RES 0402_1005` | `SOD323` | те же значения | Стабильно для footprint type; не GUID |
| `eComponentObject.UniqueID` | `SQMHHCOO` | `LDESCJFJ` | `SOKNTAMC` | `GEWPUSUV`, `RMHWOOGH`, `RSXCEHPK`, `YKEFBUVS` | Per-placement instance ID |
| `eComponentObject.SourceUniqueId` | `\CMIFTJDY\PTRNHLDH` | `\CMIFTJDY\HQCSGDHA` | `\WVHXMFKW\VXVFGORV` | пусто | Schematic instance path; не library ID |
| `eComponentObject.SourceLibReference` | `CC0402JRX7R9BB104` | `FRC0402P000 TS` | `B5819WS` | имя footprint | Component/symbol reference; не footprint identity |
| `eComponentObject.SourceFootprintLibrary` | `.PCBLIB` путь | `.PCBLIB` путь | `.PCBLIB` путь | у RZZ1/DZZ1 — `gortpower_SA.DbLib` | Путь/источник, не стабильный ID |
| `eComponentObject.SourceComponentLibrary` | `gortpower_SA.DbLib` | `gortpower_SA.DbLib` | `gortpower_SA.DbLib` | у RZZ1/DZZ1 — путь к `.PCBLIB` | Наблюдаемая инверсия путей при ручном placement |
| `eComponentObject.PackageSpecificHash` | `64C491D3` | `34B7E33D` | `355ABB39` | те же значения | Стабилен для содержимого footprint; provenance не доказана |
| `eComponentBodyObject.Identifier` | `0402 cap` | `0402 resistor` | пусто | `0402 cap`, `0402 resistor`, пусто | Имя геометрии body; не уникально |
| `eComponentBodyObject.UniqueID` | пусто | пусто | пусто | пусто | Не даёт library body ID |
| `eComponentBodyObject.Component` | `C37` | `R50` | `D8` | `CZZ1` и т.д. | Подтверждает owned link body → placed component |

Покрытие минимального набора: CAP 0402 (`CAP 0402_1005`), RES 0402
(`RES 0402_1005`), SOD323 и body-кандидат `T3DModelType.e3DModelType_Generic`.
Последний подтверждён только статически: `GetState_ModelType` через доступный
мост не читается; STEP-связанные тела CAP/RES/SOD323 использованы как Generic-
кандидаты, а `pcb_get_components` для `T2`/`L2` показывает `body_count=1`,
`step_body_count=0`. Это coverage, а не proof типа.

`lib_probe_footprint()` по трём `.PCBLIB` вернул `success=true`, имя, описание,
`primitive_count` и `height_mm`, но никаких ItemGUID/UniqueId.
`lib_get_footprints()` вернул только `name` и `description`. Ранее полученный
diagnostics (`246AE33E49E4181AFB5D1C6AFEE3ECBE69154F04783F7849ECFCF6E429819173`)
для всех трёх библиотек даёт `footprintSourceUniqueId=null` и
`LIBRARY_FOOTPRINT_UNIQUE_ID_UNAVAILABLE`, а для всех 15 тел —
`bodyUniqueId=null` / `BODY_UNIQUE_ID_UNAVAILABLE`.

**Вывод по identity.** Ни один доступный кандидат не связывает placed
component → library footprint → library 3D body с уникальным стабильным ID.
`UniqueID` — per-instance; `SourceUniqueId` — schematic instance и пуст для
новых размещений; `Pattern`/`Identifier` — имена типов; `PackageSpecificHash`
— интересный, но недоказанный; Item/Revision/Vault GUID не прочитаны в этой
среде. Это не объявление платформенной невозможности, а fail-closed для D-024
на текущих доказательствах.

### 3.3. Проверка повторяемости и save/reopen (Part B)

1. С пользовательской платы через `pcb_save_copy_as` создана копия вне пользовательских документов.
   Пользовательская плата осталась `modified=false`, SHA не изменился.
2. На копии прочитаны начальные значения C37 (CAP), R50 (RES), D8 (SOD323) и body-поля.
3. Через `pcb_place_components` на ту же копию размещены CAP CZZ1/CZZ2,
   RES RZZ1 и SOD323 DZZ1 из тех же `.PCBLIB`; `pcb_get_components` показал
   рост числа components с 130 до 134.
4. Копия сохранена (`app_save_document`), документ копии закрыт
   (`app_close_document`) и снова открыт (`pcb_focus_board`). Приложение Altium
   не закрывалось и не перезапускалось; разрешение на close/restart не требовалось.
5. Повторное чтение `obj_query` на reopened copy дало те же значения всех полей
   из таблицы §3.2.

Результат сравнения:

- `Pattern`, `PackageSpecificHash`, `Identifier` стабильны между исходными и
  заново размещёнными экземплярами одного footprint/body.
- `UniqueID` у двух CAP-размещений различен (`GEWPUSUV` vs `RMHWOOGH`), то есть
  это instance ID, а не library identity.
- `SourceUniqueId` у новых размещений пуст, у исходных — schematic-sourced path.
- `body UniqueID` пуст и до, и после save/reopen.
- У RZZ1/DZZ1 `SourceFootprintLibrary` и `SourceComponentLibrary` поменяны
  местами относительно исходных размещений: ручное placement не воспроизводит
  schematic-derived SourceUniqueId и не даёт надёжного library link.

Это подтверждает риск V-07: стабильность library-level ID после
`LoadFromLibrary`/save/reopen **не доказана**, а доступные instance-поля не
могут заменить ключ D-024.

### 3.4. Независимая геометрическая проверка (Part C)

Использованы AP214 STEP-артефакты BRIDGE-000023:

- `reference/Dev_board-full.step`, SHA-256
  `DBCAB65898A7BA2E581111EBE3C21E521FACE46FA8DAB1D151A0AFF23E04F880`;
- component STEP assets (`C37.step`, `C25.step`, `R50.step`, `R25.step`,
  `D8.step`, `D9.step`) из того же проверенного package;
- измерительные значения `BRIDGE-000025/diagnostics.json`, SHA-256
  `246AE33E49E4181AFB5D1C6AFEE3ECBE69154F04783F7849ECFCF6E429819173`.

Единицы, оси и reference point:

- STEP — mm (`SI_UNIT(.MILLI.,.METRE.)`); diagnostics — IU с
  `1 IU = 0.00000254 mm`, `10000 mil = 1 IU`.
- Board-local STEP координаты = full STEP − `(25.4, 25.4, 0) mm`; сдвиг
  подтверждён совпадением placement origin всех 15 наблюдений.
- `IPCB_Model.GetOrigin()` X/Y — абсолютные board-координаты в IU. Точка
  отсчёта проверки `P = (xMm − 25.4, yMm − 25.4, z_frame)`, где `z_frame = 0`
  для Top и `−1.614001632 mm` для Bottom из STEP placement. Это **не** bbox
  corner и не model-local origin. Z из `GetOrigin()` недоступен по контракту.
- Component rotation θ взят из diagnostics (`0/90/180/270°`); model rotation ψ
  принят `0` по D-024 §3 и отдельно в этой задаче не измерялся.
- Локальная landmark-точка `v` взята из base BREP полного STEP; выбирались
  вершины, не являющиеся bbox corner: хотя бы одна координата не на экстремуме
  своей оси.

Transform для проверки (ψ = 0):

```text
X_b = Rz(θ) · (1, 0, 0)
Z_b = Rz(θ) · (0, −1, 0)   // Top
Z_b = Rz(θ) · (0, +1, 0)   // Bottom, Rx(180) в board frame
Y_b = Z_b × X_b
E   = P + vx·X_b + vy·Y_b + vz·Z_b
```

Фактическая координата `A` бралась из placement frame
`AXIS2_PLACEMENT_3D` полного STEP; residual = `|A − E|`.

Проверка 1 — placement origin, 15 тел Top + Bottom:

| Наблюдение | Максимальный residual |
|---|---:|
| 15 тел, Top + Bottom | `0.000004577 mm` (C37) |

Направления X-axis STEP для всех 15 наблюдений совпали с ожидаемыми для
`θ ∈ {0,+90,180,270}`; для Bottom зафиксирован общий `z = −1.614001632 mm`.

Проверка 2 — по три независимые landmark-точки на модель:

| Модель / body | Landmark 1 | Landmark 2 | Landmark 3 | Max residual |
|---|---|---|---|---:|
| CAP, C37 Bottom 180° | `(-0.3, 0.012, 0.25)` | `(-0.3, 0.512, 0.25)` | `(0.3, 0.012, 0.25)` | `0.000004610 mm` |
| CAP, C25 Top 180° | те же | те же | те же | `0.000001864 mm` |
| RES, R50 Bottom 0° | `(-0.48, 0.33, 0)` | `(-0.48, 0.02, 0)` | `(-0.48, 0.02, 0.5)` | `0.000003714 mm` |
| RES, R25 Top 0° | те же | те же | те же | `0.000001580 mm` |
| SOD323, D8 Top 180° | `(-0.85, 0.75, −0.1625)` | `(-0.825405, 0.925, −0.1625)` | `(-0.825405, 0.925, 0.1625)` | `0.000001619 mm` |
| SOD323, D9 Bottom 180° | те же | те же | те же | `0.000003540 mm` |

Общий максимум по 18 landmark-точкам — `0.000004610 mm`, что укладывается в
допуск `≤ 0.001 mm` D-024 §2. Пересчитанный `e` из `GetOrigin()` остаётся
практически нулевым (максимум `0.00000254 mm` в BRIDGE-000028), но, как и там,
**не является доказательством калибровки**: здесь проверяется transform на
готовых placement, а не создание валидной записи D-024.

## 4. Неподтверждённые вопросы и риски

1. ItemGUID/ItemRevisionGUID/VaultGUID/checksum/model type не удалось прочитать
   через доступные read-only EDA-инструменты; остаётся только статическое
   доказательство сигнатур SDK 4.0.1.
2. `PackageSpecificHash` стабилен на наблюдаемых файловых библиотеках, но его
   происхождение и отсутствие в публичном C# SDK 4.0.1 не подтверждены;
   использовать его как замену `footprintSourceUniqueId` нельзя.
3. Новые ручные placement дают пустой `SourceUniqueId`; это усиливает V-07, но
   не доказывает поведение собственно `IPCB_Component.LoadFromLibrary()` в
   будущем production-коде.
4. Model rotation принят нулевым по контракту D-024; прямое чтение в этой среде
   недоступно.
5. Z из `IPCB_Model.GetOrigin()` недоступен по контракту; Z-совпадение проверено
   только через STEP placement, без повторного вывода толщины платы.
6. Landmark-проверка покрывает CAP/RES/SOD323 на конкретном PCB и конкретном
   STEP package; это не расширяет fail-closed правила D-024 на compound state,
   projection mismatch или произвольные библиотеки.
7. Runtime `GetState_ModelType` недоступен; `e3DModelType_Generic` подтверждён
   только как enum SDK, а не как прочитанный тип конкретного body.

## 5. Проверки: build, test, smoke, версии среды

- `dotnet build -c Release` — успешно, `0` предупреждений, `0` ошибок.
- `dotnet test -c Release --no-build` — успешно: `195` Altium + `94` SOLIDWORKS
  + `229` Core = `518` тестов, `0` проваленных, `0` пропущенных.
- `git diff --check` — без замечаний.
- ALTIUM: `26.12.2.9`, bridge script `2026.11.25.1`,
  `running_script_version` совпадает с `deployed_script_version`.
- SDK: Altium Designer 26, SDK 4.0.1, source SHA указан в §3.1.
- Smoke в SOLIDWORKS: не выполнялся, так как задача исследовательская и не
  меняет production placement; CAD-прогон остаётся критерием будущей
  production-реализации D-024 §5.
- User PCB: не сохранялась; SHA-256 совпадает с зафиксированным в BRIDGE-000025.

## 6. Commit SHA и ссылка на PR

- Commit: `PENDING`
- PR: `PENDING`

## 7. История изменений

- 2026-11-26: создан отчёт со статической проверкой SDK-сигнатур, runtime
  identity-значениями, save/reopen-сравнением и независимой STEP-landmark
  проверкой; вывод — вариант B, D-024 без изменений, production Bottom
  по-прежнему Unsupported.
