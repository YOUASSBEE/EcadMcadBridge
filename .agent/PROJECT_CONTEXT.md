# PROJECT_CONTEXT — ECAD-MCAD Bridge

| Поле | Значение |
|---|---|
| Проект | ECAD-MCAD Bridge |
| Репозиторий | `https://github.com/YOUASSBEE/EcadMcadBridge` |
| Базовая ветка | `source` |
| Текущая ветка | `agent/BRIDGE-000015-solidworks-step-import` |
| Статус | `BRIDGE-000015-R1`: SOLIDWORKS STEP import backend и native SLDPRT smoke с защитой жизненного цикла документов |
| Дата фиксации | 2026-11-25 |
| Владелец продукта | Оператор проекта (человек) |

## 1. Назначение

ECAD-MCAD Bridge — это интеграционный слой между электронным (ECAD) и механическим (MCAD)
проектированием печатных плат. Проект состоит из двух CAD Add-in и общей библиотеки Core:

- Add-in для Altium Designer 26 — сторона ECAD;
- Add-in для SOLIDWORKS 2026 — сторона MCAD;
- `EcadMcadBridge.Core` — общая библиотека, не зависящая от CAD SDK.

Цель — передавать между CAD-системами согласованный, версионируемый и проверяемый набор данных
о печатной плате и её механическом окружении, не смешивая API двух CAD-систем в одном компоненте.

## 2. Продуктовые цели

1. Исключить ручной перенос геометрии и посадочных мест между Altium Designer и SOLIDWORKS.
2. Сделать обмен воспроизводимым: одинаковый вход должен давать одинаковый выход.
3. Обеспечить прослеживаемость: каждая передача данных имеет источник, версию схемы и контрольные суммы.
4. Оставить возможность развития в обе стороны (ECAD → MCAD и MCAD → ECAD) без переделки архитектуры.
5. Дать локальному агенту и человеку единый, проверяемый через Git интерфейс постановки задач и приёмки результата.

## 3. Границы текущего этапа

Входит в scope:

- структура репозитория и постоянная техническая документация;
- фиксация стека, архитектуры и архитектурных решений;
- реестр подтверждённых и неподтверждённых API;
- Git-протокол взаимодействия с локальным агентом (задачи, отчёты, ревью, код);
- описание файлового runtime-обмена между CAD-системами вне Git;
- техническое задание `BRIDGE-000001` на исследование и подготовку каркаса Altium Add-in;
- каркас Altium Add-in (`Core`, `Altium`, `Core.Tests`) и одна диагностическая команда;
- минимальный smoke-снимок активной `.PcbDoc` через подтверждённые `PCB.IPCB_*` API;
- нейтральная модель платы `BoardSnapshot`, геометрия, электрические ссылки, JSON-контракт
  и CAD-независимый валидатор в Core;
- read-only Altium adapter для слоёв, компонентов, падов/PadStacks, nets и контура платы,
  команда `Export Board Snapshot` и smoke-проверка на vendor-платах AD26;
- Core Exchange Package: deterministic manifest, snapshot, checksums, atomic writer,
  validating reader и безопасные относительные пути (`BRIDGE-000004`);
- опциональный каталог 3D-моделей компонентов `models/models.json` и
  content-addressed STEP/X_T assets в `assets/3d/` (`BRIDGE-000013`);
- SOLIDWORKS STEP import backend: чтение Exchange Package, per-asset STEP AP214,
  native `.SLDPRT`, content-addressed dedup и assembly API smoke
  (`BRIDGE-000015`).

Не входит в scope текущего этапа:

- полноценный SOLIDWORKS Add-in, регистрация `ISwAddin` и UI;
- реальное размещение ECAD-компонентов в SOLIDWORKS (`T_B_S`, Bottom, Euler);
- ECO, обратная аннотация, синхронизация изменений;
- установщик, CI/CD, подпись сборок, публикация расширения;
- изменение архитектуры без отдельного согласования.

## 4. Согласованный стек

| Компонент | Версия / значение | Статус |
|---|---|---|
| Altium Designer | 26 | Зафиксировано |
| Altium SDK | 4.0.1 | Зафиксировано |
| .NET SDK | 10 | Зафиксировано |
| Target Framework (Altium) | `net10.0-windows` | Зафиксировано, совместимость подтверждена оператором |
| Target Framework (Core) | `net10.0` | Зафиксировано (D-014) |
| Target Framework (Core.Tests) | `net10.0` | Зафиксировано (D-014) |
| Target Framework (SOLIDWORKS) | `net10.0-windows`, x64 | Зафиксировано (D-020) |
| SOLIDWORKS interop | `SolidWorks.Interop.sldworks` / `swconst` 34.3.2.3, локальный reference | Зафиксировано (D-020) |
| Язык | C# | Зафиксировано |
| SOLIDWORKS | 2026 SP3.2, 34.3.2.0003 | Зафиксировано |
| Core UI | не зафиксировано | Открытый вопрос Q-005 |

Ограничения стека:

- DelphiScript не используется.
- Legacy API Altium не используется.
- Неподтверждённые API не попадают в категорию подтверждённых в `SDK_API_LEDGER.md`.
- Совместимость AD26 + SDK 4.0.1 + .NET SDK 10 + `net10.0-windows` подтверждена оператором;
  целевой TFM Altium Add-in — `net10.0-windows`.
- Все линейные величины из Altium IU сохраняются парой IU/мм (D-015); потеря исходных IU недопустима.
- Источник InteractiveBom по заданию `BRIDGE-BOOTSTRAP-004` — архив `InteractiveBom.zip` с полным исходным проектом: `InteractiveBom.sln`
  с проектами shim, Impl, Panels и Tests; `Directory.Build.props` выбирает TFM по
  `X2.runtimeconfig.json` (AD 26.1 → `net8.0`, AD 26.12 → `net10.0`); указанная Release-сборка —
  `bin/Release/net10.0-windows`. Отдельная бинарная поставка `net8.0-windows` — другой источник.
- Загрузка идёт через `.Ins`/`.rcs` и COM-фабрику с assembly-level `ComVisible(true)`,
  `AutoDispatch` и `IPluginFactory`.

## 5. Структура репозитория

```text
/
├── AGENTS.md
├── README.md
├── EcadMcadBridge.sln
├── Directory.Build.props
├── global.json
├── src/
│   ├── EcadMcadBridge.Core/        Model, Serialization, Validation, Diagnostics; net10.0
│   ├── EcadMcadBridge.Altium/      bootstrap, adapter, application, installation; net10.0-windows
│   └── EcadMcadBridge.SolidWorks/  STEP import, native SLDPRT, assembly smoke; net10.0-windows x64
├── tools/
│   └── EcadMcadBridge.SolidWorks.Smoke/  STA ROT smoke harness
├── tests/
│   ├── EcadMcadBridge.Core.Tests/
│   ├── EcadMcadBridge.Altium.Tests/
│   └── EcadMcadBridge.SolidWorks.Tests/
└── .agent/
    ├── PROJECT_CONTEXT.md
    ├── ARCHITECTURE.md
    ├── DEVELOPMENT_RULES.md
    ├── SDK_API_LEDGER.md
    ├── DECISIONS.md
    ├── tasks/
    │   └── BRIDGE-000001.md
    ├── reports/
    └── reviews/
```

Пользовательские PCB, STEP и рабочие сборки в дерево не добавляются.

## 6. Источники истины

| Документ | Что определяет | Кто меняет |
|---|---|---|
| `AGENTS.md` | Правила работы агентов в репозитории | Оператор, по согласованию |
| `README.md` | Входная точка для человека | Оператор / агент по задаче |
| `.agent/PROJECT_CONTEXT.md` | Границы, стек, статус, словарь | Оператор / агент по задаче |
| `.agent/ARCHITECTURE.md` | Компоненты, потоки данных, формат обмена, Git-протокол, runtime-обмен | Только через `DECISIONS.md` |
| `.agent/DEVELOPMENT_RULES.md` | Инженерные и Git-правила | Оператор / агент по задаче |
| `.agent/SDK_API_LEDGER.md` | Доказанная база API | Агент, с обязательным доказательством |
| `.agent/DECISIONS.md` | Принятые и открытые архитектурные решения | Оператор |
| `.agent/tasks/*.md` | Постановки задач | Оператор |
| `.agent/reports/*.md` | Отчёты о выполнении | Исполнитель |
| `.agent/reviews/*.md` | Результаты ревью | Ревьюер |

При конфликте документов приоритет: `DECISIONS.md` → `ARCHITECTURE.md` → `DEVELOPMENT_RULES.md`
→ остальные документы. Конфликт фиксируется в отчёте и выносится на согласование.

## 7. Роли

| Роль | Ответственность |
|---|---|
| Оператор | Ставит задачи, принимает архитектурные решения, мержит PR в `source` |
| Локальный агент | Выполняет задачу в отдельной ветке, готовит отчёт и PR |
| Ревьюер | Проверяет результат по критериям приёмки, пишет review-файл |
| Владелец CAD-стенда | Проводит smoke-тест в Altium/SOLIDWORKS, подтверждает поведение в реальном хосте |

Один человек может совмещать роли, кроме случая, когда он же является автором изменения:
автор не утверждает собственное архитектурное решение.

## 8. Текущий статус

- `source` содержит bootstrap-документацию; код добавляется ветками `task/BRIDGE-000001`
  и `agent/BRIDGE-000002-core-model` через PR.
- Созданы `EcadMcadBridge.Core`, `EcadMcadBridge.Altium` и `EcadMcadBridge.Core.Tests`;
  TFM — `net10.0`, `net10.0-windows` и `net10.0` соответственно (D-014).
- Add-in загружается в AD26 26.12.2.9; команда `Export Smoke Snapshot` видна в PCB → Tools.
  Минимальная WinForms-панель `ECAD-MCAD Bridge` используется только для запуска сервера.
- Smoke-тест на синтетической плате: `componentCount=28` (совпадает с Board Information),
  `outlinePointCount=42`, `warnings=0`; `.PcbDoc` не изменён (SHA256 совпадает).
- В Core реализованы `BoardSnapshot` и сущности платы, слоёв, компонентов, падов, цепей,
  проводников, переходных отверстий, регионов и текста; дуги хранятся аналитически.
- `BoardSnapshotJson` обеспечивает UTF-8 без BOM, LF, фиксированный порядок и round-trip;
  `BoardSnapshotValidator` возвращает структурированные ошибки и предупреждения.
- `AltiumBoardSnapshotAdapter` извлекает слои, компоненты, пады/PadStacks, nets и контур;
  команда `Export Board Snapshot` пишет валидированный JSON в `%TEMP%`.
- Smoke-тест `MiniPC.PcbDoc`: 31 слой, 1046 компонентов, 6004 пада, 837 сетей, 9669 форм,
  rotation до 135°; `Bluetooth_Sentinel.PcbDoc`: контур с 2 аналитическими дугами.
  SHA256 обеих vendor-плат до/после экспорта совпал.
- `BRIDGE-000003-FIX-001`: Board Outline сохраняет все контуры `GeometricPolygon`;
  несопоставленные аналитические циклы дают line-fallback с warning, а не удаление.
  `TShape` разделён на exact/simplified/unsupported; добавлены `Altium.Tests` (19 тестов).
- `BRIDGE-000003-FIX-002`: exact matcher отвергает частичное совпадение (Bluetooth
  переведён с ложного exact на fallback с warning); исходный файл оператора
  `minipc.PcbDoc` оказался byte-identical vendor-копией без вырезов, поэтому V-22
  остаётся открытым.
- `BRIDGE-000003-FIX-003`: matcher учитывает угловой диапазон дуг и середины
  polyline-сегментов; новая плата оператора снова дала 1 outer-contour/0 holes.
- `BRIDGE-000003-FIX-004`: реальный Board Cutout извлечён из `IPCB_Region`
  (`eRegionKind_BoardCutout`) и `ClipWithPolyRegShapes(false,false)`. На
  `board with cutout.PcbDoc` получены 2 hole-контура (7 линий; 6 линий + 3 дуги),
  все вершины внутри outer; `ClipWithPolyRegShapes(false,false)` не включает pad/via
  holes. `MiniPC.PcbDoc` содержит BoardCutout-регион, но клиппер вернул 0 вырезов,
  поэтому регион не экспортируется. Matcher дополнительно проверяет середины
  дуг/линий самого loop.
- `BRIDGE-000003-FIX-005`: исправлена дедупликация (сравниваются реальные точки
  geometric Hole, а не clip loop с самим собой), добавлено строгое Region matching
  с `Matched`/`Unmatched`/`Ambiguous`, association tracking по паре
  `(RegionIndex, ContourIndex)` и исправлена трактовка дуг CCW при переходе через 0°.
  На операторской плате сохранены оба Hole; на MiniPC/Bluetooth ложных cutout нет.
  Altium.Tests: 43; V-22 остаётся закрытым.
- `BRIDGE-000003-FIX-006`: bounds аналитических дуг вычисляются по Start/End и
  кардинальным экстремумам внутри sweep; broad phase больше не отбрасывает Region
  (geometry-first). Дедупликация дуговых Hole использует отдельный approximation
  tolerance и двустороннюю проверку. На операторской плате сохранены оба Hole;
  Altium.Tests: 60; V-22 закрыт.
- `BRIDGE-000003-FIX-007`: approximation-dedup сравнивает centroid характерных
  точек (вершины Hole, спроецированные на clip loop), а не raw centroid полигона.
  Исправлен ложный отказ при большой дуге и разной плотности полилинии; перенос
  независимого контура по-прежнему отклоняется. Altium.Tests: 65.
- `BRIDGE-000004`: в Core реализован Exchange Package — deterministic manifest 1.0.0,
  snapshot/checksums, atomic writer, validating reader и защита относительных путей.
  `BRIDGE-000004-FIX-001`: reader рекурсивно проверяет nested files, не обходит
  symlink/reparse points и диагностирует их как `EMB-PKG-021`.
  `BRIDGE-000007`: `ExportBoardSnapshot` пишет Exchange Package через Core Writer,
  проверяет `GetFileName/GetModified`, вычисляет SHA-256 исходного `.PcbDoc` и
  отклоняет несохранённые документы; package root задаётся env/`LOCALAPPDATA`.
  `BRIDGE-000009`: adapter заполняет Tracks, Vias, Regions и Texts; region geometry
  использует geometric polygon line fallback с явным warning (Q-010 частично открыт).
  Core 1.0.0 не содержит pad holes, pin designator, локальных координат падов и stack order;
  эти ограничения зафиксированы в ledger V-18…V-25.
- `BRIDGE-000013`: Exchange Package 1.0.0 получил опциональный
  `models/models.json` (`ecad-mcad-bridge/component-models` 1.0.0) и
  content-addressed `assets/3d/<sha256>.step|.x_t`; capability
  `component-3d-models`; raw IU/mm metadata, потоковое копирование/хеширование
  assets и полное покрытие manifest/checksums (D-018).
- `BRIDGE-000015-R1`: добавлен отдельный `EcadMcadBridge.SolidWorks`
  (`net10.0-windows`, x64): Core Reader → unique STEP plan → typed SOLIDWORKS
  import → native `.SLDPRT`; assembly API smoke с `AddComponent5`. Smoke harness
  подключается через ROT, работает в STA и не регистрирует Add-in. R1 добавил
  baseline `GetDocuments()`, защиту пользовательских/open документов, no-overwrite
  guard, временное сохранение assembly→part с восстановлением настройки и decode
  warning-флагов. Реальное размещение ECAD-компонентов не реализовано (D-020).
- API-реестр дополнен подтверждениями хоста (C-66…C-72); закрыты V-02/V-03/V-04.
  `BRIDGE-000002` новых Altium API не добавляет.
- Git-протокол и runtime-обмен разделены: Git не используется для пользовательских PCB, STEP
  и рабочих сборок.
- Полноценный SOLIDWORKS Add-in, UI и placement остаются отдельной задачей.

## 9. Ключевые открытые вопросы

| ID | Вопрос | Статус | Почему важно |
|---|---|---|---|
| Q-001 | Загружает ли AD26 in-process сборку `net10.0-windows`? | **Закрыт:** подтверждено оператором (C-65) | TFM InteractiveBom выбирается по версии Altium; TFM Bridge зафиксирован как `net10.0-windows` |
| Q-002 | Какая точная сборка Altium Designer 26 используется на стенде? | **Уточнён:** `X2.EXE` 26.12.2.9, runtime `net10.0` / 10.0.10 | Изоляция конкретного варианта `Altium.SDK.dll` внутри хоста остаётся открытой (V-13) |
| Q-003 | Как извлекать 3D-модели компонентов для MCAD-стороны? | Открыт; контракт хранения готов (`BRIDGE-000013`) | Нужно для будущей передачи в SOLIDWORKS, API не проверен; извлечение вынесено в `BRIDGE-000014` |
| Q-004 | Какой interop-пакет и API использовать для SOLIDWORKS 2026? | **Закрыт для prototype:** local `SolidWorks.Interop.*` 34.3.2.3, ROT, STA; Add-in registration отдельно | STEP/native/assembly/lifecycle API подтверждены smoke `BRIDGE-000015-R1` (S-001…S-015); D-020 |
| Q-005 | Какой UI-стек использовать в Add-in (WPF/WinForms)? | Частично закрыт | Для smoke-команды — хост-диалог, для запуска сервера — минимальная WinForms-панель; полноценные панели ещё не проектировались |
| Q-006 | Как автоматизировать установку/регистрацию расширения? | Открыт | Известен ручной путь через `ExtensionsRegistry.xml`; автоматизация требует отдельного решения |
| Q-007 | Можно ли распространять SDK-сборки Altium в составе репозитория? | Открыт | Лицензионные ограничения; вероятно, ссылаться на локальную установку |
| Q-008 | Достаточно ли `SourceUniqueId` для стабильной идентификации компонентов? | Открыт | Нужно для сопоставления ECAD/MCAD без ложных совпадений |
| Q-009 | Какой Target Framework у SOLIDWORKS Add-in? | **Закрыт для backend:** `net10.0-windows`, x64 (D-020); Add-in host отдельно | Backend и smoke собраны и запущены на .NET SDK 10 |
| Q-010 | Полно ли `BoardSnapshot` покрывает данные, которые Altium отдаёт через подтверждённые `IPCB_*`? | Частично закрыт в `BRIDGE-000009` | Tracks/vias/texts/regions извлекаются; region analytic arcs пока line-fallback, text font outline и часть region semantics не представлены в Core 1.0.0 |
| Q-011 | Как проверять пространственную вложенность отверстий и контуров? | Открыт | Валидатор проверяет роли, замкнутость и непрерывность, но не point-in-polygon |
| Q-012 | Как расширить Core для отверстий падов, pin designator, локальных координат и stack order? | Открыт | Требует отдельного решения до изменения публичного контракта |
| Q-013 | Как различать signal и internal plane в `LayerKind`? | Открыт | Сейчас оба представлены как `Copper`; Plane-тип отсутствует |
| Q-014 | Как подтвердить внутренние вырезы контура на реальной плате? | **Закрыт:** `BRIDGE-000003-FIX-004` | `IPCB_Region` + `ClipWithPolyRegShapes(false,false)` дали 2 выреза на `board with cutout.PcbDoc`; pad/via holes исключены и проверены на `MiniPC.PcbDoc` |

Полные формулировки и способы проверки — в `SDK_API_LEDGER.md` и `DECISIONS.md`.

## 10. Словарь

| Термин | Значение |
|---|---|
| ECAD | Электронный CAD; в проекте — Altium Designer 26 |
| MCAD | Механический CAD; в проекте — SOLIDWORKS 2026 |
| Add-in | Нагрузочный модуль CAD-системы, работающий в её процессе |
| Core | Общая библиотека домена и формата обмена без ссылок на CAD SDK |
| Snapshot | Снимок состояния платы в нейтральной модели Core |
| Package | Каталог/архив с `manifest.json`, snapshot-файлами и контрольными суммами |
| Model catalog | Опциональный `models/models.json` с метаданными 3D-тел и ссылками на assets |
| Content-addressed asset | Файл STEP/X_T с именем `<lowercase-sha256>.<ext>` в `assets/3d/` |
| IU | Internal Units — целочисленные единицы Altium; исходное представление линейных величин без потери точности, 1 IU = 0.00000254 mm |
| Runtime exchange | Передача Exchange Package и рабочих файлов между CAD-системами через локальный каталог вне Git |
| Local Agent | Локальный процесс на CAD-стенде (человек или автоматизация), обменивающийся задачами через Git |
| Task | Файл `.agent/tasks/BRIDGE-NNNNNN.md` с постановкой |
| Report | Файл `.agent/reports/BRIDGE-NNNNNN-report.md` с результатом |
| Review | Файл `.agent/reviews/BRIDGE-NNNNNN-review.md` с вердиктом |
| Confirmed API | Сигнатура, подтверждённая исходником SDK, метаданными или успешным вызовом в хосте |
| Unverified API | API, которое нельзя использовать как доказанное без дополнительной проверки |
