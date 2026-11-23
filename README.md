# ECAD-MCAD Bridge

Проект интеграционного слоя между electronic CAD (ECAD) и mechanical CAD (MCAD):
два независимых CAD Add-in и общая библиотека Core с версионируемым форматом обмена.

## Назначение

- устранить ручной перенос геометрии и посадочных мест между Altium Designer и SOLIDWORKS;
- сделать обмен детерминированным, проверяемым и воспроизводимым;
- сохранить прослеживаемость каждой передачи данных;
- обеспечить возможность развития обмена в обе стороны без переделки архитектуры.

> **Текущий этап:** bootstrap — создаётся только документация и структура репозитория.
> Код ещё не реализован.

## Согласованный стек

| Компонент | Версия |
|---|---|
| Altium Designer | 26 |
| Altium SDK | 4.0.1 |
| .NET SDK | 10 |
| Target Framework | Altium: `net10.0-windows`; Core/Core.Tests: `net10.0`; SOLIDWORKS: отдельным решением |
| Язык | C# |
| SOLIDWORKS | 2026 |

Совместимость Altium Designer 26, Altium SDK 4.0.1 и .NET SDK 10 с `net10.0-windows`
подтверждена оператором. Опубликованная DLL InteractiveBom (`InteractiveBomPlug.dll`) собрана
под `net8.0-windows` и использовалась только как референс; она не определяет целевой TFM.

Источник InteractiveBom разделён по типам данных:

- исходный проект с `.csproj` — в локально доступной копии не найден; состав исходного архива
  не проверялся, декомпилированные файлы не являются исходным кодом;
- опубликованная DLL — `publish_out/InteractiveBomPlug.dll`, `net8.0-windows`, версия 1.0.0.0;
- загрузка в Altium — через `.Ins` (`EditorExePath = 'InteractiveBomPlug.dll'`), `.rcs`
  и COM-фабрику `CSharpPlugin.PluginFactory.InvokePluginFactory(DXP.IClient)`.

SOLIDWORKS 2026 зафиксирован как целевая платформа, но на текущем этапе функциональность обмена
с ним не реализуется. Изменение стека или TFM — только через `.agent/DECISIONS.md`.

## Архитектура (кратко)

```text
Altium Add-in ──► Core ◄── SOLIDWORKS Add-in (будущая фаза)
                   │
                   └──► Exchange Package (JSON + checksums)
```

- `EcadMcadBridge.Core` — домен, валидация и формат пакета; не зависит от CAD SDK.
- `EcadMcadBridge.Altium` — ECAD Add-in; bootstrap, adapter, application, UI.
- `EcadMcadBridge.SolidWorks` — не создаётся до отдельного ТЗ.
- Add-in не ссылаются друг на друга.
- Обмен идёт только через версионируемый файловый пакет Core.
- Все линейные величины из Altium IU сохраняются парой: исходное целое IU и вычисленные мм
  (1 IU = 0.00000254 mm); неподтверждённые значения остаются `null`.
- Git используется для задач, отчётов, ревью и кода; пользовательские PCB, STEP и рабочие сборки
  передаются вне Git через локальный runtime-каталог.

Полная архитектура: [`.agent/ARCHITECTURE.md`](.agent/ARCHITECTURE.md).

## Структура репозитория

```text
AGENTS.md                      правила работы локального агента
README.md                      этот файл
.agent/
  PROJECT_CONTEXT.md           границы, стек, статус, словарь
  ARCHITECTURE.md              компоненты, формат, Git-протокол
  DEVELOPMENT_RULES.md         инженерные и Git-правила
  SDK_API_LEDGER.md            подтверждённые и проверяемые API
  DECISIONS.md                 архитектурные решения (ADR)
  tasks/                       постановки задач
  reports/                     отчёты об исполнении
  reviews/                     ревью
```

Git-протокол разработки (задачи, отчёты, ревью и код) описан в разделе 8 `ARCHITECTURE.md`.
Файловый runtime-обмен между CAD-системами описан в разделе 9 `ARCHITECTURE.md` и не использует Git.
См. также [`.agent/tasks/BRIDGE-000001.md`](.agent/tasks/BRIDGE-000001.md).

## Незыблемые ограничения

- DelphiScript и Legacy API Altium не используются.
- Неподтверждённые API не попадают в категорию подтверждённых.
- `.PcbDoc` не разбирается вручную; чтение — только через `PCB.IPCB_*`.
- Запись в `.PcbDoc` на этапе `BRIDGE-000001` запрещена.
- Ветка `source` изменяется только через Pull Request.
- В репозиторий не коммитятся проприетарные DLL, лицензии, секреты и данные заказчиков.

## Сборка и проверки

Код появится в задаче `BRIDGE-000001`. Планируемые команды:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Smoke-тест в Altium Designer 26 выполняется вручную и фиксируется в report задачи.

## Документы

| Документ | Назначение |
|---|---|
| [AGENTS.md](AGENTS.md) | Обязательные правила для локального агента |
| [PROJECT_CONTEXT.md](.agent/PROJECT_CONTEXT.md) | Контекст, границы, статус, открытые вопросы |
| [ARCHITECTURE.md](.agent/ARCHITECTURE.md) | Архитектура, формат пакета, Git-протокол, runtime-обмен |
| [DEVELOPMENT_RULES.md](.agent/DEVELOPMENT_RULES.md) | Инженерные правила, DoD, Git |
| [SDK_API_LEDGER.md](.agent/SDK_API_LEDGER.md) | Доказательная база API |
| [DECISIONS.md](.agent/DECISIONS.md) | ADR и открытые решения |
