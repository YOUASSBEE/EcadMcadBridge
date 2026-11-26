---
id: BRIDGE-000025
type: report
status: blocked
from: local-agent
to: operator
created: 2026-11-26T20:37:18+03:00
updated: 2026-11-26T20:37:18+03:00
base: source
branch: task/BRIDGE-000025-runtime-validation
commit: b1448547404dcba46a8a802bcdf1919f24db63fc
pr: none
related:
  - BRIDGE-000023
  - BRIDGE-000024
  - PR-23
  - D-024
---

# BRIDGE-000025 — Runtime Validation of Body Identity Diagnostics

## 1. Итог

Статус: **`blocked`**. Runtime-проверка **не выполнена**. Ни один из требуемых полей
(`bodyUniqueId`, library-level `footprintSourceUniqueId`, `IPCB_Model.GetOrigin().X/Y/Z`)
фактически не получен: реальный `diagnostics.json` не создан.

Что сделано:

- Проверены актуальное состояние ветки, базы `source` и PR #23. HEAD PR #23
  `0b407e3f2d0a59fc300e4e0929633a406e9f0ffb` совпадает с постановкой; `state=open`,
  `merged=false`.
- Диагностическая сборка собрана **из этого точного commit** в отдельном worktree вне
  рабочего дерева репозитория: `Release`, `0 warnings, 0 errors`.
- Повторно проверен research package BRIDGE-000023: `files=24 ok=24 bad=[]`; STEP не
  переэкспортировался.
- Подготовлен request JSON со всеми 15 designators вне Git.
- Зафиксированы состояние текущей сессии Altium и хэши установленных DLL — они не
  изменились.

Что **не** сделано и почему:

- Отдельная сессия Altium с диагностической сборкой **не подготовлена**. В машине одна
  установка AD26, один machine-wide каталог расширений, и установленная DLL держится
  текущим X2 с эксклюзивной блокировкой. Замена DLL требует остановки этого
  процесса, что прямо запрещено условием «текущую сессию не трогать».
- Добавление второго именованного расширения или правка `.ins`/`.rcs`/
  `ExtensionsRegistry.xml`/реестра является постоянной регистрацией/изменением
  конфигурации Altium и запрещено; обходные механизмы регистрации не применялись.
- Команда `ReadBodyIdentityDiagnostics` в текущей сессии не исполнялась: она там не
  зарегистрирована. `dispatched=true` / `unlisted_process` не принимаются как
  доказательство.
- 9 Bottom/Top пар **не посчитаны**: нет фактических входных значений. Bbox-подстановка
  не применялась.

BRIDGE-000024 и PR #23 остаются открытыми и `blocked` до появления доказательств.

## 2. Изменённые файлы

В этой ветке:

- `.agent/reports/BRIDGE-000025-report.md` — настоящий отчёт (создан).
- `.agent/tasks/BRIDGE-000025.md` — постановка (создана предыдущим коммитом ветки).

Вне Git (временные артефакты):

- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000025\pr23` — worktree PR #23 head,
  только для сборки.
- `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000025\pr23\src\EcadMcadBridge.Altium\bin\Release\net10.0-windows\EcadMcadBridge.dll`
  и `...\EcadMcadBridge.Core.dll` — собранные диагностические DLL.
- `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000025\diagnostics-request.json`
  — подготовленный request (15 designators).
- `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000025\diagnostics.json` —
  **отсутствует**; это ожидаемый output, а не результат.

Не изменены: D-022/D-024, placement engine, catalog, Exchange Package, SDK ledger,
API contract, `.PcbDoc`, registry, постоянная конфигурация Altium. DLL/STEP/PCB/CAD в Git
не добавлялись.

## 3. Environment и версии

| Параметр | Значение |
|---|---|
| Репозиторий | `YOUASSBEE/EcadMcadBridge` |
| База | `source`, `bdff118b288454be68c7c590d875142620a139e2` |
| Ветка | `task/BRIDGE-000025-runtime-validation` |
| Task commit | `3ec3d6219bfa2ad540242b2ce91190817466da2e` |
| Диагностическая реализация | PR #23 head `0b407e3f2d0a59fc300e4e0929633a406e9f0ffb` |
| PR #23 | `state=open`, `merged=false`, не сливался |
| Altium Designer | `26.12.2.9` (через `app_get_version`) |
| EDA bridge | mcp_server `0.8.2`, bundled script `2026.11.25.1` |
| .NET SDK | `10.0.404`; Altium TFM `net10.0-windows` |
| CLR в предыдущем bootstrap логе | `10.0.10` (старая сборка add-in) |
| Текущий X2 | на старте PID `32260`; с `20:38:02+03:00` PID `33732` (перезапуск **не** агентом), `E:\Program Files\Altium\AD26\X2.EXE`, `candidate_count=1` |
| Bootstrap после рестарта | `17:38:07Z` (= `20:38:07+03:00`): `BOOTSTRAP_COMMANDS commands=ExportSmokeSnapshot,ExportBoardSnapshot` — новой команды нет |
| Активный документ | `D:\Git\Projects\mdm15-1v05m_electric\Dev_board.PcbDoc`, `modified=false` |
| Открытые диалоги | `dialog_count=0` |
| SHA256 `Dev_board.PcbDoc` | `0B3D67B6144AA7D766C850DAA797287319ca17aab8345be4557bd0d8807d167f` |
| Package BRIDGE-000023 | `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000023\package`, `files=24 ok=24 bad=[]` |

## 4. Способ временной загрузки Add-in

**Не применён** — безопасный способ не найден.

Исследование (только read-only):

1. Установка Altium одна: `E:\Program Files\Altium\AD26`; вторая установка отсутствует.
2. Каталог расширений один и machine-wide:
   `C:\ProgramData\Altium\Altium Designer {B4282C90-18BF-448F-BF1A-9E36AE887190}\Extensions`.
   Per-user `Extensions` в `%APPDATA%`/`%LOCALAPPDATA%` не найден. Следовательно любой
   второй instance загрузил бы те же самые DLL с диска.
3. Установленные DLL:
   - `EcadMcadBridge.dll` =
     `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A`;
   - `EcadMcadBridge.Core.dll` =
     `1A106CF0DD31F9D194E760392C460E62C39D4BA078A7DCE6A32953677D567F70`.
   Обе совпадают с CONTROL из BRIDGE-000024 — замены не было.
4. Проверка доступа: открыть `EcadMcadBridge.dll` на запись с `FileShare.None` —
   **отказано** («файл используется другим процессом»); открытие на чтение с
   `FileShare.ReadWrite` — успешно. Проверка повторена после внешнего рестарта X2
   (PID `32260` → `33732`, `20:38:02+03:00`): результат тот же. То есть замена на диске
   невозможна, пока X2 держит сборку загруженной. Рестарт выполнен не агентом; агент
   Altium не закрывал и не перезапускал.
5. В логе моста последняя запись:
   `BOOTSTRAP_COMMANDS commands=ExportSmokeSnapshot,ExportBoardSnapshot` — новой команды
   в загруженном add-in нет.
6. Поиск поддерживаемого переключателя изоляции профиля/расширений в `X2.EXE` и в
   инструментах EDA-моста не дал результата; `app_attach` сообщает
   `candidate_count=1`, `other_servers=[]`.
7. Документация Altium упоминает несколько instance только как отдельные licence seats;
   это не даёт изолированный каталог расширений.

Вывод: подготовить отдельную сессию **с диагностической сборкой** можно только через
остановку текущего X2 (запрещено) или через постоянную регистрацию второго расширения /
правку конфигурации (запрещено). Ни то, ни другое не выполнено.

## 5. Release build диагностической сборки

Команда (из worktree PR #23 head, без изменения рабочей ветки):

```text
dotnet build -c Release -p:AltiumSdkDir="E:\Program Files\Altium\AD26\System" \
  <worktree>\src\EcadMcadBridge.Altium\EcadMcadBridge.Altium.csproj
```

Результат: `Сборка успешно завершена. Предупреждений: 0. Ошибок: 0`.

Фактические SHA256 собранных DLL:

| Assembly | SHA256 |
|---|---|
| `EcadMcadBridge.dll` | `1C0E17428981217A78CE2976EE84428E06323F5E20120C730A91A2CABF052732` |
| `EcadMcadBridge.Core.dll` | `C253FBE3CAD79DF13FF4B216E611168A7B034B9399F2E0E3177CCB494A8A5F62` |

CONTROL-значения из BRIDGE-000024 (`DF756510…` / `13B8848A…`) не совпали. Причина:
сборка выполнялась из другого каталога (временный worktree) с тем же исходным кодом;
код не менялся, расхождение зафиксировано как факт, без корректировки исходников.
Ни одна из этих DLL не копировалась в каталог расширений.

## 6. Подтверждение фактического исполнения команды

**Отсутствует.** Проверено:

- `diagnostics.json` по пути из request — не создан;
- структура результата, записи по designators, availability полей — получить нечего;
- `obj_run_process` в BRIDGE-000024 давал только `dispatched=true`, `unlisted_process`,
  с явным предупреждением моста, что Altium принимает неизвестный process name без ошибки;
  в BRIDGE-000025 эта команда не повторялась, чтобы не создавать ложное впечатление
  успеха.

Команда `EcadMcadBridge:ReadBodyIdentityDiagnostics` существует только в собранной DLL;
в загруженной в текущую сессию сборке её нет.

## 7. Проверяемые объекты и доступность полей

Request подготовлен для всех 15 designators:

`C37, C34, C36, C35, C25, C29, R50, R43, R44, R53, R25, R36, R26, D9, D8`.

Ни один designator не обработан runtime. Доступность полей (фактический ответ отсутствует):

| Поле | Статус | Причина |
|---|---|---|
| `bodyUniqueId` | `unavailable` | команда не исполнена; response отсутствует |
| library-level `footprintSourceUniqueId` | `unavailable` | то же; placed `SourceUniqueId` не подставлялся |
| `IPCB_Model.GetOrigin().X` | `unavailable` | то же; bbox/center не подставлялись |
| `IPCB_Model.GetOrigin().Y` | `unavailable` | то же |
| `IPCB_Model.GetOrigin().Z` | `unavailable` | то же; по SDK-контракту Z всё равно `null` с `MODEL_ORIGIN_Z_UNAVAILABLE` |
| component identity / side / bodyOrdinal | `unavailable` | response отсутствует |
| coordinate system / units / STEP asset SHA-256 | `unavailable` | response отсутствует |

`ambiguous` не фиксировалось: не было ответа, в котором значение можно было бы счесть
неоднозначным. Фиктивные ID и `(0,0)` не генерировались.

## 8. Таблица 9 Bottom/Top пар

Все пары остаются **невычисленными**. Factual `d_top`, `d_bottom_local`,
`d_bottom_board`, `e` не получены; ячейки намеренно оставлены `unavailable`, а не
заполнены bbox-proxy.

| Bottom | Top ref | Footprint | θ bottom | d_top | d_bottom_local | d_bottom_board | e | Статус |
|---|---|---|---:|---|---|---|---|---|
| C37 | C25 | CAP 0402_1005 | 180 | unavailable | unavailable | unavailable | unavailable | no runtime data |
| C34 | C25 | CAP 0402_1005 | 180 | unavailable | unavailable | unavailable | unavailable | no runtime data |
| C36 | C29 | CAP 0402_1005 | 270 | unavailable | unavailable | unavailable | unavailable | no runtime data |
| C35 | C29 | CAP 0402_1005 | 270 | unavailable | unavailable | unavailable | unavailable | no runtime data |
| R50 | R25 | RES 0402_1005 | 0 | unavailable | unavailable | unavailable | unavailable | no runtime data |
| R43 | R25 | RES 0402_1005 | 0 | unavailable | unavailable | unavailable | unavailable | no runtime data |
| R44 | R36 | RES 0402_1005 | 90 | unavailable | unavailable | unavailable | unavailable | no runtime data |
| R53 | R26 | RES 0402_1005 | 180 | unavailable | unavailable | unavailable | unavailable | no runtime data |
| D9 | D8 | SOD323 | 180 | unavailable | unavailable | unavailable | unavailable | no runtime data |

Завершённых пар: **0 из 9**.

## 9. Сопоставление с BRIDGE-000023 и Y-sign rule

Сопоставление не выполнено: нет фактического `ModelOrigin`, с которым можно сравнить
bbox-наблюдения. Точки сравнения из BRIDGE-000023 (`CAP e≈[0,+0.524002]` mm,
`RES e≈[0,+0.499999]` mm, `SOD323 e≈[-0.00000254,+1.25000004]` mm; no-flip residual
`≤0.000006` mm) **не использовались как вход** и не подтверждены.

- Разница по X/Y/Z: не вычислена (нет фактических значений).
- Применимость Y-sign rule D-024: **не проверена**. Ни подтверждения, ни опровержения.
- Подтверждённые наблюдения: только то, что package BRIDGE-000023 по-прежнему валиден
  (`bad=[]`) и что bbox-наблюдения остаются гипотезой.
- Какие пары позволяют вычислить calibration offset: определить нельзя без фактического
  `ModelOrigin`; никакой offset не вычислялся.
- Экстраполяция между footprint-типами не выполнялась.
- Формула D-024 не изменялась. Новых finding-противоречий не зафиксировано: данных для
  противоречия нет.

## 10. Runtime errors, warnings, unavailable fields

| Тип | Значение |
|---|---|
| Fatal blocker | отдельная сессия с диагностической DLL не подготовлена |
| Техническая причина | установленная DLL заблокирована X2 (`FileShare.None` → отказ; и до, и после внешнего рестарта PID `32260` → `33732`); единственный каталог расширений machine-wide; второй установки нет |
| Запрещённый обход | постоянная регистрация, реестр, `.ins`/`.rcs`, `ExtensionsRegistry.xml` — не применялись |
| Команда | не исполнялась; response-файл отсутствует |
| `diagnostics.json` | absent (ожидаемый путь указан в §2) |
| Unavailable fields | все поля из §7 |
| PCB | не изменён; SHA256 до/после одинаков; `modified=false`; диалогов нет |
| CAD smoke | не запускался (SOLIDWORKS вне scope; Altium runtime заблокирован) |

## 11. Ограничения соблюдения

- D-022/D-024, placement engine, catalog, Exchange Package, SDK ledger, API contract —
  не изменены.
- Bottom в production не активирован.
- `Dev_board.PcbDoc` не модифицирован и не сохранялся; текущая сессия не перезапускалась.
- Реестр и постоянная конфигурация Altium не менялись.
- DLL, STEP, PCB, SLDPRT/SLDASM, логи CAD в Git не добавлены.
- bbox как замена `ModelOrigin` не использовался; reflection/`dynamic`/Legacy API/
  DelphiScript не применялись.

## 12. Проверки

| Проверка | Результат |
|---|---|
| Release build PR #23 head | `0 warnings, 0 errors` |
| `dotnet test -c Release` | не запускался: код BRIDGE-000025 не менялся; тесты PR #23 ранее — 195 passed |
| package BRIDGE-000023 | `files=24 ok=24 bad=[]` |
| повторный STEP export | не выполнялся |
| runtime команда | **не выполнена** (blocked) |
| структура/полнота `diagnostics.json` | файл отсутствует |
| PCB SHA256 до/после | совпадает |
| ALTium version | `26.12.2.9` |
| .NET SDK | `10.0.404` |
| PR #23 | `open`, `merged=false` |

## 13. Commit SHA и PR

- Commit, создающий постановку: `3ec3d6219bfa2ad540242b2ce91190817466da2e`.
- Коммит отчёта: `b1448547404dcba46a8a802bcdf1919f24db63fc`. Следующий meta-коммит меняет
  только ссылки на SHA в этом разделе.
- PR для BRIDGE-000025 **не создаётся** по условию задачи.
- PR #23: https://github.com/YOUASSBEE/EcadMcadBridge/pull/23 — `open`, `merged=false`.
  Диагностика собрана из указанного в постановке head
  `0b407e3f2d0a59fc300e4e0929633a406e9f0ffb`. После обновления отчёта BRIDGE-000024
  (только документ, без изменения кода) head PR #23 стал
  `e28881486c69f0fb689f3181f9abda38deefe481`; это не меняет собранных DLL и не переносит
  реализацию на другой HEAD.

## 14. Оставшиеся блокеры и что нужно от Оператора

Чтобы снять `blocked`, нужно одно из:

1. Предоставить **уже подготовленную отдельную машину/сессию Altium 26.12.2.9** с
   изолированным профилем и каталогом расширений, куда можно положить собранные DLL,
   не трогая текущую сессию; либо
2. Явно разрешить контролируемый перезапуск текущего X2 с временной подменой двух DLL,
   сохранением копии PCB вне Git и безусловным восстановлением исходных DLL; либо
3. Закрыть BRIDGE-000025 как `blocked` и перенести рантайм-проверку в среду, где
   диагностическая сборка установлена штатно.

Пока ни одно из условий не выполнено, данные D-024 остаются неподтверждёнными,
bbox-значения — гипотезой, а PR #23 и BRIDGE-000024 — открытыми/`blocked`.

## 15. История изменений

1. Проверены состояние ветки, базы и PR #23; подтверждено совпадение head с постановкой.
2. Собрана Release-диагностика из PR #23 head в отдельном worktree; зафиксированы SHA256.
3. Проверены package BRIDGE-000023, request JSON и состояние текущей сессии Altium.
4. Исследована возможность отдельной сессии; подготовка признана невозможной без
   запрещённых изменений. Статус переведён в `blocked`, отчёт создан.
