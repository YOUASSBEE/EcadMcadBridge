---
id: BRIDGE-000021
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T16:35:46+03:00
updated: 2026-11-26T16:39:30+03:00
base: source
branch: task/BRIDGE-000021-bottom-placement
commit: 94fbc639b28cac7ad1afca3aa34fbcfd6af84247
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/20
related: [BRIDGE-000016, BRIDGE-000017, BRIDGE-000018, BRIDGE-000020, BRIDGE-000021]
---

# BRIDGE-000021 — Bottom placement research scope

## 1. Итог

- Выполнено: принят **D-023** в статусе `Accepted (research only; production activation deferred)`. Bottom placement зафиксирован как отдельное расширение scope, а не продолжение D-022. Документы согласованы между собой; подготовлен проект контракта per-footprint calibration.
- Не выполнено: production Bottom placement в SOLIDWORKS не реализован. Код Core/SOLIDWORKS, Exchange Package и Component Model Catalog не изменены. `SolidWorksPlacementRequestBuilder` по-прежнему возвращает Unsupported для Bottom и projection mismatch.
- Причина: R4-A/R4-B дают fixture-level доказательство только для `BRIDGE_ROT_3D`; измеренный pivot `e=+3.371315 mm` не может использоваться как production-константа. Per-footprint калибровка в пакете отсутствует, CAD-прогон на реальной плате с Bottom occurrence не проводился.
- Task-файл `.agent/tasks/BRIDGE-000021.md` в репозитории отсутствует; scope взят из сообщения Оператора. Production-часть задачи остаётся заблокированной до отдельного ADR и CAD-подтверждения.

## 2. Изменённые файлы

- `AGENTS.md` — §2 и §13: production Bottom остаётся вне scope, открыт research D-023.
- `.agent/DECISIONS.md` — D-022 дополнен ссылкой на D-023; добавлен D-023 и открытое решение O-010.
- `.agent/DEVELOPMENT_RULES.md` — §1.4, §4.1, §4.2 и новый §4.5: research ≠ production activation, fail-closed правила калибровки.
- `.agent/ARCHITECTURE.md` — §6.6 таблица режимов и новый §6.6.1 с проектом контракта калибровки.
- `.agent/PROJECT_CONTEXT.md` — границы текущего этапа, статус BRIDGE-000021 и Q-003.
- `README.md` — пользовательское описание: Bottom — research D-023, production Unsupported.
- `.agent/reports/BRIDGE-000021-report.md` — настоящий отчёт.

CAD-бинарники, пользовательские PCB/STEP, DLL, секреты и данные заказчиков в Git не добавлялись.

## 3. Принятые решения и обоснование

### 3.1. D-023

D-023 принят как разрешение на пересмотр архитектурного решения и research, а не как автоматическая активация Bottom. D-022 остаётся действующим для production-поведения и не помечается `Superseded`: SOLIDWORKS продолжает отклонять Bottom диагностиками `SW-PLC-006`/`SW-PLC-007`, а projection mismatch — `SW-PLC-007`.

Обоснование:

- `AGENTS.md` §2, D-022 и `.agent/DEVELOPMENT_RULES.md` §1.4/§4.2 прямо определяют Bottom как Unsupported; реализация без ADR была бы изменением архитектуры.
- `.agent/reports/BRIDGE-000016-coordinate-calibration.md` §13.6 фиксирует `e=+3.371315 mm` только для scratch fixture `BRIDGE_ROT_3D` и прямо требует per-footprint measurements для production.
- `.agent/reports/BRIDGE-000018-report.md` описывает реальный Top assembly и явно указывает, что Bottom/Native/External runtime не подтверждён.
- В репозитории `PlacementTransformBuilder.BuildBottom` и unit-тесты BRIDGE-000017 уже проверяют Bottom-формулу на fixture-данных, но `SolidWorksPlacementRequestBuilder` не строит Bottom-запросы из пакета. Это unit-level доказательство, а не поддержка production.

### 3.2. Проект контракта per-footprint calibration (требует D-024)

| Аспект | Предложение |
|---|---|
| Хранение | Опциональная секция `footprintCalibration` у `ComponentBodyEntry` Component Model Catalog 1.2.0, внутри Exchange Package вне Git; не в `BoardSnapshot`, не в коде, не в пользовательских PCB/STEP |
| Состав | `schemaVersion`, `calibrationId`, `footprintSourceUniqueId`, `libraryDocumentSha256`, `bodyUniqueId`, `bodyOrdinal`, `stepAssetSha256`, Top/Bottom offsets парами IU/мм, `fixtureId`, `adVersion` |
| Вычисление `e` | `bottomBoard = (bottomLocal.X, -bottomLocal.Y)`; `e = bottomBoard - topLocal`; отдельной константой `e` не хранится |
| Ключ привязки | `(footprintSourceUniqueId, bodyUniqueId, bodyOrdinal, stepAssetSha256)`; designator и позиция на плате ключом не являются |
| Проверка применимости | SHA catalog asset == SHA STEP asset == SHA calibration; совпадение footprint/body id; конечные IU/мм пары и конечный `e`; `SdkExport`, `Generic`, `Embedded`, `PlacedBodyEffective`, Bottom/Bottom, нулевой compound state, rotation из `{0, 90, 180, -90}` |
| Ошибка | Любое отсутствие/рассогласование — явная диагностика `MissingFootprintCalibration`/`Unsupported`; zero/identity/reflection и scale не подставляются |
| Ограничение | Projection mismatch и compound Bottom в research-контракт не входят; Z берётся как `BottomSurfaceZ - StandoffHeight`, ориентация `Rx(180)` без centering/scaling |

Это предложение зафиксировано в D-023 и §6.6.1 ARCHITECTURE, но не реализовано: изменение catalog/package требует отдельного принятого ADR D-024 (O-010).

## 4. Неподтверждённые вопросы и риски

- **Точный блокер production:** нет принятого D-024 с хранилищем/ключом/проверкой применимости калибровки и нет CAD-доказательства на реальной плате с Top и Bottom occurrences. Пока они отсутствуют, заявлять поддержку Bottom нельзя.
- Нет измеренных Top/Bottom offsets для конкретных footprint/body, которые можно было бы безопасно поместить в пакет; R4-A даёт только один fixture.
- Не подтверждено, что proposed catalog 1.2.0 обратно совместим с текущим writer/validator; это потребует отдельных тестов и ADR.
- Projection mismatch и compound Bottom остаются Unsupported; R4-B/R4-C не активируются этим research.
- Отсутствует task-файл BRIDGE-000021; для production-фазы нужна корректная постановка или обновление текущей после D-024.
- Новые Altium/SOLIDWORKS API не добавлялись; SDK_API_LEDGER изменений не требует.

## 5. Проверки

- `dotnet build EcadMcadBridge.sln -c Release`: успешно, **0 предупреждений / 0 ошибок**.
- `dotnet test EcadMcadBridge.sln -c Release --no-build`: **518/518** пройдено — Core 229/229, Altium 195/195, SOLIDWORKS 94/94.
- `git diff --check`: чисто.
- Среда: .NET SDK `10.0.404`, Windows; сборки AD `26.12.2.9` и SOLIDWORKS `2026 SP3.2` взяты из предыдущих отчётов и в этой docs-only итерации не перепроверялись.
- Smoke в CAD: **не выполнялся**, причина — research/docs-only task, production-код и калибровка не реализованы; real Bottom assembly и readback невозможны без D-024 и подготовленного пакета. Это не является доказательством Bottom runtime.

## 6. Commit SHA и PR

- Code/docs commit: `94fbc639b28cac7ad1afca3aa34fbcfd6af84247` — `docs(bridge-000021): accept D-023 research scope for Bottom placement`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/20 — открыт в `source`, не смержен; ветка `task/BRIDGE-000021-bottom-placement` сохранена.

## 7. История изменений

- 2026-11-26: исходная постановка была остановлена как конфликтующая с D-022; Оператор выбрал D-023 с уточнением research-only scope. Подготовлены согласованные документы, проект контракта калибровки и настоящий отчёт. Production-код не изменялся.
- 2026-11-26: открыт PR #20 в `source`; в отдельном коммите добавлены финальный SHA и URL PR. PR не смержен.
