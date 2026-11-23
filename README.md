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
| Target Framework | `net10.0-windows` |
| Язык | C# |
| SOLIDWORKS | 2026 |

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

Задачи и отчёты передаются через Git: см. раздел 8 `ARCHITECTURE.md`
и [`.agent/tasks/BRIDGE-000001.md`](.agent/tasks/BRIDGE-000001.md).

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
| [ARCHITECTURE.md](.agent/ARCHITECTURE.md) | Архитектура, формат пакета, Git-протокол |
| [DEVELOPMENT_RULES.md](.agent/DEVELOPMENT_RULES.md) | Инженерные правила, DoD, Git |
| [SDK_API_LEDGER.md](.agent/SDK_API_LEDGER.md) | Доказательная база API |
| [DECISIONS.md](.agent/DECISIONS.md) | ADR и открытые решения |
