---
id: BRIDGE-BOOTSTRAP-001
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T00:00:00+03:00
updated: 2026-11-24T00:22:00+03:00
base: source
branch: bootstrap/project-docs
commit: <fill after commit>
pr: <fill after PR>
related: [BRIDGE-000001, BRIDGE-BOOTSTRAP-002]
---

# BRIDGE-BOOTSTRAP-001 — Отчёт об инициализации документации

## 1. Итог

**Статус: ready_for_review.**

Создана постоянная техническая документация и структура репозитория ECAD-MCAD Bridge.
Ветка `bootstrap/project-docs` создана от `source`; ветка `source` не изменялась.

Выполнено:

- создан обязательный набор файлов `.agent/` и корневые `AGENTS.md`, `README.md`;
- зафиксированы стек, архитектура двух Add-in и Core, Git-протокол взаимодействия;
- создан `SDK_API_LEDGER.md` с разделением на подтверждённые и требующие проверки API;
- создан `DECISIONS.md` с ADR ранее согласованных решений;
- создано первое техническое задание `BRIDGE-000001` по каркасу Altium Add-in;
- создан этот отчёт;
- выполнена проверка структуры, front matter, ссылок и качества Markdown.

В рамках `BRIDGE-BOOTSTRAP-002` по результатам ревью дополнительно:

- зафиксирована подтверждённая оператором совместимость AD26 + SDK 4.0.1 + .NET 10
  с `net10.0-windows` (C-65); InteractiveBom под .NET 8 остаётся только референсом;
- разделён Target Framework: Altium — `net10.0-windows`, Core/Core.Tests — `net10.0`,
  SOLIDWORKS — отдельным решением (D-014);
- изменён контракт координат: исходные IU и вычисленные мм сохраняются одновременно (D-015);
- Git-протокол разработки отделён от runtime-обмена; пользовательские PCB, STEP и рабочие сборки
  через Git не передаются (D-016).

Не выполнено (сознательно, вне scope):

- код Add-in/Core/тестов не создан — это задача `BRIDGE-000001`;
- smoke-тест в Altium Designer не проводился — на bootstrap-этапе нет исполняемого Add-in;
- обмен с SOLIDWORKS не проектировался и не реализовывался;
- CI/CD, установщик, подпись сборок не создавались.

## 2. Созданные и изменённые файлы

| Путь | Тип | Комментарий |
|---|---|---|
| `AGENTS.md` | создан; изменён BRIDGE-BOOTSTRAP-002 | Правила агента, ограничения, формат отчёта, PR-процесс; разделённый TFM и запрет runtime-данных в Git |
| `README.md` | создан; изменён BRIDGE-BOOTSTRAP-002 | Входная точка проекта, стек, краткая архитектура, IU+мм, Git и runtime-обмен |
| `.agent/PROJECT_CONTEXT.md` | создан; изменён BRIDGE-BOOTSTRAP-002 | Границы, стек, статус, роли, словарь, вопросы Q-001…Q-009; Q-001 закрыт |
| `.agent/ARCHITECTURE.md` | создан; изменён BRIDGE-BOOTSTRAP-002 | Компоненты, TFM по проектам, модель данных с IU+мм, Exchange Package, Git-протокол, runtime-обмен, установка Add-in |
| `.agent/DEVELOPMENT_RULES.md` | создан; изменён BRIDGE-BOOTSTRAP-002 | Инженерные правила, TFM по проектам, единицы, Git/runtime-граница, Definition of Done |
| `.agent/SDK_API_LEDGER.md` | создан; изменён BRIDGE-BOOTSTRAP-002 | Сигнатуры C-01…C-64, контракт C-65, вопросы V-02…V-17; V-01 закрыт через C-65 |
| `.agent/DECISIONS.md` | создан; изменён BRIDGE-BOOTSTRAP-002 | ADR D-001…D-016, открытые решения O-002…O-009; O-001 закрыт |
| `.agent/tasks/BRIDGE-000001.md` | создан | ТЗ на исследование и каркас Altium Add-in, требования R-01…R-10 |
| `.agent/reports/BRIDGE-BOOTSTRAP-001-report.md` | создан | Этот отчёт |
| `.agent/reports/.gitkeep` | создан | Удержание пустого каталога в Git |
| `.agent/reviews/.gitkeep` | создан | Удержание пустого каталога в Git |

Все файлы — текст (Markdown) или пустые `.gitkeep`. Проприетарные DLL, лицензии,
секреты и данные заказчиков отсутствуют.

## 3. Принятые решения и обоснование

Зафиксированы ранее согласованные решения (полный текст — `.agent/DECISIONS.md`):

| ID | Решение | Обоснование |
|---|---|---|
| D-001 | Два CAD Add-in + общая библиотека Core | Изоляция CAD SDK, возможность тестировать Core без CAD |
| D-002 | Стек AD 26 / SDK 4.0.1 / .NET 10 / C# / SOLIDWORKS 2026; TFM по D-014 | Совместимость Altium подтверждена оператором (C-65); InteractiveBom — только референс |
| D-003 | Запрет DelphiScript и Legacy API | Современный SDK воспроизводим и поддерживаем |
| D-004 | SOLIDWORKS вне текущего scope | Сначала стабилизируется ECAD-сторона и контракт Core |
| D-005 | Core не зависит от CAD SDK; обмен через версионируемый пакет вне Git | Проверяемость и независимость от порядка CAD |
| D-006 | Git-протокол разработки для task/report/review/кода | Аудируемость без сетевого сервиса; runtime-данные исключены |
| D-007 | Доказанность API | Снижение риска падений в CAD-хосте |
| D-008 | Bootstrap — только документация | Чистая приёмка первого PR |
| D-009 | Исходные IU + вычисленные мм, явные повороты | Предотвращение ошибок масштаба и потери точности |
| D-011 | Запрет проприетарных бинарников в репозитории | Юридические и размерные риски |
| D-012 | PR в `source`, автор не мержит без проверки | Аудит и независимая проверка |
| D-013 | TFM не меняется молча | Конфликт совместимости требует решения Оператора |
| D-014 | Разделение TFM: Altium `net10.0-windows`, Core/Core.Tests `net10.0`, SOLIDWORKS отдельно | Core и тесты не зависят от Windows-хоста CAD |
| D-015 | Двойное представление координат `xIu/yIu` + `xMm/yMm` | Сохранение исходной точности и проверяемость преобразования |
| D-016 | Разделение Git-протокола разработки и runtime-обмена | Пользовательские PCB, STEP и сборки не попадают в Git |

Дополнительно принято D-010 в статусе **Proposed**: стабильные ID на базе
`SourceUniqueId` с fallback-хешем. Перевод в `Accepted` — после эксперимента R-08
в `BRIDGE-000001`.

O-001 закрыт подтверждением оператора (C-65). Остальные открытые решения вынесены в блок
O-002…O-009 `DECISIONS.md` и не требуют действий в рамках bootstrap-задачи.

## 4. Неподтверждённые технические вопросы и риски

| ID | Вопрос | Почему не подтверждено | Где проверяется |
|---|---|---|---|
| Q-001 / O-001 | ~~Грузит ли AD26 in-process сборку `net10.0-windows`?~~ | **Закрыт:** подтверждено оператором (C-65); .NET 8 в InteractiveBom — только референс | R-01 как регрессионная проверка, `BRIDGE-000001` |
| Q-002 / V-13 | Точный build AD26 на стенде | help/about не зафиксирован | Шаг 2 `BRIDGE-000001` |
| Q-003 / V-06 | Как извлекать 3D-модели для MCAD | API SaveModelToFile/3D не проверен | MCAD-фаза |
| Q-004 / V-14 | SOLIDWORKS interop и загрузка Add-in | Нет проверенных сигнатур | Отдельное ТЗ |
| Q-005 / V-15 / O-003 | UI-стек: WPF или WinForms | Выбор не зафиксирован | R-09, `BRIDGE-000001` |
| Q-006 / V-11 / O-006 | Автоматизация установки расширения | Известен только ручной путь | R-07 |
| Q-007 / V-12 / O-008 | Условия распространения SDK-сборок | Требуется проверка лицензии | R-10 |
| Q-008 / V-07 / O-007 | Стабильность `SourceUniqueId` | Нет эксперимента с ECO | R-08 |
| V-02 / O-002 | kit SDK 4.0.1 (.NET Framework 4.0) против runtime AD26 (.NET 8, 1.0.7383.26074) | Разные TFM и версии сборок | R-02 |
| V-03 | Прямой вызов `Internal_GetCurrentPCBBoard()` | Референс использует reflection | R-03 |
| V-04 | Получение активного документа без reflection | В `IServerDocumentView` нет `GetState` | R-04 |
| V-05, V-08…V-10, V-16, V-17 | Документ, слои, высота, потоки, фильтры итератора | Сигнатуры есть, поведение не проверено | `BRIDGE-000001` |

Риск загрузки `net10.0-windows` снят подтверждением оператора (C-65). Если в будущем
возникнет регрессия, требуется остановка и ADR по D-013; самовольное изменение TFM запрещено.
R-01 в `BRIDGE-000001` сохранён как регрессионная проверка, а не исследование совместимости.

Ограничение доказательной базы: SDK 4.0.1 — это версия API-контракта; версия сборок,
фактически загружаемых AD26, отличается (V-02/O-002). Риск несовместимости reference assemblies
остаётся открытым и проверяется в R-02.

## 5. Проверки

### 5.1 Структура

Проверка выполнялась скриптом на PowerShell из корня репозитория.

```text
STRUCTURE_OK: all 10 required paths present
```

Присутствуют: `AGENTS.md`, `README.md`, 5 корневых документов `.agent/`,
`tasks/BRIDGE-000001.md`, `reports/.gitkeep`, `reviews/.gitkeep`.

### 5.2 Front matter и Markdown

```text
AGENTS.md OK (fences=4)
README.md OK (fences=6)
PROJECT_CONTEXT.md OK (fences=2)
ARCHITECTURE.md OK (fences=18)
DEVELOPMENT_RULES.md OK (fences=6)
SDK_API_LEDGER.md OK (fences=0)
DECISIONS.md OK (fences=0)
BRIDGE-000001.md OK (fences=4)
BRIDGE-BOOTSTRAP-001-report.md OK (fences=4)
STRUCTURE_OK: all 11 required paths present
FRONTMATTER_OK: task and report
LINKS_OK
CONTENT_OK: 9 review checks passed
```

Проверено: обязательные поля front matter задачи и отчёта, отсутствие незакрытых блоков кода,
отсутствие CJK-мусора и скрытых управляющих символов, разрешимость всех относительных ссылок,
наличие 11 обязательных путей и 9 ключевых строк правок `BRIDGE-BOOTSTRAP-002`
(TFM, IU/мм, C-65, D-014…D-016, runtime-обмен).

### 5.3 Build / test / smoke

| Проверка | Результат | Причина |
|---|---|---|
| `dotnet build -c Release` | не выполнялась | В репозитории нет кода и solution; bootstrap — только документация |
| `dotnet test -c Release` | не выполнялась | Нет тестовых проектов |
| Smoke-тест в CAD | не выполнялся | Нет Add-in; запуск CAD не требуется на bootstrap-этапе |

Все три пропуска явные и не являются скрытым провалом проверок. `BRIDGE-BOOTSTRAP-002`
тоже изменяет только документацию, поэтому перечень проверок не меняется.

### 5.4 Версии среды

| Параметр | Значение |
|---|---|
| ОС | Windows |
| Git-ветка | `bootstrap/project-docs` от `source` |
| .NET SDK | 10.0.404 (установлен также 11.0.100; для проекта используется 10.0.x) |
| Altium SDK | 4.0.1, `Altium Developer\\SDK4.0\\Version.txt`; SDK.ini: AD 18.0+ |
| Altium SDK kit assembly | `Altium.SDK` 1.0.6542.30251, .NET Framework 4.0 |
| AD26 runtime assembly | `Altium.SDK` 1.0.7383.26074, .NET 8 (из предоставленного InteractiveBom) |
| Референсный Add-in | `InteractiveBomPlug` 1.0.0.0, `net8.0-windows` |
| Altium Designer | 26, точный build не зафиксирован (Q-002); совместимость с .NET 10/`net10.0-windows` подтверждена оператором (C-65) |
| SOLIDWORKS | 2026, не используется; TFM — O-009 |

## 6. Commit SHA и Pull Request

| Поле | Значение |
|---|---|
| Ветка | `bootstrap/project-docs` |
| База | `source` |
| Commit (BRIDGE-BOOTSTRAP-001 документация) | `8321a578ee3dee59ee47466750d24b15caba51b1` |
| Commit (BRIDGE-BOOTSTRAP-001 отчёт SHA/PR) | `c2d8f4775c750523e4983c08683788d10cdc37c0` |
| Commit (BRIDGE-BOOTSTRAP-002 документация) | `f4365aeb9245b1f6ea81a9d33355f701e0d0b362` |
| Commit (BRIDGE-BOOTSTRAP-002 отчёт SHA/PR) | `<fill after commit>` |
| Pull Request | https://github.com/YOUASSBEE/EcadMcadBridge/pull/1 |
| Заголовок PR | `BRIDGE-BOOTSTRAP-001: инициализация документации ECAD-MCAD Bridge` |

## 7. История изменений

| Дата | Изменение |
|---|---|
| 2026-11-24 | Создан начальный набор документации и отчёт; заполнены все разделы, кроме SHA/PR |
| 2026-11-24 | Добавлены commit SHA и ссылка на PR отдельным коммитом |
| 2026-11-24 | BRIDGE-BOOTSTRAP-002: TFM по проектам, IU+мм, разделение Git/runtime; отчёт дополнен |
