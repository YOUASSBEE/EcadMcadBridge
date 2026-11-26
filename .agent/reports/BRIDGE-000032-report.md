---
id: BRIDGE-000032
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T23:20:30+03:00
updated: 2026-11-26T23:23:00+03:00
base: source
branch: task/BRIDGE-000031-sdk-diagnostic-host
commit: 8260335fa01cac510301f8e850cfdef84bc507c2
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/27
related: [BRIDGE-000031, BRIDGE-000032]
---

# BRIDGE-000032 — controlled install and runtime validation of selected-object diagnostics

## 1. Git baseline

- Fresh metadata was read from GitHub for PR #27 before any change.
- PR #27: <https://github.com/YOUASSBEE/EcadMcadBridge/pull/27>; `state=open`,
  `merged=false`, base `source` @ `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`,
  expected head `task/BRIDGE-000031-sdk-diagnostic-host` @
  `31da0732e2c2ea71189cd56315171be65b25b7e3`.
- Local checks after `git fetch origin --prune`:
  - `HEAD` = `31da0732e2c2ea71189cd56315171be65b25b7e3`;
  - `origin/source` = `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`;
  - `origin/task/BRIDGE-000031-sdk-diagnostic-host` =
    `31da0732e2c2ea71189cd56315171be65b25b7e3`;
  - working tree clean.
- Work continued in the existing PR branch. No separate branch was created, PR base was not
  changed, and PR #27 was not merged.
- No `.agent/tasks/BRIDGE-000032*.md` file exists; the task was executed from the Operator text.

## 2. Document ↔ PCB context check and fail-closed decision

### 2.1 Static SDK evidence

Pinned SDK: Altium Designer SDK 4.0.1 at
`C:\ProgramData\Altium\Altium Designer {B4282C90-18BF-448F-BF1A-9E36AE887190}\Extensions\Altium Developer\SDK4.0\CSharp\Source Code`.

Path prefix used below: `SDK`. Line numbers are from the pinned sources.

| API / signature | Source | Status | What it proves |
|---|---|---|---|
| `static IServerDocument IServerDocumentViewHelper.GetOwnerDocument(this IServerDocumentView view)` | `SDK\DXP\DXP.Interfaces.cs:13364-13370` | Public typed helper over `Internal_GetOwnerDocument()`; accepted in this repository as C-67/C-68 | Gives the owner document, not a board |
| `string IServerDocument.GetKind()`; `string IServerDocument.GetFileName()` | `SDK\DXP\DXP.Interfaces.cs:319-320`, `313-314` | Public typed | Kind/path only; filename matching is explicitly forbidden by the task |
| `int IServerDocument2.GetOpenDocumentId()` | `SDK\DXP\DXP.Interfaces.cs:12804-12805` | Public typed | Per-open-document id, but there is no board-side accessor for the same id |
| `uint IExternalForm.GetHandle()` (inherited by `IServerDocumentView`) | `SDK\DXP\DXP.Interfaces.cs:198-199`, `219`, `409` | Public typed | View/window handle; no documented equivalence to a board handle |
| `uint IPCB_Board.GetState_Window()` | `SDK\EDP.PCB.Interfaces.cs:2125-2126` | Public typed | Board window handle; comparing it to a view handle is a heuristic, not proof |
| `string IPCB_Board.GetState_FileName()`; `bool IPCB_Board.HasServerDocument()`; `int IPCB_Board.GetState_BoardID()` | `SDK\EDP.PCB.Interfaces.cs:2128-2129`, `2674-2675`, `2686-2687` | Public typed | Board metadata; no counterpart token on the supplied view/document |
| `static IPCB_Board IPCB_ServerInterfaceHelper.GetPCBBoardByBoardID(this IPCB_ServerInterface, int)` | `SDK\EDP.PCB.Interfaces.cs:16668-16672` over `7137-7138` | Public typed helper over internal call | Needs a board id that cannot be obtained from the view/document |
| `static IPCB_Board IPCB_ServerInterfaceHelper.GetCurrentPCBBoard(this IPCB_ServerInterface)` | `SDK\EDP.PCB.Interfaces.cs:16555-16559` over `7044-7045` | Public typed helper over internal call | Global current board; not bound to the command's `IServerDocumentView` |
| `static IServerDocumentView IClientHelper.GetCurrentView(this IClient)` | `SDK\DXP\DXP.Interfaces.cs:14053-14056` | Public typed helper over internal call | Global current view, not the supplied view |
| `static IDXPDocument IDXPWorkSpaceHelper.DM_FocusedDocument(this IDXPWorkSpace)` | `SDK\DXP\DXP.Interfaces.cs:13938-13942` | Public typed helper over internal call | Global focused document, not the supplied view |

Shipped examples show the same pattern as the repository: check
`argView.GetOwnerDocument().GetKind()` and then call `GetCurrentPCBBoard()`
(`SDK\..\Examples\Board Outline\Source Code\Commands.cs:14-26`, `29-41`, `44-56`, `61-69`, `101-109`;
`ModelPreview\Source Code\Commands.cs:17-27`, `101`). No example proves view/document ↔ board identity.

Absent from the pinned SDK C# sources: `GetState_Document`, `GetOwnerBoard`, `BoardFromDocument`,
or any public typed view/document → `IPCB_Board` accessor. Therefore document kind alone is not
sufficient, and no filename/window/handle heuristic was used.

### 2.2 Implemented behavior

`SelectedObjectDiagnosticsReader.Read` now calls
`TryValidateContext(documentType, BoardOwnershipProven, out reason)` before any board or
selection call. `BoardOwnershipProven` is hard-coded to `false` with a comment explaining the SDK
limitation. On failure the report has:

- `documentType` preserved from the supplied view when readable;
- `objectCount`, `selectionCountBefore`, `selectionCountAfter` = `0`;
- `readStatus.status = "unavailable"`;
- `readStatus.reason = "Document/board ownership cannot be proven with documented SDK 4.0.1 APIs; refusing to read the global current board."`

No `PCB.GlobalVars.PCBServer`, `GetCurrentPCBBoard`, `GetState_SelectecObjectCount` or
`GetState_SelectecObject` call is reachable when the context is unproven. The existing read-only
primitive/body/model code remains behind that gate for a future task that establishes a documented
proof; it was not executed in this runtime validation.

Focused tests added (4):

- PCB document with a proven board is accepted by the policy function;
- incomplete/null document context is rejected with an explicit reason;
- non-PCB document is rejected;
- unproven document/board link is rejected and the reason is serialized into `readStatus`.

## 3. Static contract vs runtime observation

| Claim | Static contract | Runtime observation in this task |
|---|---|---|
| Command registered | `CommandProc`, `RegisterCommand`, `.ins`/`.rcs` declarations; C-07…C-09, C-61…C-64, C-71, C-72 | Log `2026-11-26T20:14:04Z INFO BOOTSTRAP_COMMANDS commands=ExportSmokeSnapshot,ExportBoardSnapshot,ReadSelectedObjectDiagnostics` |
| View/document access | `GetOwnerDocument()`, `GetKind()`; C-67/C-68/C-84/C-89 | JSON `documentType` = `available`, value `PCB` |
| Current PCB board | `GetCurrentPCBBoard()` helper C-18/C-19 | **Not called**; fail-closed gate refused because ownership cannot be proven |
| Selection read | `GetState_SelectecObjectCount()`, `GetState_SelectecObject(int)`; C-120 | **Not executed**; JSON counts are `0` by design |
| Result path | `%TEMP%\EcadMcadBridge\BRIDGE-000031\diagnostics.json`; repository convention C-69 | File created at `C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000031\diagnostics.json` |
| Result delivery | `DXP.Utils.ShowInfo` / `ShowWarning`; C-69 | Modal titled `EcadMcadBridge`, OCR text `Selected-object diagnostics written to:`; pressed `OK` |
| Thread/apartment | V-16 remains open | No new background thread; no CAD evidence about apartment requirements |

Static SDK signatures are not presented as runtime evidence; the right-hand column is the only
runtime basis for the conclusions in this report.

## 4. Build and tests before install

| Command | Result |
|---|---|
| `dotnet build -c Release` | Success: 0 warnings, 0 errors |
| `dotnet test -c Release --no-build` | Success: Core 229 passed, Altium 203 passed, SolidWorks 94 passed; total **526 passed**, 0 failed, 0 skipped |
| `git diff --check` | Clean |
| Forbidden API/dependency grep in changed `.cs` | No `LoadFromLibrary`, `LoadCompFromLibrary`, `SaveToFile`, `DoFileSave`, `ExportToStep`, `dynamic`, reflection, `DispId`, `Internal_`, `DelphiScript`, selection mutation or internal COM calls |
| Iterator cleanup | `GroupIterator_Create` paired with `GroupIterator_Destroy(ref iterator)` in `finally`; not reached because fail-closed |
| Command IDs | `ReadSelectedObjectDiagnostics` appears once in `RegisterCommand`, once in `.ins`, once as `.rcs` PL/Command and once as a menu link; no duplicate ID found |
| JSON schema/path | `schemaVersion=1.0`, fixed `%TEMP%` path; covered by serializer/writer tests |
| Git artifacts | No DLL/PDB/ZIP/PCB/STEP/SLDPRT/PCBLIB in diff |

Installation was started only after these checks passed.

## 5. Initial Altium state

Evidence: `%TEMP%\opencode\bridge-000032\evidence\before-install.json`, captured
`2026-11-26T23:12:42+03:00`.

- Altium PID `27116`, `E:\Program Files\Altium\AD26\X2.EXE`, version `26.12.2.9`.
- Active document: `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000029\fixture\Dev_board_copy.PcbDoc`,
  kind `PCB`, `modified=true`, selection count `0`.
- `app_context.unsaved` listed only that temporary fixture, not a user working PCB.
- Safe test PCB: `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000032\fixture\Diagnostic_board.PcbDoc`,
  SHA-256 `04E1AC387D6DDFD68AA04AF3E592D12265C85C0AC94EA66F0735B40064772197`; a copy made in a
  temp directory from an earlier temp fixture, not a user project file.
- User working PCB `D:\Git\Projects\mdm15-1v05m_electric\Dev_board.PcbDoc`, SHA-256
  `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F`; never opened, saved or
  modified by this task.
- Original extension hashes: `EcadMcadBridge.dll`
  `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A`, `EcadMcadBridge.Core.dll`
  `1A106CF0DD31F9D194E760392C460E62C39D4BA078A7DCE6A32953677D567F70`, `EcadMcadBridge.deps.json`
  `A16481FF966EF03A83B35D2987ED2725B2C84967CAD5956CEA1D142905294D86`, `.ins`
  `938DE6DE8DF84DC74107E603EBE69865C429487A5A8F12674D728578DF73918A`, `.rcs`
  `C6123EB93B9EF87133739A55321EB20C5C7335010890A996A9137F8900768074`.
- `ExtensionsRegistry.xml` SHA-256
  `A73EE1130E474694B3162959AB46DF659129FAD648C172E27D8EE9C51C66A793`; backed up before install.
- `app_find_menu_command(query="Read Selected Object Diagnostics")` returned `count=0` before install.
- The unsaved temporary fixture was closed with `discard_changes=true`; its file hash on disk was
  unchanged. No user document was saved.

## 6. Controlled install and loaded assembly

- Backup directory: `%TEMP%\opencode\bridge-000032\backup` (five extension files plus registry).
- Installed from `src/EcadMcadBridge.Altium/bin/Release/net10.0-windows/`:
  - `EcadMcadBridge.dll` SHA-256 `804318BD6CE19DC546B08C18316B9E8E1844D5321A1EA5C8B568CBDFC4E0C880`;
  - `EcadMcadBridge.Core.dll` SHA-256 `0892D0AAC8CE9A43076931592B9D8775B62F2A8AEBFD46886C5EC65C1600E7E3`;
  - `EcadMcadBridge.deps.json` unchanged
    `A16481FF966EF03A83B35D2987ED2725B2C84967CAD5956CEA1D142905294D86`;
  - `.ins` SHA-256 `196870F824557F2AFE4BC7D924B64DC08B27A0A8DACE7F2561276ABA8BA1B4F3`;
  - `.rcs` SHA-256 `D19F9526053509A085D8F43EA7A0DE8BC2AAEC4661B9AE04C295AC6D92C1F2D9`.
- Altium was closed gracefully (`CloseMainWindow=true`, process exited) and restarted with the safe
  fixture; new PID `29296`. The EDA polling loop required `app_recover_script` after restart; it
  responded again with bridge `2026.11.25.1`.
- Actual loading of the checked assembly is proven by the bootstrap log line
  `2026-11-26T20:14:04Z INFO BOOTSTRAP_COMMANDS commands=ExportSmokeSnapshot,ExportBoardSnapshot,ReadSelectedObjectDiagnostics`
  — the old installed DLL logged only the first two commands.
- `app_find_menu_command` still returned `count=0` for the diagnostic caption after restart. The
  command was nevertheless available and executed by dispatch through
  `obj_run_process(process_name="EcadMcadBridge:ReadSelectedObjectDiagnostics")`. No shim or API
  substitution was used.
- If the extension had not loaded or the command had not registered, the plan was to stop and
  restore; that path was not needed.

## 7. Runtime command result and JSON validation

Evidence: `%TEMP%\opencode\bridge-000032\evidence\runtime-result.json` and the two retained JSON copies.

- Command executed twice on the safe fixture with a pre-fixed selection of 10 `SOD323` components:
  - run 1: `2026-11-26T20:16:28Z`, log `SELECTED_DIAGNOSTICS_OK objects=0 status=unavailable`;
  - run 2: `2026-11-26T20:17:24Z`, same log status and `objects=0`.
- Each invocation raised one `EcadMcadBridge` modal dialog; `OK` was pressed and
  `app_list_open_dialogs` afterwards reported `dialog_count=0`. No unhandled exception, hang or
  forced process kill occurred during the runs.
- JSON at `%TEMP%\EcadMcadBridge\BRIDGE-000031\diagnostics.json` parsed successfully and matched
  schema 1.0:
  - `schemaVersion = "1.0"`;
  - `measuredAtUtc` present (`2026-11-26T20:16:28Z` then `2026-11-26T20:17:24Z`);
  - `documentType = {"status":"available","value":"PCB"}`;
  - `objectCount = 0`, `selectionCountBefore = 0`, `selectionCountAfter = 0`;
  - `readStatus.status = "unavailable"` with the explicit ownership reason from §2.2;
  - `objects = []`.
- The zero counts are the expected fail-closed result, not a successful selection read: the command
  refused to touch the global board because the supplied view could not be tied to it. This matches
  the task's required behavior for an unconfirmed document/board context.
- No conclusions about library identity were made from `Pattern`, `Identifier`, filename,
  checksum, per-instance `UniqueID`, `PackageSpecificHash` or `ModelHash`. D-024 and BRIDGE-000030
  variant C are untouched.

## 8. SHA-256 of diagnostics.json

| Artifact | SHA-256 |
|---|---|
| `...\evidence\diagnostics-first-run.json` (copy of the file created at `20:16:28Z`) | `A4D3A6043C039DBF97FD4D4BE303DD45291BE7821D260B507A0814AD9EE96DA8` |
| `...\evidence\diagnostics-second-run.json` (copy of the file created at `20:17:24Z`) | `26DC175BFF9B3AA07BE388B0E83E5176DD166804BF65A7BC05BF739C3B085E4D` |

Full path of the live output:
`C:\Users\Anton\AppData\Local\Temp\EcadMcadBridge\BRIDGE-000031\diagnostics.json` (overwritten per
invocation). The retained copies are outside Git and were not deleted.

## 9. Selection and GetModified before/after

Measured around run 2 via EDA, on `Diagnostic_board.PcbDoc`:

| Measurement | Before run 2 | After run 2 |
|---|---|---|
| `app_get_active_document.modified` | `true` | `true` |
| Selection count | `10` | `10` |
| Selection composition | 10 × `eComponentObject`, `Pattern=SOD323`; same `ObjectId`, `Layer` and `SourceUniqueId` tuple list | identical tuple list |

The document was already `modified=true` before the diagnostic run: setting the 10-object
selection through EDA marked the temporary fixture modified. The diagnostic command did **not**
change that flag, the selection count or the selection composition. Therefore "document must not
become modified due to diagnostics" is satisfied for the command itself; this report does not claim
the fixture was unmodified before the command.

## 10. PCB state and absence of modification

- Safe fixture SHA-256 before install and after both runs:
  `04E1AC387D6DDFD68AA04AF3E592D12265C85C0AC94EA66F0735B40064772197` — unchanged on disk.
- User working PCB SHA-256 before and after all work:
  `0B3D67B6144AA7D766C850DAA797287319CA17AAB8345BE4557BD0D8807D167F` — unchanged; not opened,
  not saved.
- No `.PcbDoc`, `.PCBLIB`, STEP, DLL or other CAD binary was added to Git; diff artifact check was
  empty.
- The fail-closed reader performed no board object access, so it could not add, delete, select, move
  or save PCB objects. EDA selection changes were confined to the safe fixture and were discarded
  on close.

## 11. Restoration of the original configuration

Evidence: `%TEMP%\opencode\bridge-000032\evidence\restore-result.json`.

- The test document was closed with `discard_changes=true`; `app_context.unsaved` was empty before
  the test process was closed. Altium PID `29296` exited via `CloseMainWindow`.
- All five original extension files were copied back from backup and verified byte-identical:
  - `EcadMcadBridge.dll` `7D1E21C29FDAA71B9DBFD84438D4B75417349D5ACEDC2F8327FF2365AC2A453A`;
  - `EcadMcadBridge.Core.dll` `1A106CF0DD31F9D194E760392C460E62C39D4BA078A7DCE6A32953677D567F70`;
  - `EcadMcadBridge.deps.json` `A16481FF966EF03A83B35D2987ED2725B2C84967CAD5956CEA1D142905294D86`;
  - `.ins` `938DE6DE8DF84DC74107E603EBE69865C429487A5A8F12674D728578DF73918A`;
  - `.rcs` `C6123EB93B9EF87133739A55321EB20C5C7335010890A996A9137F8900768074`.
- Final `ExtensionsRegistry.xml` SHA-256 matches the backup:
  `A73EE1130E474694B3162959AB46DF659129FAD648C172E27D8EE9C51C66A793`. Altium rewrote this file
  during the final startup; the byte-identical backup was restored afterwards and verified.
- No temporary shim, registration override or extra file exists in the extension directory; it
  contains exactly the original five files.
- Altium was restarted in the original configuration with the original temp fixture: final PID
  `33048`, version `26.12.2.9`, active document
  `C:\Users\Anton\AppData\Local\Temp\opencode\bridge-000029\fixture\Dev_board_copy.PcbDoc`, kind
  `PCB`, `modified=false`, `unsaved=[]`. `app_ping.success=true` after `app_recover_script`.
- Final bootstrap log confirms the old assembly is loaded:
  `2026-11-26T20:18:39Z INFO BOOTSTRAP_COMMANDS commands=ExportSmokeSnapshot,ExportBoardSnapshot`.
  `app_find_menu_command` again returned `count=0` for the diagnostic caption.
- Diagnostic JSON and evidence copies were retained. Only the in-memory unsaved changes that
  existed in the original temp fixture at task start were discarded; the file on disk was not
  changed. No user document was affected.

## 12. Limitations and remaining risks

- Runtime selection reads remain unavailable by design: SDK 4.0.1 provides no documented proof that
  the board from `GetCurrentPCBBoard()` belongs to the command's view. Component/body/model fields
  were verified statically and by JSON serializer tests, not by CAD runtime values.
- The command was dispatched by Altium process ID through `obj_run_process`; the menu-search tool
  did not find its caption. Registration and execution are proven by log and JSON, menu visibility
  is not.
- `documentType` is available in JSON, but document↔board identity is not. A future task needs a
  documented SDK contract or a different command input before enabling selection reads.
- Thread/apartment requirements for PCB API remain open (ledger V-16). No background thread was
  used in this validation.
- The live diagnostic file has a fixed name and is overwritten on every invocation; retained copies
  under `%TEMP%\opencode\bridge-000032\evidence` are the audit artifacts.
- `SDK_API_LEDGER.md` was not modified because this task's scope was validation and fail-closing,
  not ledger expansion; the newly reviewed APIs are cited above from the pinned SDK sources.
- Ambient tool responses containing unrelated instructions were treated as untrusted data and did
  not affect this work.

## 13. Outcome

**passed** — for the validated scope: controlled install loaded the exact checked assembly,
`ReadSelectedObjectDiagnostics` executed in AD26.12.2.9, produced schema-1.0 JSON with an explicit
fail-closed reason, did not change selection or `GetModified()` on the safe fixture, left the user
PCB untouched, and the original extension configuration was restored and verified.

This is **not** a claim that runtime selected-object reads succeeded; they were intentionally
blocked because document↔board ownership cannot be proven with documented public SDK 4.0.1 APIs.

## 14. Changed files

- `src/EcadMcadBridge.Altium/Application/SelectedObjectDiagnosticsReader.cs` — added the explicit
  context gate and `TryValidateContext` policy; no board/selection access on unproven context.
- `tests/EcadMcadBridge.Altium.Tests/SelectedObjectDiagnosticsJsonTests.cs` — four focused tests for
  required context cases and explicit unavailable reason in JSON.
- `.agent/reports/BRIDGE-000032-report.md` — this report.

## 15. Commit and PR

- Reviewed commit: `8260335fa01cac510301f8e850cfdef84bc507c2`.
- PR: <https://github.com/YOUASSBEE/EcadMcadBridge/pull/27>, `state=open`, `merged=false`, base
  `source` @ `ea817c9b1e52b6a86e5eb541cf814a578887a7d5`, head branch
  `task/BRIDGE-000031-sdk-diagnostic-host`. The reviewed SHA above is the implementation/report
  commit; later metadata commits do not change the reviewed code.

## 16. History

1. First version: baseline verified, SDK link search completed, fail-closed gate and tests added,
   full build/tests passed.
2. Controlled install, restart, two runtime runs, JSON/schema/hash checks, before/after selection and
   `GetModified()` measurements completed.
3. Original DLL/deps/.ins/.rcs and registry restored, Altium restarted in original configuration,
   final hashes and PID recorded.
4. Separate metadata commit added the reviewed SHA and confirmed the open, unmerged PR after push.
