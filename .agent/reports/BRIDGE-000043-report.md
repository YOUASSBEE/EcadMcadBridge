---
id: BRIDGE-000043
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-27T16:00:00+03:00
updated: 2026-11-27T16:00:00+03:00
base: source
branch: task/BRIDGE-000043-top-only-export-hardening
commit: pending
pr: pending
related: [BRIDGE-000042, BRIDGE-000041, BRIDGE-000040, BRIDGE-000035]
runtime_status: not_authorized
smoke_status: not_executed
production_top_status: blocked/unverified
bottom_status: blocked/deferred
---

# BRIDGE-000043 — Top-only export at-most-once hardening

## 1. Итог

Выполнен implementation milestone, заявленный после остановки BRIDGE-000042. Введён request-scoped
координатор экспорта: один request ID может вызвать export boundary не более одного раза; повтор того же
ID отклоняется, разные ID обрабатываются независимо. Добавлены request/correlation/invocation ID,
журналы `EXPORT_REQUEST_START`, `EXPORT_REQUEST_END`, `EXPORT_DUPLICATE_REJECTED`, а также ID и outcome
в `PACKAGE_EXPORT_OK`.

Добавлены 9 юнит-тестов, которые считают фактические вызовы export boundary, а не только записи лога.
Полная сборка и все тесты проходят: `0` ошибок, `0` предупреждений, `527` тестов (`204` Altium,
`229` Core, `94` SolidWorks).

**Не выполнялось:** SOLIDWORKS runtime, placement, repeated BRIDGE-000042, CAD smoke, установка/замена DLL,
 registry и какие-либо изменения пользовательских PCB. `runtime_status` остаётся `not_authorized`,
 smoke — `not_executed`. Production Top-only остаётся `blocked/unverified`, Bottom — `blocked/deferred`,
 V-16 — `unverified`.

---

## 2. Изменённые файлы

| Файл | Изменение |
|---|---|
| `src/EcadMcadBridge.Altium/Application/ExportRequestCoordinator.cs` | новый внутренний координатор: request-scoped состояние, at-most-once dispatch, duplicate rejection, диагностика |
| `src/EcadMcadBridge.Altium/Application/ExportBoardSnapshotHandler.cs` | вызов export boundary через координатор; request ID из параметров или SHA-256 пути PCB; correlation/invocation ID в логах и UI-сообщении |
| `tests/EcadMcadBridge.Altium.Tests/ExportRequestCoordinatorTests.cs` | 9 тестов границы экспорта: одиночный вызов, повтор ID, разные ID, конкуренция, gate failure, exception до/во время export, uncertain outcome, resolver |
| `.agent/reports/BRIDGE-000043-report.md` | настоящий отчёт |

Production code SOLIDWORKS, Core, адаптеры, `.ins`/`.rcs`, D-024, `DECISIONS.md`, SDK ledger, installer,
 registry, DLL, PCB/STEP/SLDPRT/SLDASM и любые бинарные артефакты в Git не изменялись.

---

## 3. Stage A — root cause по BRIDGE-000042

Путь команды: `.rcs` menu `Tools|Export Board Snapshot` с `Params=''` →
`BridgeServerModule.InitializeCommands()` → `EcadMcadBridge:ExportBoardSnapshot` →
`ExportBoardSnapshotHandler.Execute` → `Export` → `ExchangePackageWriter.Write`.

В логе `%TEMP%\EcadMcadBridge\logs\bridge.log` зафиксированы два полных цикла:

```text
2026-11-27T11:53:30Z INFO SOURCE_STATE doc=multi-body-r90.PcbDoc kind=PCB modified=false pathRooted=true
2026-11-27T11:55:48Z INFO PACKAGE_EXPORT_OK ... packageId=b6dd8d27-486b-4308-ad20-d9414f02485c
2026-11-27T11:56:15Z INFO SOURCE_STATE doc=multi-body-r90.PcbDoc kind=PCB modified=false pathRooted=true
2026-11-27T11:58:31Z INFO PACKAGE_EXPORT_OK ... packageId=8738d764-c3c3-49b4-98c6-399b7730664e
```

Каждый цикл имел собственный `DURATION_MS` (`138137` и `136548`) и собственный `Guid.NewGuid()`
package ID. Это исключает версию «один export, дублированная запись в лог» и указывает на **два
независимых полных вызова `Execute`**. В исходном коде не найдено внутреннего retry, callback, timer
или повторного вызова writer после успешной записи. Старая команда не имела request/correlation ID:
`Params=''` и `Guid.NewGuid()` создавался внутри каждого `Export`.

Точная причина второго menu dispatch из лога не восстанавливается; она не объявляется ни безвредным
повтором, ни подтверждённым внутренним retry. Это и есть остаточный вопрос Stage A. Данный milestone
устраняет последствие на границе экспорта, не маскируя причину dispatch.

---

## 4. Stages B–D — минимальный дизайн и реализация

### 4.1 Решение

`ExportRequestCoordinator` хранит состояние request ID в процессе Altium Add-in. Состояния:
`InProgress`, `Completed`, `Failed`, `Uncertain`, `GateRejected`, `DuplicateRejected`. Первое обращение
резервирует request ID до вызова gate; последующие обращения с тем же ID получают
`DuplicateRejected` и **не вызывают** gate и export boundary. Terminal-состояния не переопределяются.

`requestId`:

- явный `Guid` из `parameters`, если он передан;
- иначе `doc-` + lowercase SHA-256 абсолютного пути PCB.

`correlationId` создаётся один раз на request, `invocationId` — один раз на фактический вызов export.
Оба идентификатора являются GUID и попадают в `EXPORT_REQUEST_START`, `EXPORT_REQUEST_END`,
`EXPORT_DUPLICATE_REJECTED`, `PACKAGE_EXPORT_OK` и UI-сообщение.

### 4.2 Гарантия

Гарантия: **at-most-once dispatch**. Не заявляется exactly-once внешний эффект: если writer
записал package, но Core Reader не подтвердил ожидаемый каталог, результат — `Uncertain`, cleanup
запрошен, повтор того же ID запрещён. Это сохраняет fail-closed поведение BRIDGE-000042.

### 4.3 Сохранённые ограничения

- Top-only фильтрация, ownership/document validation, `GetModified()` gate и повторная проверка
  источника перед записью не изменены по смыслу; последние два Checks перенесены в gate координатора.
- `documentSha256`, schema/capabilities, board model и component model pipeline не изменены.
- Bottom и projection mismatch по-прежнему отклоняются в SOLIDWORKS (`SW-PLC-006`, `SW-PLC-007`);
  D-024 catalog `1.2.0` не реализован.
- Library identity, `ModelOrigin` и Top transform conventions не тронуты.
- Новые SDK API, Legacy API, DelphiScript, reflection, `dynamic`, DispId не использовались.

---

## 5. Stage E — тесты

`ExportRequestCoordinatorTests` проверяют:

| Тест | Проверка |
|---|---|
| `SingleRequest_InvokesExportBoundaryOnceAndLogsAllIdentifiers` | 1 gate, 1 фактический export, IDs и outcome в start/end |
| `SameRequestIdRetry_IsRejectedWithoutSecondExport` | повтор не вызывает gate/export, `EXPORT_DUPLICATE_REJECTED`, correlation сохранён |
| `DifferentRequestIds_AreProcessedIndependently` | 2 export, разные correlation/invocation IDs |
| `ConcurrentSameRequestId_HasExactlyOneExportBoundaryCall` | параллельный повтор во время `InProgress` — ровно 1 export |
| `GateRejection_DoesNotCallExportBoundaryAndRemainsTerminal` | ownership/document gate fail-closed |
| `ExceptionBeforeExport_FailsClosedWithoutExportBoundaryCall` | исключение gate → `Failed`, 0 export |
| `ExceptionDuringExport_FailsClosedAndRejectsRetry` | исключение writer → `Failed`, повтор запрещён |
| `UncertainExportOutcome_IsTerminalAndRejectsRetry` | неоднозначный результат → terminal, повтор запрещён |
| `ResolveRequestId_UsesExplicitIdOrStableDocumentFallback` | GUID из параметров и стабильный `doc-<sha256>` fallback |

Счётчик вызовов находится в delegates, переданных в `Execute`, поэтому тесты доказывают именно число
пересечений export boundary, а не только число строк лога. Existing SOLIDWORKS Tests продолжают
покрывать Bottom rejection; новые тесты не ослабляют их.

---

## 6. Stage F — подготовка runtime-сценария (без выполнения)

Сценарий подготовлен, но **не запущен**:

1. Использовать control fixture `%TEMP%\EcadMcadBridge\BRIDGE-000042\fixture\multi-body-r90.PcbDoc`
   SHA-256 `2E14287D145B5130DF7595E18C96D9DA1F1292BDB3D1065EA0142CECDF6BA995`; `M501` Top, 2 body,
   `rotation=+90°`.
2. Обеспечить единственный активный PCB, `kind=PCB`, абсолютный путь, `modified=false`, ownership текущей
   сессии; пользовательскую `Dev_board.PcbDoc` не открывать.
3. Выполнить ровно один `ExportBoardSnapshot`; ждать `PACKAGE_EXPORT_OK`, `EXPORT_REQUEST_START`,
   `EXPORT_REQUEST_END` и отсутствие `EXPORT_DUPLICATE_REJECTED`.
4. Зафиксировать `requestId`, `correlationId`, `invocationId`, `packageId`, source SHA, checksums и
   catalog M501.
5. Повтор того же request ID должен завершиться `DuplicateRejected` без нового package и без вызова
   writer; отдельный request ID должен создать отдельный package.
6. Только после подтверждения инварианта одного экспорта в runtime — SOLIDWORKS import, placement,
   `2 component + 1 board occurrence`, `0 Bottom occurrence`, readback transforms и 6 landmark residual
   по неизменённым порогам BRIDGE-000042.

Этот milestone не даёт разрешения на выполнение шагов 3–6; требуется отдельная авторизация оператора.

---

## 7. Stages G — проверки и static validation

| Проверка | Результат |
|---|---|
| `dotnet build -c Release` | passed, 0 warnings, 0 errors |
| `dotnet test -c Release --no-build` | passed: Altium `204`, Core `229`, SolidWorks `94`, всего `527` |
| `git diff --check` | no whitespace errors |
| Scan: `DelphiScript`, Legacy API, `dynamic`, reflection, `Activator`, `DispId`, COM internals | no matches в изменённых production/test файлах |
| Scan: PCB/STEP/SLDPRT/SLDASM/DLL binaries in commit | none |
| SDK ledger | без изменений; новый SDK API не добавлялся |
| Architecture / DECISIONS / D-024 / installer / registry | без изменений |
| Branch / base | `task/BRIDGE-000043-top-only-export-hardening` ← `source` @ `5da53446866f12f227953a03321c7af48489920c` |
| CAD smoke | `not_executed`, `runtime_status: not_authorized` |

Версии среды из BRIDGE-000042: Altium Designer `26.12.2.9`, .NET SDK `10.0.404`, SOLIDWORKS не
запускался. Настоящая работа выполнялась только сборкой/тестами .NET; версия установленной DLL
`7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A` не трогалась.

---

## 8. Принятые решения, риски и открытые вопросы

1. Выбран at-most-once dispatch в Add-in process, а не попытка «угадать» причину двойного menu dispatch.
   Точный source второго dispatch остаётся открытым.
2. Fallback request ID детерминирован по пути PCB. В текущем `.rcs` `Params=''`, поэтому в одном процессе
   для одного и того же PCB разрешён только один экспорт; повторное намеренное создание второго package
   требует явного request ID или перезапуска Add-in. Это осознанный компромисс ради stop condition
   BRIDGE-000042 и требует отдельного UI/host решения для безопасного выбора нового request ID.
3. Состояние координатора живучести процесса; после перезапуска Altium состояние сбрасывается. Для
   межпроцессной гарантии потребовался бы отдельный persistent ledger — вне scope.
4. `Uncertain` не повышается до `Completed`: при сомнительной верификации package повтор запрещён.
5. Никакие acceptance-критерии BRIDGE-000042 не переопределялись; production Top-only и Bottom статусы
   не повышались.
6. smoke-тест не выполнялся, поэтому работоспособность изменённой DLL в живом Altium остаётся
   `unverified`.

---

## 9. Commit и PR

- Commit: `pending`
- PR: `pending`

## 10. История изменений

1. Зафиксирован root cause BRIDGE-000042: два полных `Execute`, отсутствие correlation ID в старом пути.
2. Добавлен `ExportRequestCoordinator`, интеграция в `ExportBoardSnapshotHandler`, 9 тестов.
3. Build/tests/static checks passed; runtime сценарий подготовлен, но не запущен.
