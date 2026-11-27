---
id: BRIDGE-000044
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-27T17:00:00+03:00
updated: 2026-11-27T17:00:00+03:00
base: source
branch: task/BRIDGE-000043-top-only-export-hardening
commit: pending
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/29
related: [BRIDGE-000043, BRIDGE-000042]
runtime_status: not_authorized
smoke_status: not_executed
production_top_status: blocked/unverified
bottom_status: blocked/deferred
---

# BRIDGE-000044 — Export Pipeline Reservation Boundary Fix

## 1. Preflight

| Проверка | Результат |
|---|---|
| Branch | `task/BRIDGE-000043-top-only-export-hardening`, PR #29 не смержен |
| Pre-change HEAD | `ed0ccba32c10ba8337331bacb8634c1718ef623d` |
| `origin/source` | `5da53446866f12f227953a03321c7af48489920c`, без изменений |
| PR #29 head | совпадает с локальным HEAD, `state=open`, `base=source`, `commits=3` |
| Working tree | только пять прежних untracked reports; не удалялись и не перезаписывались |
| `source` | не изменялся; новый commit направлен только в ветку PR #29 |
| Runtime | Altium/SOLIDWORKS не запускались, PCB не открывалась, DLL и registration не трогались |

---

## 2. Root cause

В BRIDGE-000043 резервирование request ID происходило после:

1. `AltiumBoardSnapshotAdapter.Read(view)`;
2. `AltiumComponentModelReader.Read(...)`;
3. `AltiumBoardModelExporter.Export(...)`;
4. `PackageRootResolver.Resolve()`.

Координатор вызывался только перед `ExchangePackageWriter.Write`. Поэтому повторный command dispatch
или callback с тем же request ID успевал выполнить чтение PCB, model extraction и STEP export до того,
как получал `DuplicateRejected`. Фактически at-most-once защищала только запись package, а не весь
export pipeline.

Полный путь: `Execute` → `Export` → `AltiumDocumentStateReader.TryRead` → snapshot adapter →
component model reader / board model exporter → `ExportRequestCoordinator.Execute` → package writer.
Минимальная корректная граница — сразу после вычисления request ID и до `TryRead`, snapshot reader,
model reader, board exporter и writer.

---

## 3. Изменённые файлы

| Файл | Изменение |
|---|---|
| `src/EcadMcadBridge.Altium/Application/ExportRequestCoordinator.cs` | единый operation delegate; request и invocation резервируются до вызова операции; IDs и outcome сохраняются |
| `src/EcadMcadBridge.Altium/Application/ExportRequestPipeline.cs` | новый whole-operation pipeline с injected boundaries для gate, root, workspace, snapshot, model, board и package writer |
| `src/EcadMcadBridge.Altium/Application/ExportBoardSnapshotHandler.cs` | composition root: вычисляет request ID, передаёт реальные адаптеры в pipeline, сохраняет fail-closed проверки и диагностику |
| `tests/EcadMcadBridge.Altium.Tests/ExportRequestCoordinatorTests.cs` | адаптированы под reservation-before-operation контракт |
| `tests/EcadMcadBridge.Altium.Tests/ExportRequestPipelineTests.cs` | 12 invocation-count тестов на границы export pipeline |
| `.agent/reports/BRIDGE-000044-report.md` | настоящий отчёт |

Production Bottom, D-024, SolidWorks backend, Core packaging, `.ins`/`.rcs`, SDK ledger, registry,
установленная DLL и любые PCB/STEP/SLDPRT/SLDASM/DLL бинарники не изменялись.

---

## 4. Reservation boundary: до и после

### До BRIDGE-000044

```text
command dispatch
  → document state read
  → snapshot reader            [side effect]
  → component model reader     [STEP export]
  → board model exporter       [STEP export]
  → package root resolve
  → coordinator duplicate check
  → package writer
```

### После BRIDGE-000044

```text
command dispatch
  → resolve request ID (только document.GetFileName для fallback)
  → atomically reserve request + correlation + invocation
  → EXPORT_REQUEST_START
  → ownership/document gate
  → package root + workspace
  → snapshot reader             [только после успешной резервации]
  → component model reader      [только после успешной резервации]
  → board model exporter        [только после успешной резервации]
  → package writer
  → EXPORT_REQUEST_END
```

Повтор того же request ID возвращается из coordinator до вызова `ExportRequestPipeline.Run`, поэтому
gate, workspace, snapshot, model, board и writer не вызываются. Единственная операция до резервации —
`GetFileName()` для детерминированного fallback request ID; это не `AltiumDocumentStateReader.TryRead`,
не snapshot и не model extraction. Явный GUID в `parameters` позволяет резервировать без чтения документа.

---

## 5. Invocation-count test matrix

`ExportRequestPipelineTests` считает вызовы injected delegates, а не только состояния или логи.

| Сценарий | Gate | Root/Workspace | Snapshot | Model | Board | Writer | Outcome |
|---|---:|---:|---:|---:|---:|---:|---|
| Один request | 1 | 1/1 | 1 | 1 | 1 | 1 | `Completed` |
| Повтор того же ID | +0 | +0/+0 | +0 | +0 | +0 | +0 | `DuplicateRejected` |
| Два разных ID | 2 | 2/2 | 2 | 2 | 2 | 2 | `Completed` ×2 |
| Два параллельных одинаковых ID | 1 | 1/1 | 1 | 1 | 1 | 1 | `Completed` + `DuplicateRejected` |
| Exception snapshot reader | 1 | 1/1 | 1 | 0 | 0 | 0 | `Failed` |
| Exception model reader | 1 | 1/1 | 1 | 1 | 0 | 0 | `Failed` |
| Exception board exporter | 1 | 1/1 | 1 | 1 | 1 | 0 | `Failed` |
| Exception package writer | 1 | 1/1 | 1 | 1 | 1 | 1 | `Failed` |
| Uncertain outcome + retry | 1 | 1/1 | 1 | 1 | 1 | 1 | `Uncertain` + `DuplicateRejected` |
| Ownership gate failure | 1 | 0/0 | 0 | 0 | 0 | 0 | `GateRejected` |
| Повторный callback | 1 | 1/1 | 1 | 1 | 1 | 1 | `Completed` + duplicate ×2 |
| Bottom request | 1 | 0/0 | 0 | 0 | 0 | 0 | `GateRejected` (`SW-PLC-007`) |

Ключевые ID и invocation captured в writer и сравнены с `EXPORT_REQUEST_START/END`. Состояния
`Failed`/`Uncertain`/`GateRejected` остаются terminal: автоматического освобождения request ID нет.

---

## 6. Гарантия и сохранённые ограничения

Гарантируется только **at-most-once dispatch** в рамках процесса Altium Add-in. Не заявляется
exactly-once execution: package writer может завершить запись до неопределённой верификации; такой
исход фиксируется как `Uncertain`, cleanup запрашивается, повтор того же ID запрещён.

Сохранено:

- Top-only filtering и фильтрация BodyProjection;
- ownership/path/modified validation и повторная проверка `TryRecheck` перед writer;
- fail-closed при неполной inventory, отсутствии package root, ошибках writer и верификации;
- `documentSha256`, schema/capabilities, ModelOrigin и Top transform conventions;
- D-024 как contract only, production Bottom rejection (`SW-PLC-006/007`);
- correlation ID и invocation ID на всей цепочке.

---

## 7. Validation

| Проверка | Результат |
|---|---|
| `dotnet build -c Release` | passed, 0 warnings, 0 errors |
| `dotnet test -c Release --no-build` | passed: Altium `215`, Core `229`, SolidWorks `94`, всего `538` |
| `git diff --check` | no whitespace errors |
| Banned API scan | no matches in changed code (`DelphiScript`, Legacy API, `dynamic`, reflection, `Activator`, `DispId`, COM internals) |
| Binary scan | только `.cs`/`.md`; PCB/STEP/SLDPRT/SLDASM/DLL не добавлялись |
| Production Bottom / D-024 | не изменялись; существующие SOLIDWORKS Bottom tests проходят |
| Duplicate rejection до side effects | доказано invocation-count matrix: все границы второго вызова = +0 |
| CAD runtime / smoke | not executed; `runtime_status: not_authorized` |

---

## 8. Safety audit и риски

1. Не выполнялись Altium/SOLIDWORKS runtime, smoke, открытие PCB, установка DLL и registry changes.
2. PR #29 остаётся открытым; base `source`; merge не выполнялся.
3. Fallback request ID по-прежнему детерминирован для одного PCB в одном процессе. Явный GUID нужен для
   намеренного второго package без перезапуска Add-in; UI/host для этого не добавлялся.
4. Dispatcher, вызвавший второй export в BRIDGE-000042, остаётся неидентифицированным. Новый pipeline
   блокирует его до любых export side effects, но не устраняет первопричину. UI-параметр остаётся пустым.
5. `source` и пять прежних untracked reports не тронуты.

---

## 9. PR и SHA

- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/29
- Content commit: `702725035195f8341d125ef2b82f7c0548acec6c`
- Implementation head: см. актуальный head PR #29 после push

## 10. История изменений

1. Preflight: branch/HEAD/PR head и untracked baseline зафиксированы.
2. Reservation перенесён в Coordinator до operation delegate; добавлен `ExportRequestPipeline`.
3. Добавлены 12 invocation-count сценариев; build/tests/static checks passed.
4. PR #29 обновлён новым commit; merge и runtime не выполнялись.
