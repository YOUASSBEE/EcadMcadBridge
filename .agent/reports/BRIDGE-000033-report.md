---
id: BRIDGE-000033
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T23:36:30+03:00
updated: 2026-11-26T23:36:30+03:00
base: source
branch: task/BRIDGE-000031-sdk-diagnostic-host
commit: pending
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/27
related: [BRIDGE-000031, BRIDGE-000032, BRIDGE-000033]
---

# BRIDGE-000033 — Host threading contract and command discoverability

## 1. Git baseline and PR metadata

Fresh GitHub metadata was read before any change:

- PR #27: <https://github.com/YOUASSBEE/EcadMcadBridge/pull/27>
- `state=open`, `merged=false`
- base: `source` @ `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`
- head branch: `task/BRIDGE-000031-sdk-diagnostic-host`
- expected head: `b6e178abb600e7cf693533a14d4b50cc41d9b8fe`

Local checks after `git fetch origin --prune`:

- `HEAD` = `b6e178abb600e7cf693533a14d4b50cc41d9b8fe`
- `origin/source` = `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`
- `origin/task/BRIDGE-000031-sdk-diagnostic-host` = `b6e178abb600e7cf693533a14d4b50cc41d9b8fe`
- working tree clean.

Work continued in the existing PR branch. No new PR was created, PR #27 was not merged, and its base was not changed. No `.agent/tasks/BRIDGE-000033*.md` file exists; the work was performed from the Operator text.

## 2. Part A — static threading/apartment research

Pinned SDK root:

`C:\ProgramData\Altium\Altium Designer {B4282C90-18BF-448F-BF1A-9E36AE887190}\Extensions\Altium Developer\SDK4.0\CSharp`

Prefix `SDK` below means that directory. Line numbers are from the pinned files.
Version proof: `SDK\Version.txt:1` — `Altium Designer SDK Version 4.0.1`.

### 2.1 Command dispatch contract — documented

| Signature / evidence | Source | Classification |
|---|---|---|
| `public delegate void CommandProc(IServerDocumentView argContext, ref string argParameters);` | `SDK\Source Code\DXP\DXP.Classes.cs:21` | DOCUMENTED: handler receives the invoking view and a by-ref parameter string |
| `[ClassInterface(ClassInterfaceType.AutoDispatch)] public class CommandLauncher : SupportService, ICommandLauncher` | `SDK\Source Code\DXP\DXP.Classes.cs:427-428` | DOCUMENTED: launcher is COM-dispatch visible |
| `public void LaunchCommand(string argCommandName, ref string argParameters, object argContext)` | `SDK\Source Code\DXP\DXP.Classes.cs:438-442` | DOCUMENTED: public entry point, forwards the context as `IServerDocumentView` |
| `public void RegisterCommand(string argCommandId, CommandProc argCommandProc, GetStateProc argGetStateProc = null)` | `SDK\Source Code\DXP\DXP.Classes.cs:450-456` | DOCUMENTED: registration stores the delegate in a dictionary |
| `protected virtual void LaunchCommandImpl(string argCommandName, ref string argParameters, IServerDocumentView argContext)`; `commandProc(argContext, ref argParameters);` | `SDK\Source Code\DXP\DXP.Classes.cs:476-495`, especially `:482` | DOCUMENTED: the registered delegate is invoked **synchronously** in the launcher call; no thread switch is present in SDK code |
| `[ClassInterface(ClassInterfaceType.AutoDispatch)] public abstract class ServerModule : SupportService, IServerModule, IServerSecurity, IServerOptions` | `SDK\Source Code\DXP\DXP.Classes.cs:1655-1656` | DOCUMENTED: module itself is COM-dispatch visible |

The repository's command registration matches this contract:
`src/EcadMcadBridge.Altium/Bootstrap/BridgeServerModule.cs:21-41` calls `RegisterCommand` for all three handlers, including `ReadSelectedObjectDiagnosticsHandler.Execute`.

### 2.2 Thread/apartment requirements — pinned 4.0.1 search result

A targeted search of the pinned SDK C# sources and examples for
`STAThread`, `MTAThread`, `ApartmentState`, `Thread.CurrentThread`,
`IsThreadPoolThread`, `main thread`, `UI thread`, `thread-safe`,
`worker thread`, `background thread`, `SynchronizeContext`, `SynchronizationContext`
found no per-API requirement for `IServerDocumentView`, `IPCB_ServerInterface`, PCB object model,
selection reads, or iterators. The only `main thread` hits are unrelated supplier-search comments:
`SDK\Source Code\EDP.Supplier.cs:1156` and `:1164` (constants at `:1157-1168`).

Structural COM evidence exists but does not establish an apartment rule:

- dispatch-style interfaces: `SDK\Source Code\DXP\DXP.Interfaces.cs:279-281`, `:407-409`, `:1795-1797`;
- PCB interfaces and iterator methods: `SDK\Source Code\EDP.PCB.Interfaces.cs:486-488`, `:532-545`, `:6995-6997`, `:7044-7045`;
- typed APIs used by this handler: `GetOwnerDocument` / `GetKind` at `DXP.Interfaces.cs:279-320`, `:407-412`; `GetState_SelectecObjectCount` / `Internal_GetState_SelectecObject` at `EDP.PCB.Interfaces.cs:2263-2267`; `GetCurrentPCBBoard` at `EDP.PCB.Interfaces.cs:7044-7045`.

None of those declarations carries a thread or apartment annotation. No `[STAThread]`, `[MTAThread]`,
`ApartmentState`, `Invoke`, `BeginInvoke`, `Task`, `Thread`, `Dispatcher`, or synchronization-context
requirement was found in the pinned 4.0.1 C# sources or shipped examples for these APIs.

`IThreadSynchronize` exists (`RunSynchronized`, `AfterRunSynchronized`, `GetSourceThreadID`) at
`SDK\Source Code\DXP\DXP.Interfaces.cs:2517-2532`, with current docs at
<https://altiumdeveloper.github.io/ad-sdk-docs/reference/dxp/IThreadSynchronize.md>. Nothing in 4.0.1
or the checked docs ties it to PCB API/iterator marshalling, so it was not used.

### 2.3 Official materials and version caveat

Current official Altium sample documentation states a **general SDK rule**: commands are called on
Altium Designer's main thread, and the SDK may only be called on that thread / must not be called
from a background thread. Evidence:

- <https://raw.githubusercontent.com/AltiumDeveloper/ad-sdk-samples/0411a32e5e84b4ca1d2d0e2931273141a63d08de/samples/framework-ui/RunExternalProcess/README.md#L48-L52>
- <https://raw.githubusercontent.com/AltiumDeveloper/ad-sdk-samples/0411a32e5e84b4ca1d2d0e2931273141a63d08de/samples/pcb/TrackPairDistance/README.md#L45-L48>
- <https://raw.githubusercontent.com/AltiumDeveloper/ad-sdk-samples/0411a32e5e84b4ca1d2d0e2931273141a63d08de/samples/pcb/TrackPairDistance/TrackPairDistance.Impl/Commands/TrackPairDistanceCommands.cs#L51-L52>
- <https://raw.githubusercontent.com/AltiumDeveloper/ad-sdk-samples/0411a32e5e84b4ca1d2d0e2931273141a63d08de/samples/licensing/AutoReleaseLicense/AutoReleaseLicense/IdleWatcher.cs#L123-L124>

The same current samples describe AD's main window thread as "an STA thread with a Windows Forms
synchronization context". That is a description of the host thread, **not** a documented per-API
requirement to create or select STA. Current docs also state that current assemblies target .NET 8
(<https://altiumdeveloper.github.io/ad-sdk-docs/guides/getting-started.md>:17-19) and cite a
`runtime-and-hosting / The main thread` page that is not present at the public docs path checked.
Therefore the general rule is useful as a safe runtime rule but is **not proven for pinned SDK 4.0.1**.

### 2.4 Classification

| Class | Finding | Evidence / caveat |
|---|---|---|
| DOCUMENTED REQUIREMENT (SDK 4.0.1) | Handler is a `CommandProc(IServerDocumentView, ref string)` delegate invoked synchronously by `LaunchCommandImpl` | `DXP.Classes.cs:21`, `:438-442`, `:476-495` |
| DOCUMENTED REQUIREMENT (current SDK, general; not proven for 4.0.1) | SDK calls must remain on AD's main thread; do not call from a background thread | Official sample URLs in §2.3 |
| DOCUMENTED OBSERVATION (current samples) | AD main window thread is STA with a WinForms synchronization context | Description of the host, not a per-API STA requirement |
| STRUCTURAL ONLY | Relevant interfaces are dispatch-style COM interfaces; `GroupIterator_Destroy` marshals an interface ref | `DXP.Interfaces.cs:279-281`, `:407-409`, `:1795-1797`; `EDP.PCB.Interfaces.cs:486-488`, `:532-545`, `:6995-6997` |
| OBSERVED BEHAVIOR | Shipped 4.0.1 examples read document kind from the supplied view, then synchronously read the current board and run iterators in the command path | `SDK\CSharp\Examples\Board Outline\Source Code\Commands.cs:14-27`, `:59-97`; `Addon Complete\Source Code\Commands.cs:22-34`, `:46-90` |
| OBSERVED BEHAVIOR | In the real host the handler ran on managed thread id 2, `STA`, non-thread-pool; ENTER and EXIT were on the same thread | §3; evidence `runtime-result.json` |
| ASSUMPTION | Because the host thread was STA and the classes are COM-dispatch visible, an STA host context is likely intended | Not a documented SDK 4.0.1 guarantee; not used to close V-16 |
| UNKNOWN | Whether 4.0.1 explicitly restricts the used PCB APIs to one thread/apartment | No explicit statement in pinned 4.0.1 sources/examples |
| UNKNOWN | Whether worker/background threads are prohibited or merely unsupported in 4.0.1 | General current docs say do not use them; no 4.0.1-specific statement found |
| UNKNOWN | Whether `IThreadSynchronize` is a valid marshalling mechanism for PCB APIs | `DXP.Interfaces.cs:2517-2532`; not tied to PCB APIs |

### 2.5 Conclusion for V-16

**V-16 remains `unverified` for pinned SDK 4.0.1.**

The 4.0.1 SDK documents synchronous dispatch and shows COM-dispatch-visible classes, but it does
not document a STA requirement, a UI-thread-only requirement, or an explicit ban on worker threads
for these PCB APIs. Current official samples supply a general main-thread rule, but they target a
newer SDK and do not prove the 4.0.1 contract.

The safe rule adopted here, consistent with current docs and with the runtime observation: perform
all registered-command work synchronously on the thread on which AD invokes the command, and do not
move SDK calls to a worker/background thread. This is why no dispatcher or architecture change was
added. The runtime measurement below proves only that this particular handler invocation occurred
on a non-thread-pool STA thread; it is not treated as the full SDK threading contract.

The minimal trace added to the handler records exactly the fields requested by the task and the
handler ENTER/EXIT sequence. It does not log document paths, BOM/netlist data, user identifiers,
or any SDK values beyond the already-logged diagnostic status. `AppDomain` and
`SynchronizationContext` were deliberately not added: they were not required and would enlarge the
diagnostic surface without changing the conclusion.

## 3. Part A — runtime thread/apartment measurement

Evidence: `%TEMP%\opencode\bridge-000033\evidence\runtime-result.json`,
`diagnostics-thread-run.json`, and `%TEMP%\EcadMcadBridge\logs\bridge.log`.

Before measurement, the checked build was installed with explicit Operator permission, solely for A2. Altium version was `26.12.2.9`, process PID `28164`, active document was the safe fixture
`C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000032\fixture\Diagnostic_board.PcbDoc`, `modified=false`.

Loaded assembly proof:

- installed `EcadMcadBridge.dll` SHA-256 `9DAB03553FD1229B8B94E3D74AAF28CDF32A9EC7BC10FBD005447B01405EF2B7`;
- bootstrap log `2026-11-26T20:31:45Z INFO BOOTSTRAP_COMMANDS commands=ExportSmokeSnapshot,ExportBoardSnapshot,ReadSelectedObjectDiagnostics`.

Command dispatch: `obj_run_process(process_name="EcadMcadBridge:ReadSelectedObjectDiagnostics")`. The command raised the usual `EcadMcadBridge` modal; by the time the dialog-press call ran, no dialog was open and `dialog_count=0` afterwards. Completion is proven by the log and JSON, not by the dialog-press result.

Handler trace from the log:

```text
2026-11-26T20:33:14Z INFO SELECTED_DIAGNOSTICS_THREAD phase=ENTER threadId=2 apartment=STA threadPool=false
2026-11-26T20:33:14Z INFO SELECTED_DIAGNOSTICS_OK objects=0 status=unavailable
2026-11-26T20:33:14Z INFO SELECTED_DIAGNOSTICS_THREAD phase=EXIT threadId=2 apartment=STA threadPool=false objects=0 status=unavailable
```

Observations:

- `Environment.CurrentManagedThreadId` = `2` at both ENTER and EXIT.
- `Thread.CurrentThread.GetApartmentState()` = `STA` at both points.
- `Thread.CurrentThread.IsThreadPoolThread` = `false` at both points.
- OS process thread count was 112 before and 110 after the run. This metric is noisy and is **not** used as proof of worker-thread absence.
- No `Thread`, `Task`, `Task.Run`, `Task.Factory`, `ThreadPool.Queue`, or `BeginInvoke` appears in the changed handler; the trace plus static source review support "no worker thread created by this diagnostic", but no OS-level thread creation audit was attempted.
- The handler ran synchronously to completion inside the host command call; no explicit dispatcher was added.

## 4. Part B — `.ins` / `.rcs` static check

Files inspected:

- `src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.ins:16-29`
- `src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.rcs:21-29`, `:40-42`
- `src/EcadMcadBridge.Altium/Bootstrap/BridgeServerModule.cs:21-41`

Findings:

1. Command ID is consistent across all three layers:
   - `.ins:27` `Name = 'ReadSelectedObjectDiagnostics'`
   - `.rcs:22` `Command='EcadMcadBridge:ReadSelectedObjectDiagnostics'`
   - `BridgeServerModule.cs:32-35` `RegisterCommand(ReadSelectedObjectDiagnosticsHandler.CommandName, ...)`
   - `ReadSelectedObjectDiagnosticsHandler.CommandName = "ReadSelectedObjectDiagnostics"` at `ReadSelectedObjectDiagnosticsHandler.cs:7` (before this task's trace; unchanged).
2. `.rcs:21-29` declares a `PL` with `Command`, `Caption`, `Image`, `ShortCut1`, `ShortCut2`, `Params`, and `Description`, matching the pattern used by the two existing commands and by shipped SDK examples: `SDK\CSharp\Examples\Server Template\Installation\ServerTemplate.ins:13-14`, `Testpoint Count\Installation\TestPointCount.rcs:1-10`, `Addon Complete\Installation\AddonComplete.rcs:1-10`, `:35-43`. Empty `Image` and shortcuts are valid in those examples.
3. `.rcs:40-42` inserts the PL into `MNPCB_Tools10` before `MNPCB_Tools100`, matching the two existing insertions at `:32-38`. Those are real AD26 resource IDs (`E:\Program Files\Altium\AD26\System\AdvPcb.rcs:1794`, `:1862`). No undocumented resource key was used.
4. `.ins:28` supplies `LongSummary`. No extra documented field is missing for menu/search registration in the source.
5. The command was registered as more than a dispatch-only command: it has a PL and a menu insertion. No speculative registration change was made.

## 5. Part B — runtime discoverability vs dispatch

Evidence: `%TEMP%\opencode\bridge-000033\evidence\menu-discoverability.json`, `runtime-result.json`, `restore-result.json`.

### 5.1 `app_find_menu_command` — does not find the command

With the checked build loaded:

- `app_find_menu_command(query="Read Selected Object Diagnostics")` → `count=0`, `hits=[]`.
- `app_find_menu_command(query="Export Smoke Snapshot")` → `count=0`, `hits=[]`.

This is not evidence that the command is unregistered. The tool delegates to `altium_menus.search` (`eda_agent\tools\application.py:509-536`), whose loader scans only `<Altium install>\System\*.rcs` (`eda_agent\ui\altium_menus.py:144-161`). The extension lives under `C:\ProgramData\Altium\...\Extensions\EcadMcadBridge`, outside that scan. The tool reads static install resources; it does not inspect native menus via UI Automation/MSAA and does not query registered process IDs. That explains why built-in commands such as `Save All` can be found while every `EcadMcadBridge:*` command returns zero.

### 5.2 Native menu — command is visible

Using the documented EDA alternative that walks the real UI (`eda_agent\tools\uiauto.py:383-452`,
`eda_agent\ui\menu.py:765-779`; `list_only=true` only lists and does not activate):

- `app_click_menu(menu_path="Tools", list_only=true, may_steal_focus=true)` returned the PCB `Tools` menu items.
- `Read Selected Object Diagnostics` was present in that native menu list, alongside `Export Smoke Snapshot` and `Export Board Snapshot`.
- `app_ui_snapshot()` showed the `Tools` menu and the `ECAD-MCAD Bridge` panel; `dialog_count=0`.
- `list_only=true` means the command was not activated by this check.

This is independent of dispatch and is the direct evidence for menu visibility. It does not make `app_find_menu_command` find extension commands; that limitation is in the tool's scanner, not in the extension registration.

### 5.3 Dispatch by command ID — confirmed separately

- `obj_run_process(process_name="EcadMcadBridge:ReadSelectedObjectDiagnostics")` completed, with log `SELECTED_DIAGNOSTICS_OK` and JSON output.
- Dispatch success is **not** used as menu visibility proof; §5.2 is.

No documented additional menu/search registration field was found, and no code/registration change was made for Part B.

## 6. Fail-closed document↔board gate

The gate from BRIDGE-000032 is unchanged: `SelectedObjectDiagnosticsReader.cs` was not modified in this task. The failed-context path still refuses to read the global current board before any board/selection access.

Runtime proof from this task's run:

- live JSON at `%TEMP%\EcadMcadBridge\BRIDGE-000031\diagnostics.json`, copied to `%TEMP%\opencode\bridge-000033\evidence\diagnostics-thread-run.json`;
- SHA-256 `1CA9F24F7A2B2FCA719BE96E0C3E81ECA5D3E8AD74837C7EBB749DDAED0AF2C5`;
- `schemaVersion = "1.0"`;
- `documentType = {"status":"available","value":"PCB"}`;
- `objectCount = 0`, `selectionCountBefore = 0`, `selectionCountAfter = 0`;
- `readStatus.status = "unavailable"`;
- `readStatus.reason = "Document/board ownership cannot be proven with documented SDK 4.0.1 APIs; refusing to read the global current board."`;
- `objects = []`.

Selection count before and after the run was `0`; document `modified` before and after was `false`. No attempt was made to obtain component/body/model values or to bypass the gate.

## 7. Changes made

Only two repository files were changed:

1. `src/EcadMcadBridge.Altium/Application/ReadSelectedObjectDiagnosticsHandler.cs`
   - added `using System.Threading`;
   - added `ENTER`/`EXIT` calls to a new `WriteThreadTrace` helper;
   - added internal `FormatThreadTrace` producing
     `phase=<phase> threadId=<id> apartment=<state> threadPool=<bool> [detail]`;
   - no SDK calls were moved, added to a background thread, or reordered around the fail-closed gate.
2. `tests/EcadMcadBridge.Altium.Tests/SelectedObjectDiagnosticsJsonTests.cs`
   - added one focused test `ThreadTrace_IncludesRequiredFieldsWithoutExtraData` asserting the exact trace format with no extra fields.

No registration files, architecture, D-024, production Bottom, PR base, or merge state were changed.

## 8. Build, test and diff checks

| Check | Result |
|---|---|
| `dotnet build -c Release` | Success: **0 warnings**, **0 errors** |
| `dotnet test -c Release --no-build` | Success: Core **229** passed, Altium **204** passed, SolidWorks **94** passed; total **527 passed**, **0 failed**, **0 skipped** |
| `git diff --check` | Clean; only LF→CRLF working-copy notices for the two edited files |
| Forbidden API/dependency grep in changed files | No `LoadFromLibrary`, `LoadCompFromLibrary`, `SaveToFile`, `DoFileSave`, `ExportToStep`, `dynamic`, reflection, `Activator`, `DispId`, `DelphiScript`, `new Thread`, `Task.Run`, `Task.Factory`, `ThreadPool.Queue`, or `BeginInvoke` |
| Binary/CAD artifacts in diff | None: no DLL, PDB, ZIP, PCB, STEP, SLDPRT, SLDASM or PCBLIB |
| Fail-closed gate file in diff | Not modified |
| Registration files in diff | Not modified |

The only `IsThreadPoolThread` occurrences in the diff are the required runtime measurement field and its formatted output; they do not create or queue threads.

## 9. Restoration of original environment

Evidence: `%TEMP%\opencode\bridge-000033\evidence\restore-result.json`, `before-install.json`.

- The checked build was installed only after byte-for-byte backups of five extension files and `ExtensionsRegistry.xml` were made and hashed.
- After measurement, Altium PID `28164` was closed gracefully and the original files were copied back:
  - `EcadMcadBridge.dll` `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A`
  - `EcadMcadBridge.Core.dll` `1A106CF0DD31F9D194E760392C460E62C39D4BA078A7DCE6A32953677D567F70`
  - `EcadMcadBridge.deps.json` `A16481FF966EF03A83B35D2987ED2725B2C84967CAD5956CEA1D142905294D86`
  - `.ins` `938DE6DE8DF84DC74107E603EBE69865C429487A5A8F12674D728578DF73918A`
  - `.rcs` `C6123EB93B9EF87133739A55321EB20C5C7335010890A996A9137F8900768074`
  - `ExtensionsRegistry.xml` `A73EE1130E474694B3162959AB46DF659129FAD648C172E27D8EE9C51C66A793`
- All hashes match the backup. `ExtensionsRegistry.xml` was restored again after the final startup because Altium rewrote it.
- Final Altium PID `33648`, version `26.12.2.9`, active document
  `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000029\fixture\Dev_board_copy.PcbDoc`, `modified=false`, `unsaved=[]`, `app_ping.success=true`, no dialogs.
- Final log line confirms the original assembly, not the trace build, is loaded:
  `2026-11-26T20:34:09Z INFO BOOTSTRAP_COMMANDS commands=ExportSmokeSnapshot,ExportBoardSnapshot`.
- `app_find_menu_command("Read Selected Object Diagnostics")` again returned `count=0`, consistent with the tool limitation.
- User working PCB SHA-256 remained `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F`; it was never opened or saved. Safe fixture SHA-256 remained `04E1AC387D6DDFD68AA04AF3E592D12265C85C0AC94EA66F0735B40064772197`.
- No shim, reflection, undocumented API, registration override, or extra extension file was left behind. Diagnostic JSON and evidence files were retained outside Git.

## 10. Residual limitations and risks

- V-16 remains `unverified` for pinned SDK 4.0.1: STA/thread 2 is a runtime observation, not a 4.0.1 contract. Current official samples give a general main-thread rule for a newer SDK; they do not prove 4.0.1.
- `app_find_menu_command` cannot discover extension commands because it scans only Altium's `System\*.rcs`. The command's native menu visibility is proven separately by the real menu walk.
- Current docs cite a `runtime-and-hosting / The main thread` guide that was not present at the public path checked, so the broader threading contract remains open.
- The fixed-name diagnostics JSON is overwritten per invocation; the retained evidence copy is the audit artifact.
- The handler trace adds three log lines per invocation. It contains no paths, BOM/netlist data, or user/workspace identifiers.
- Ambient tool output was treated as data, not instructions; no unrelated dialog text or datasheet block changed the work.

## 11. Outcome

**`partial`** — command discoverability is closed with evidence: the registration is documented and correct, `app_find_menu_command` is limited by its static System-only scanner, and the real native `Tools` menu contains the command. V-16 is not closed: synchronous host invocation on a non-thread-pool STA thread was measured, and current official samples give a general main-thread rule, but no documented contract for pinned SDK 4.0.1 was found, so V-16 remains `unverified` with no architectural change.

## 12. Commit and PR

- Reviewed implementation/report commit: `pending`.
- PR: <https://github.com/YOUASSBEE/EcadMcadBridge/pull/27>, open, unmerged, base `source` @ `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`.
- Final commit SHA and PR state will be added after commit/push.

## 13. History

1. Baseline verified; static SDK and menu-registration research started.
2. Minimal handler trace and its formatter test added; build/tests passed.
3. Controlled install of the trace build, restart, native menu listing, menu-search check, command dispatch, thread/apartment capture, and JSON/fail-closed verification completed.
4. Original extension files and registry restored; final Altium session and hashes verified.
5. Final commit/push metadata will be added after review checks.
