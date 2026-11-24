---
id: BRIDGE-000001
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T03:05:00+03:00
updated: 2026-11-24T09:41:47+03:00
base: source
branch: task/BRIDGE-000001
commit: af9bb67a40c65619e02f8d5ab867ec3bcddd59e8
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/2
related: [BRIDGE-000001, D-002, D-007, D-013, D-014, D-015, D-016, D-017]
---

# BRIDGE-000001 — отчёт об исполнении

## 1. Итог

Каркас Altium Add-in `EcadMcadBridge` создан, собирается на согласованном стеке, загружается
в Altium Designer 26.12.2.9 и регистрирует одну команду `EcadMcadBridge:ExportSmokeSnapshot`
в `PCB → Tools`. Команда читает активную `.PcbDoc` только через типизированные `PCB.IPCB_*` API
и пишет диагностический JSON в `%TEMP%\EcadMcadBridge\`; запись в `.PcbDoc` не выполняется.
SOLIDWORKS-код, DelphiScript, Legacy API, reflection/dynamic в основном пути и проприетарные DLL
в репозиторий не добавлены.

Задача выполнена в пределах scope. Полный обмен, ECO, SourceUniqueId и MCAD-геометрия
не реализованы — это последующие этапы.

## 2. Изменённые файлы

```text
EcadMcadBridge.sln
global.json
Directory.Build.props
.gitignore
src/EcadMcadBridge.Core/EcadMcadBridge.Core.csproj
src/EcadMcadBridge.Core/Diagnostics/AltiumUnits.cs
src/EcadMcadBridge.Core/Diagnostics/SmokeSnapshotJson.cs
src/EcadMcadBridge.Core/Diagnostics/SnapshotFileWriter.cs
src/EcadMcadBridge.Core/Model/SmokeCoordinate.cs
src/EcadMcadBridge.Core/Model/SmokeSnapshot.cs
src/EcadMcadBridge.Altium/EcadMcadBridge.Altium.csproj
src/EcadMcadBridge.Altium/Bootstrap/PluginFactory.cs
src/EcadMcadBridge.Altium/Bootstrap/BridgeServerModule.cs
src/EcadMcadBridge.Altium/Bootstrap/BridgePanel.cs
src/EcadMcadBridge.Altium/Adapter/PcbSnapshotReader.cs
src/EcadMcadBridge.Altium/Application/BridgeLogger.cs
src/EcadMcadBridge.Altium/Application/ExportSmokeSnapshotHandler.cs
src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.ins
src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.rcs
tests/EcadMcadBridge.Core.Tests/EcadMcadBridge.Core.Tests.csproj
tests/EcadMcadBridge.Core.Tests/AltiumUnitsTests.cs
tests/EcadMcadBridge.Core.Tests/SmokeSnapshotJsonTests.cs
.agent/SDK_API_LEDGER.md
.agent/DECISIONS.md
.agent/PROJECT_CONTEXT.md
README.md
.agent/reports/BRIDGE-000001-report.md
```

Пользовательские PCB, STEP, логи и DLL в Git не добавлены. `Directory.Build.local.props`
(локальный путь к AD26 SDK) остаётся git-ignored.

## 3. Принятые решения и обоснование

1. **Reference assemblies** — сборки установленного AD26 (`$(AltiumSdkDir)\Altium.SDK.dll`,
   `Altium.SDK.Interfaces.dll`, `Private=false`). Kit SDK 4.0.1targets .NET Framework 4.0 и не
   совместим с `net10.0-windows`; сигнатуры при этом сверены с контрактом 4.0.1 и метаданными
   runtime-сборок. Без изменения архитектуры и TFM (D-002/D-013/D-014).
2. **COM-контракт D-017** — `[assembly: ComVisible(true)]`, assembly GUID
   `f2c9b1a4-6d5e-4c8b-9a31-7e0f2b8d4c10`, `PluginFactory : IPluginFactory` и
   `BridgeServerModule` с `ClassInterfaceType.AutoDispatch`. Точка входа
   `CSharpPlugin.PluginFactory.InvokePluginFactory(DXP.IClient)` подтверждена загрузкой в хосте.
3. **Загрузка сервера** — минимальная WinForms-панель `EcadMcadBridgePanel`
   (`ServerPanelForm`/`ServerPanelView`, `AddServerView` + `client.AddServerView`) используется
   как триггер старта модуля при загрузке AD. Для сообщений команды используется хост-диалог
   `DXP.Utils.ShowInfo/ShowWarning`, кастомный WPF/UI не вводится.
4. **Меню** — `.rcs` вставляет команду в PCB Tools через `MNPCB_Tools10`; это конфигурация
   установки, а не архитектурное изменение. UIA подтверждает пункт `Export Smoke Snapshot`.
5. **Единицы** — `xIu/yIu` сохраняются как исходные целые IU, `xMm/yMm` вычисляются
   `EDP.Utils.CoordToMMs`; округления IU нет (1 IU = 0.00000254 mm).

## 4. Ответы на исследовательские вопросы R-01…R-10

| ID | Статус | Доказательство |
|---|---|---|
| R-01 | Подтверждено | Лог `%TEMP%\EcadMcadBridge\logs\bridge.log`: `BOOTSTRAP_FACTORY addin=0.1.0 clr=10.0.10`, `BOOTSTRAP_COMMANDS command=ExportSmokeSnapshot`, `BOOTSTRAP_MODULE module=EcadMcadBridge` при каждом запуске AD26 26.12.2.9. UIA-дерево содержит `Tools → Export Smoke Snapshot`; скриншот `final-tools-menu.png`. |
| R-02 | Подтверждено | Сборка из runtime-сборок AD26 (`Altium.SDK` assembly 1.0.7383.26074, `Altium.SDK.Interfaces` 1.0.0.0) проходит и загружается в тот же AD26; kit 4.0.1 несовместим с `net10.0-windows`. Зафиксировано в C-66, V-02, O-002. |
| R-03 | Подтверждено (уточнена формулировка) | Вместо внутреннего `Internal_GetCurrentPCBBoard()` используется публичный типизированный helper `PCB.IPCB_ServerInterfaceHelper.GetCurrentPCBBoard()` (C-18). Код `PcbSnapshotReader.cs` не содержит reflection/dynamic; smoke-тест вернул `components=28`, `outlinePoints=42`. |
| R-04 | Подтверждено | `view.GetOwnerDocument()` (C-67) + `document.GetKind()` (C-68) без reflection; не-PCB документ отклоняется понятной ошибкой. |
| R-05 | Подтверждено для синтетической платы | `componentCount=28` из `BoardIterator_Create` с фильтром `eComponentObject` совпадает с отчётом AD26 `Board Information → Components on board = 28` (UIA, скриншот `board-info-reference.png`). |
| R-06 | Подтверждено | `outlinePointCount=42` > 0; первая точка контура `xIu=40216536`, `yIu=40314961` → `xMm=102.15`, `yMm=102.4` через `CoordToMMs`. Warning-пути реализованы и покрыты тестами Core; на реальной плате с отсутствующим контуром не воспроизводились. |
| R-07 | Частично | Пошаговая установка описана в `README.md`: копирование пяти файлов в `Extensions\EcadMcadBridge`, правка `ExtensionsRegistry.xml` с резервной копией, перезапуск AD. Автоматизация шага реестра и перенос на другой стенд не реализованы (O-006/V-11). |
| R-08 | Не проверено | `SourceUniqueId` не используется каркасом и в JSON не попадает. Сравнение до/после сохранения и простого ECO не выполнялось, чтобы не изменять тестовую плату и не экономить внесенную в неё информацию; остаётся открытым V-07. |
| R-09 | Подтверждено частично | Хост-диалог `ShowInfo` с заголовком `EcadMcadBridge` и кнопкой OK зафиксирован скриншотом `final-success-dialog.png` и OCR (`Smoke snapshot written to: ...smoke-snapshot-20261123T235829755Z.json`). Минимальная WinForms-панель отображается в UIA. Отдельный smoke кастомной WPF/WinForms-панели не проводился (V-15). |
| R-10 | Не закрыто | SDK-сборки в репозиторий не коммитятся (`.gitignore`: `*.dll`, `*.exe`); используется локальный `AltiumSdkDir`. Файл `EULA.pdf` найден в `E:\Program Files\Altium\AD26`, но согласия на распространение сборок не получено. Юридическое решение остаётся за Оператором (V-12/O-006). |

### Дополнительные проверки и риски

- **Повторяемость:** команда запускалась трижды в одной сессии без перезапуска AD
  (23:57:32Z, 23:58:29Z, 23:58:34Z); каждый раз создавался новый JSON и диалог с путём.
- **Неизменность `.PcbDoc`:** для синтетической платы SHA256 до и после всех запусков —
  `EC13CC84307C6393263624A1A2F4C10DDB767640B2860C3E71E655098A87E031` (1487360 байт); сохранение не выполнялось.
- **Формат JSON:** UTF-8 без BOM, LF (19 переводов строк, 0 CR), детерминированный порядок
  полей, только имя документа. Проверено на `smoke-snapshot-20261123T235834688Z.json`.
- **Отсутствие активной платы:** код возвращает `E_NO_ACTIVE_BOARD` и показывает
  `ShowWarning("No active PCB document. Open a .PcbDoc and try again.")`. В хосте сценарий
  не воспроизведён: команда намеренно доступна только в контексте `AdvPCB`, поэтому при
  открытом не-PCB документе пункт меню скрыт. Требуется отдельный тест с искусственным
  снятием активной платы; риск считается низким (ветка обработана до обращений к SDK-объектам).
- **Производительность:** на синтетической плате длительность 1–11 мс; для рабочего экспорта больших плат потребуется асинхронный режим.
- **V-02/V-03/V-04/V-13/V-15** уточнены в `SDK_API_LEDGER.md`; V-07, V-11, V-12, V-16,
  V-17 остаются открытыми.

## 5. Проверки

| Проверка | Результат |
|---|---|
| `dotnet build -c Release` | Успешно: `Предупреждений: 0`, `Ошибок: 0` |
| `dotnet test -c Release` | Пройдено 11, пройдено 0, пропущено 0, всего 11 |
| Smoke в AD26 | См. раздел 4; команда видна в PCB Tools, JSON создаётся повторно, `.PcbDoc` не меняется |
| Новые API в ledger | C-66…C-72 добавлены, V-02/V-03/V-04 закрыты |
| Архитектура | D-002/D-007/D-013/D-014/D-017 не изменялись; правки DECISIONS — фиксация исполнения |
| Секреты / DLL / данные заказчиков | В Git отсутствуют; локальные артефакты вне репозитория |
| Diff | Только файлы из раздела 2; `source` не изменялся напрямую |

### Версии среды

- Altium Designer Enterprise: `26.12.2.9` (`Help → About`, `X2.exe`).
- .NET SDK: `10.0.404`; CLR в логе Add-in: `10.0.10`.
- `Altium.SDK.dll`: assembly `1.0.7383.26074`; `Altium.SDK.Interfaces.dll`: `1.0.0.0`.
- `X2.runtimeconfig.json`: `net10.0`, `Microsoft.NETCore.App` / `Microsoft.WindowsDesktop.App` `10.0.10`.
- ОС: Windows 11 Pro, `10.0.26300.0`.

### Шаги smoke-теста (воспроизводимо)

1. Собрать Release, развернуть расширение по инструкции `README.md`, перезапустить AD26.
2. Открыть синтетический `.PcbDoc` (`%TEMP%\EcadMcadBridge\smoke\board.PcbDoc`).
3. В `PCB → Tools` выбрать `Export Smoke Snapshot`; подтвердить диалог с путём OK (2–3 повтора без перезапуска).
4. Проверить последний `%TEMP%\EcadMcadBridge\smoke-snapshot-*.json`, лог и SHA256 платы до/после.

## 6. Commit SHA и PR

- Commit (состояние PR на момент создания): `af9bb67a40c65619e02f8d5ab867ec3bcddd59e8`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/2 — `source` ← `task/BRIDGE-000001`,
  открыт, автором не слит; ожидается проверка Оператора.
- Отдельным коммитом после создания PR в отчёт добавлены только SHA и URL; код не изменялся.
- История ветки переписана по `BRIDGE-000001-FIX-001`: удалены локальный абсолютный путь и данные
  заказчика; `source` не изменялся.

## 7. История изменений

| Дата | Изменение |
|---|---|
| 2026-11-24 | Первая версия отчёта: итог, файлы, ответы R-01…R-10, проверки, версии, smoke-доказательства. |
| 2026-11-24 | Добавлены финальный commit SHA и URL PR #2; PR создан, не слит. |
| 2026-11-24 | BRIDGE-000001-FIX-001: удалены абсолютные пути с именем пользователя и данные заказчика; история ветки переписана, SHA обновлён. |
