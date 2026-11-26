---
id: BRIDGE-000023
type: report
status: blocked
from: local-agent
to: operator
created: 2026-11-26T17:28:29+03:00
updated: 2026-11-26T17:28:29+03:00
base: source
branch: task/BRIDGE-000023-bottom-calibration-evidence
commit: pending
pr: pending
related: [BRIDGE-000016, BRIDGE-000021, BRIDGE-000022, BRIDGE-000023]
---

# BRIDGE-000023 — Real Bottom calibration evidence

## 1. Итог

- **Статус: `blocked`.** Реальные измерения на плате `Dev_board.PcbDoc` выполнены, но **валидных калибровочных записей D-024 получить нельзя**.
- Прочитаны 130 компонентов платы; Bottom-размещений обнаружено 18 (CAP 0402_1005 — 4, RES 0402_1005 — 11, SOD323 — 1, WDFN-8(2x2) — 2). Для исследования выбраны три footprint с STEP-моделями: CAP 0402_1005, RES 0402_1005, SOD323. Подготовлены 9 Bottom-наблюдений и 13 Top-наблюдений; углы `{0,+90,180,270}` представлены.
- Экспортированы 22 реальных component STEP asset и полный STEP платы из AD26. Все файлы находятся **вне Git**, в `%LOCALAPPDATA%\Temp\opencode\bridge-000023\package\`. Созданы `manifest.json`, `checksums.sha256`, `research-evidence.json` и reference STEP. Контрольные суммы проверены.
- D-024 **не подтверждён на реальных CAD-данных в требуемом смысле**: недоступны обязательные поля ключа `bodyUniqueId` и подтверждённый library-level `footprintSourceUniqueId`, а доступный `obj_query eComponentBodyObject.X/Y` оказался координатой левого нижнего угла bbox 3D body, а не `IPCB_Model.GetOrigin`, который требует D-024 как `ModelOrigin`.
- Независимая проверка по полному STEP с подстановкой bbox-corner вместо `ModelOrigin` даёт расхождение ≈0.5–1.25 mm с правилом знака Y из D-024 и ≈0.000001–0.000006 mm без смены знака Y. Этот результат **не является опровержением D-024**, потому что подставленное поле не является требуемым `ModelOrigin`; он показывает, что текущий read-only API не позволяет собрать доказательство, отвечающее D-024 §2/§5.
- Production-код, transform engine, catalog, Exchange Package, SDK_API_LEDGER, D-022/D-024, `.PcbDoc` и любые CAD-бинарники в Git **не изменены**. SOLIDWORKS smoke **не запускался** по условию задачи.

## 2. Изменённые файлы

- `.agent/reports/BRIDGE-000023-report.md` — настоящий отчёт.

В Git не добавлены PCB, STEP, SLDPRT/SLDASM, DLL, лицензии, секреты или данные заказчика. Все сгенерированные CAD-файлы остаются во временном каталоге вне репозитория.

Подготовленный research package вне Git:

| Файл | Назначение | SHA-256 / примечание |
|---|---|---|
| `package/manifest.json` | research manifest со списком 24 файлов | packageId `00000000-0000-0000-0000-000000000023`; capabilities `research-bottom-calibration`, `step-assets` |
| `package/checksums.sha256` | контрольные суммы всех файлов пакета | 24 записи |
| `package/research-evidence.json` | research-доказательства, **не** catalog калибровки D-024 | 22 строки наблюдений, 9 Bottom-кандидатов, provenance, blockers |
| `package/reference/Dev_board-full.step` | независимый full-board STEP | `dbcab65898a7ba2e581111ebe3c21e521face46fa8dab1d151a0aff23e04f880` |
| `package/assets/*.step` | 22 component STEP asset | AP214, SHA-256 в `manifest.json` и в таблицах ниже |

Исходный документ: `Dev_board.PcbDoc`, SHA-256 `0b3d67b6144aa7d766c850daa797287319ca17aab8345be4557bd0d8807d167f`. Агент документ не сохранял; Altium сообщал `modified=true` до и после чтения, изменений агент не вносил.

## 3. Принятые решения и обоснование

### 3.1. Источник D-024 и границы

Task-файл `.agent/tasks/BRIDGE-000023.md` в базовой ветке `source` отсутствует; scope взят из сообщения Оператора. D-024 в `source` отсутствует: он есть только в открытом PR #21 (`origin/pr-21-calibration-contract`, head `ab8307a02327b55eacbd82648fb68fc493619762`). Поэтому все ссылки на D-024 в отчёте относятся к тексту PR #21, а база остаётся `source`.

D-024 остаётся принятым только как `contract only; production activation deferred`. Ни D-022, ни D-023/D-024 не изменялись. Production Bottom остаётся Unsupported.

### 3.2. Происхождение и единицы

- CAD: Altium Designer `26.12.2.9`, EDA bridge script `2026.11.25.1`.
- Сессия EDA: `length_units=mm`, но `obj_query` для `eComponentObject` и `eComponentBodyObject` возвращает `X/Y`, `StandoffHeight`, `OverallHeight` в **mil**. Пересчёт: `1 mil = 10000 IU`, `1 IU = 0.00000254 mm`.
- Board-local system: XY origin контура Outer из BRIDGE-000020; при сравнении с полным STEP применяется подтверждённый сдвиг origin `25.4 mm` по X и Y. Поворот component positive = CCW quarter turn; при нормализации локального offset используется `Rz(-θ)`.
- Все строки наблюдений, таймстемп, library hashes, исходные IU/mm пары и residual сохранены в `research-evidence.json`.

### 3.3. Выборка и идентичность

Наблюдения получены для следующих пар Top reference / Bottom target:

| Footprint | Top reference | Bottom target | Rotation Bottom |
|---|---|---|---|
| CAP 0402_1005 | C31, C32, C25, C29 | C37, C36, C35, C34 | 180, 270, 270, 180 |
| RES 0402_1005 | R25, R40, R36, R26 | R50, R44, R53, R43 | 0, 90, 180, 0 |
| SOD323 | D6, D2, D8, D7, D3 | D9 | 180 |

Hash библиотек:

| Библиотека | SHA-256 |
|---|---|
| `CAP 0402_1005.PCBLIB` | `76832a0cf3744b02f1b33948f5200bf0101bed361c8145caa2ce4bb4cff45f82` |
| `RES 0402_1005.PCBLIB` | `212e5dffb8b7c0910e6b7a62f21abee4ded4bfbfe990be73ae95b41f1da2205e` |
| `SOD323.PcbLib` | `717fcd9e18522912c0d0040109e4df48774da5955e2a4e13e1793cb719e19456` |

### 3.4. Bottom-наблюдения: ключ и asset

`footprintSourceUniqueId` ниже — это значение `SourceUniqueId` **placed component**, а не подтверждённый library-level ID. D-024 требует стабильного ID исходного footprint; значения различаются между экземплярами одного footprint, поэтому как calibration key они использоваться не могут.

| Bottom | Top ref | Footprint | θ | Placed SourceUniqueId | bodyUniqueId | ordinal | STEP SHA-256 |
|---|---|---|---:|---|---|---:|---|
| C37 | C25 | CAP 0402_1005 | 180 | `\CMIFTJDY\PTRNHLDH` | unavailable | 0 | `59a5b7320690042655df55350c4df28365352e19b882a200ff63b72766534284` |
| C34 | C25 | CAP 0402_1005 | 180 | `\CMIFTJDY\OPCEDOAV` | unavailable | 0 | `e5cb8b091943a54f62fc1dde5c43304a9cde6ad5756994c66e27454094335bc2` |
| C36 | C29 | CAP 0402_1005 | 270 | `\CMIFTJDY\ZRSORZXV` | unavailable | 0 | `9a591c0f5c014ed795e847759fe2a30990d7d27afbde5b381b0092d2113df1eb` |
| C35 | C29 | CAP 0402_1005 | 270 | `\CMIFTJDY\IYGVMWRN` | unavailable | 0 | `3eadd6a3d6352d83f6ff322bb871efb0252e513f3c74d9ce8b3575b2a893a31c` |
| R50 | R25 | RES 0402_1005 | 0 | `\CMIFTJDY\HQCSGDHA` | unavailable | 0 | `0ed90260900d03a2f7b7b3172c43b5dca66197bbb5159fd9107d05ea4b503e06` |
| R43 | R25 | RES 0402_1005 | 0 | `\CMIFTJDY\WBPDDSWC` | unavailable | 0 | `e43a055cda66edb16ecf5ef3eb23dc645eacbb1902826860040a02a76b2b7c5b` |
| R44 | R36 | RES 0402_1005 | 90 | `\CMIFTJDY\KZRSMNVT` | unavailable | 0 | `fbf9d02bdfeaf4165225a81e7cde39edc46260490ace9e79c3d93833451cc98d` |
| R53 | R26 | RES 0402_1005 | 180 | `\CMIFTJDY\JHEUNOJL` | unavailable | 0 | `ffff06f19f6436e6fb14e920c9677ef9b70d61f6e3ddd74983987374a376aeb4` |
| D9 | D8 | SOD323 | 180 | `\CMIFTJDY\ZGYNBWCZ` | unavailable | 0 | `eb4c22355753db4298c9661717d77c334a6c60004a4fbb01916f8c322d0091af` |

`bodyUniqueId` недоступен: у всех прочитанных `eComponentBodyObject` значения `SourceUniqueId` и `UniqueID` пусты; `Identifier` содержит только имя модели (`0402 cap`, `0402 resistor`) или пустую строку (SOD323). Список свойств `obj_query` не содержит поля body ID. `ObjectId` — instance-like и не является стабильным library body ID. По D-024 при отсутствии подтверждённого стабильного ID калибровка **не создаётся**.

### 3.5. Измеренные offsets и проверка знака

Значения ниже получены из `obj_query X/Y` и нормализованы как `Rz(-θ)·(BodyXY−ComponentXY)`. Они обозначены `proxy`, потому что проверено: эти значения равны левому нижнему углу bbox библиотечного body (CAP `(-0.4999,-0.2619)`, RES `(-0.50008,-0.25005)`, SOD323 `(-1.25,-0.625)` mm), а не `ModelOrigin`. D-024 требует `ModelOrigin`; поэтому эти числа пригодны только для трассировки эксперимента, но не для валидной calibration record.

| Bottom | d_top IU (proxy) | d_bottom_local IU (proxy) | d_bottom_board IU (D-024) | e IU (D-024) | e mm (D-024) | e mm (без flip Y) | residual mm D-024 | residual mm без flip |
|---|---|---|---|---|---|---|---:|---:|
| C37 | `[-196818,-103119]` | `[-196818,-103181]` | `[-196818,103181]` | `[0,206300]` | `[0,+0.524002]` | `[0,-0.00015748]` | 0.524156242 | 0.000004398 |
| C34 | `[-196818,-103119]` | `[-196818,-103181]` | `[-196818,103181]` | `[0,206300]` | `[0,+0.524002]` | `[0,-0.00015748]` | 0.524155807 | 0.000003972 |
| C36 | `[-196818,-103119]` | `[-196818,-103181]` | `[-196818,103181]` | `[0,206300]` | `[0,+0.524002]` | `[0,-0.00015748]` | 0.524158815 | 0.000002509 |
| C35 | `[-196818,-103119]` | `[-196818,-103181]` | `[-196818,103181]` | `[0,206300]` | `[0,+0.524002]` | `[0,-0.00015748]` | 0.524161116 | 0.000001810 |
| R50 | `[-196882,-98444]` | `[-196882,-98406]` | `[-196882,98406]` | `[0,196850]` | `[0,+0.499999]` | `[0,+0.00009652]` | 0.499899362 | 0.000003123 |
| R43 | `[-196882,-98444]` | `[-196882,-98406]` | `[-196882,98406]` | `[0,196850]` | `[0,+0.499999]` | `[0,+0.00009652]` | 0.499899282 | 0.000003203 |
| R44 | `[-196882,-98444]` | `[-196882,-98406]` | `[-196882,98406]` | `[0,196850]` | `[0,+0.499999]` | `[0,+0.00009652]` | 0.499902717 | 0.000000829 |
| R53 | `[-196882,-98444]` | `[-196882,-98406]` | `[-196882,98406]` | `[0,196850]` | `[0,+0.499999]` | `[0,+0.00009652]` | 0.499899294 | 0.000005877 |
| D9 | `[-492125,-246063]` | `[-492126,-246063]` | `[-492126,246063]` | `[-1,492126]` | `[-0.00000254,+1.25000004]` | `[-0.00000254,0]` | 1.250002655 | 0.000002768 |

Независимый residual вычислен по полному STEP платы и экспортированному STEP компонента: для каждой пары сравнивался перенос bbox/ориентиров asset после коррекции origin платы и `Rz(-θ)`. Расчёт не переиспользует `bodyUniqueId` и не читает SDK model origin. Порог D-024 §2 — `≤0.001 mm`; ни одна D-024-кандидатная запись его не проходит при подстановке bbox-corner. Это не позволяет ни принять, ни отвергнуть математику D-024: входные данные не соответствуют определению `ModelOrigin`.

Вывод по знаку Y: **не подтверждён и не опровергнут**. Для измеренного proxy смена знака Y ухудшает совпадение с полным STEP; но proxy не является требуемым полем контракта.

### 3.6. Толщина платы

- `pcb_special_strings .Total_Thickness` = `1.634 mm`.
- Full board STEP: solid Z range `-1.614002…0 mm`, thickness `1.614002 mm`.
- `pcb_physical_stack`: copper+dielectric = `1.614003 mm`; сумма всех 15 слоёв с пастой/масками/оверлеями = `1.659723 mm`; span Z = `1.647022 mm`.
- Расхождение BoardProperties vs STEP/copper+dielectric: `0.019998 mm`. Причина не установлена; в D-024 thickness должна читаться из package, а не из STEP. Ни код, ни данные package не изменялись.

## 4. Неподтверждённые вопросы и риски

1. **Эскалация (главный блокер).** Как получить для реальных placed bodies `bodyUniqueId`, библиотечный `footprintSourceUniqueId` и `IPCB_Model.GetOrigin()` через подтверждённый read-only SDK/API, не меняя production-код?
   - Вариант A (рекомендуется): выдать отдельный read-only diagnostic API/access, вернуть стабильные library/body ID и model origin, затем повторить измерения и проверить D-024 §5 на 3+ записях для 2+ footprint с ненулевым rotation.
   - Вариант B: считать `obj_query X/Y` (bbox corner) источником ModelOrigin и подготовить ADR на изменение D-024. Не рекомендуется: это противоречит определению поля и не проходит независимый landmark gate.
   - Вариант C: оставить Bottom заблокированным без production activation. Безопасный fallback, но evidence gate остаётся открытым.
2. `SourceUniqueId` компонентов может быть instance-specific (C-112/V-07). Стабильность library-level ID не доказана.
3. Provenance STEP: `FILE_SCHEMA` подтверждает AP214, но результат `pcb_export_component_model` не сообщает `SdkExport`/`PlacedBodyEffective`. D-024 §5 требует именно эту provenance; она не подтверждена.
4. `Thickness`: BoardProperties и STEP расходятся на `0.019998 mm`. Для Bottom Z это риск, но в рамках задачи не исправлялся.
5. SOLIDWORKS `.SLDASM` с Top/Bottom occurrences, save/reopen, transform readback и `CleanupStatus=Verified` не подготовлены и не запускались: package находится на research-стадии.
6. У WDFN-8(2x2) есть Bottom body, но для него не подтверждены STEP asset в рамках этой выборки; в расчёты он не включён.
7. D-024 в `source` отсутствует; если PR #21 будет изменён/закрыт, ссылка на контракт должна быть обновлена оператором.

## 5. Проверки

- Real CAD read: AD `26.12.2.9`, bridge script `2026.11.25.1`, active document `Dev_board.PcbDoc`, 130 components, 18 Bottom placements.
- 22 STEP assets exported through `pcb_export_component_model`; 24 files in research package; checksum verification: `files=24, bad=[]`.
- STEP headers: `FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'))` — AP214 подтверждён.
- Full STEP board bbox and physical stack measured; discrepancy зафиксирована в §3.6.
- Независимое сравнение landmark/bbox по полному STEP выполнено для 9 пар Bottom/Top; все D-024 proxy-residues > 0.001 mm, все no-flip proxy-residues ≤ 0.000006 mm. Результат не является валидацией D-024 из-за несоответствия входных данных определению `ModelOrigin`.
- `git diff --check`: будет проверен перед коммитом; в отчёте фиксируется отдельно в истории после выполнения.
- `dotnet build` / `dotnet test`: **не запускались по условию задачи** (только research materials; production-код не менялся).
- CAD smoke в SOLIDWORKS: **не выполнялся по условию задачи**; package подготовлен только для будущего согласованного прогона.
- Секреты, DLL, лицензии, PCB/STEP/SLDASM/SLDPRT в Git не добавлялись.

## 6. Commit SHA и PR

- Commit: pending (будет обновлено после первого коммита).
- PR: pending (будет обновлено отдельным коммитом после открытия PR в `source`).

## 7. История изменений

- 2026-11-26: создана ветка `task/BRIDGE-000023-bottom-calibration-evidence` от `source` (`ae9e0f7dd4407a3664cdf841210aeb6d828603d3`).
- 2026-11-26: прочитана реальная плата AD26, выбраны 22 footprint/body наблюдения, экспортированы STEP assets и полный board STEP, подготовлен research package вне Git.
- 2026-11-26: выполнена независимая проверка по full STEP; обнаружено, что доступный `obj_query X/Y` — bbox corner, а не `ModelOrigin`, и что обязательные ID D-024 недоступны. Статус переведён в `blocked`; production-изменения и smoke не выполнялись.
