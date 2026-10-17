---
name: neat-crash-lessons
description: Read before debugging crashes, freezes or hangs in the neat browser (WinUI 3 + WebView2), reading its dumps or crash.log, or changing Windows App SDK / WebView2 versions. Records findings, traps and open items.
---

# neat browser: crash and freeze history, and how to avoid repeating mistakes

Repo: github.com/neatbrowser/neat (WinUI 3 browser "NEAT Browser", src/neat). This file records what earlier sessions learned while chasing an intermittent "spinning cursor, then crash" report. Read it before touching crash-related code, and update it when you learn something new.

## How to work with this user

- The user speaks Vietnamese. Reply in Vietnamese unless asked otherwise. Code, comments and commit messages stay in English.
- Do not change code until the user says so. "Khoan chua code" (wait, no code yet) was said many times and meant it. Investigate read-only first, report, and wait for approval.
- Sessions run on a Linux sandbox. You cannot compile or run WinUI here. Say plainly that a change is "not compiled". The first real compile is the Build workflow (windows-latest) on a PR; the user then downloads the `neat-win-x64` artifact and tests on Windows.
- Before any XAML or x:Name change, follow the `xaml-codebehind-sync` skill (run its check script). For icons see `winui-fluent-ui`.
- Work on a branch, push, and give the user the `.../pull/new/<branch>` link. Start each branch from a fresh `origin/main`: earlier PRs (#2, #3) were merged while a session was still running.
- Commit trailers: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` and the `Claude-Session:` line given by the session reminder.
- A stop hook sometimes warns "branch has unpushed commits and no remote branch" right after a successful push. It was a false alarm every time. Verify with `git rev-parse HEAD` against `git ls-remote origin <branch>`; if equal, say so and move on.

## Sandbox and network traps

- `curl` to api.nuget.org is refused by the proxy (403). Use WebFetch on nuget.org package pages and learn.microsoft.com release notes instead.
- WebFetch of raw GitHub source is truncated after about 1,350 lines. `gh` may be denied. To read Microsoft.UI.Xaml source at a specific commit, do a blobless `git fetch` of microsoft/microsoft-ui-xaml at that commit and read the file; `git grep` over the whole repo timed out.
- Pushing needs the Claude GitHub App installed on the repo; a 403 on push means the user must grant access.
- WebFetch release-notes pages are summarised by a small model and cut at ~100k characters. Treat "not covered on this page" as unknown, not as "no change".

## Project facts that matter for crashes

- Windows App SDK was 1.8.12 (1.8.260921001) and moved to 2.5.1 stable (PR #4, merged). The user has since run v0.14 builds on 2.5.1 daily and no 2.x-specific failure shows in crash.log; check the csproj for the current value. Self-contained, unpackaged (`WindowsPackageType=None`), net8.0-windows10.0.19041.0, x64 only.
- WebView2 is a Fixed Version runtime bundled next to neat.exe (NuGet `WebView2.Runtime.X64` + `.Core`, same version for both; 155.0.4283.60 when this was written). Microsoft.Data.Sqlite 8.0.8.
- `src/neat/lib/` (History / Bookmarks Library: LibList, LibView, LibRow) and `src/neat/look/` (colour picker, settings) were added by the user after the first crash work. Do not assume they were part of any earlier dump.
- Every SQLite call opens a new connection and some run on the UI thread (history add, counts, bookmark lookup).

## Findings so far

### 1. Exit-time fail-fast (0xc000027b) on every normal close: FIXED (PR #2)
- Cause: `bar.LayoutUpdated` still fired while the window was closing, and `Holes()` in win.bar.cs used `AppWindow.Id`, which throws after close. An exception escaping a XAML callback becomes a stowed-exception fail-fast, so every normal exit wrote a dump.
- Fix: `_closed` flag set in `OnClosed`, `LayoutUpdated` handler is a named method and is unsubscribed in `OnClosed`, `Holes()` returns early when `_closed` or `XamlRoot` is null.
- 9 of the first 10 dumps in C:\CrashDumps were this exit bug. They are obsolete.

### 2. Hover crash in the tab list (0xc0000005): EXPERIMENTAL FIX MERGED (PR #3), NOT PROVEN
- Symptom: access violation inside Microsoft.UI.Xaml.dll 3.1.8.0, fault offset 0x3455a, while the mouse hovers a row of the sidebar tab ListView.
- Stack (dump neat.exe.1884): `ListViewBaseItem::OnPointerEntered` -> `ChangeVisualState` -> `CListViewBaseItemChrome::GoToChromedStateNewStyle` -> `SetSelectionIndicatorBackground` -> `SetValueByKnownIndex`.
- Source reading (ListViewBaseItemChrome.cpp): `SetSelectionIndicatorBackground` dereferences `m_selectionIndicatorRectangle` with only an assert. The rectangle is created by `EnsureSelectionIndicator`, which runs when the selected state changes, not on a pointer-over change. A selected row without the rectangle crashes on hover.
- Change made: in the tab ListView resources (src/neat/win.xaml) `ListViewItemSelectionIndicatorVisualEnabled` is `False`. The accent bar was already painted transparent, so nothing visible changes. This removes the code path; it does not explain why the row had no rectangle.
- Do not claim this is the root cause. The test is whether new dumps with this stack stop appearing. If one appears again, the experiment failed; reopen the investigation with the new dump. As of 17 Oct no dump with this stack has been reported since the fix, but nobody has confirmed it either.

### 3. Reload (and Back / Forward) before the web view has started: FIXED (PR #6), not yet verified at runtime
- Dump `9bac2040` (`kn` only): fail-fast 0xc000027b from a pointer-up gesture: `RaiseDelayedPointerUpEvent` -> `CEventManager::RaiseControlEvents` -> `DXamlCore::FireEvent` -> `CLR_FireEvent` -> `ReportUnhandledError`. That means "a managed handler threw". The stack has no managed frames and cannot say which handler or which exception.
- crash.log of 16 Oct 13:28:15 named it, 2 s after launch: `InvalidOperationException 0x8000000E: Reload(): Failed because a valid CoreWebView2 is not present`, from `Win.reload_Click`. The tab's `CoreWebView2` only exists after `EnsureCoreWebView2Async` finishes (the "WebView2 <version> (bundled runtime)" log line comes after that). The dump is very probably the same event, but its file time was never compared.
- Fix: one `Win.Reload()` that returns while `CoreWebView2` is null and catches and logs failures; `reload_Click`, Ctrl+R and F5 use it. `Back()` / `Fwd()` go through `CoreWebView2` too. The Ctrl+Shift+R / Ctrl+F5 path (`HardReload`) already checked for null.
- Rule from this: any handler that touches a tab's WebView2 must check `CoreWebView2` for null first. Any exception that leaves an XAML callback (click, key accelerator, pointer, LayoutUpdated) ends the whole app.
- To verify: start the app and press Reload / F5 / Ctrl+R within the first second or two. It must do nothing.

### 4. Build break from a missing using in lib/librow.cs: FIXED (PR #5)
- `LibSel : DataTemplateSelector` needs `using Microsoft.UI.Xaml.Controls;`. The XAML errors in the same log (WMC0909, WMC0001, WMC1111, WMC9999) were follow-ons: the XAML pass cannot resolve `LibRow`, `LibHead` and `LibSel` while the C# does not compile. Read the first CSxxxx error of a Build log before the WMC ones.

### 5. Hypotheses that were disproved
- "Many tabs cause the freeze": the logs showed no correlation.
- "WebView2 crashes": `ProcessFailed` logging showed none before the crashes; the faulting module is Microsoft.UI.Xaml.dll, not WebView2.
- Most 0xc000027b events were the exit bug (finding 1), not mid-session crashes.

## Diagnostics in the app (src/neat/web/log.cs)

- `Log` writes crash.log in `%LOCALAPPDATA%\NEAT`, rotated at 1 MB. `Log.Timed` / `Log.TimedValue` wrap SQLite calls. A line "window closed normally" means a clean exit; its absence means a crash.
- `Heartbeat` is a UI-stall watchdog (2 s threshold). App.xaml.cs hooks AppDomain, TaskScheduler and Application unhandled-exception events; they log but never mark the exception handled.
- Known flaws, not yet fixed: the WebView2 process count includes ALL msedgewebview2 processes on the machine, not just NEAT's; "system memory 0" appears before the first GC and is meaningless; stall lines can be logged out of order. Do not draw conclusions from those numbers.

## Analysing dumps

- Ask for crash.log FIRST. `App.xaml.cs` logs every unhandled XAML exception as a `[xaml]` line with type, message and a managed stack before the app dies. Of 42 such lines (8 to 16 Oct) 41 were the old `Holes()` exit bug and one was `reload_Click`; those lines named the culprit where the dump could not.
- To find real crashes in a crash.log: list the sessions (lines with `[app] ... start,`), mark those with no `window closed normally`, then look for a `[xaml]` line inside each. A session with no clean exit and no `[xaml]` line did not die through the XAML path and needs another explanation.
- Dumps come from WER LocalDumps into `C:\CrashDumps`. In WinDbg: `.ecxr`, `kn`, `!analyze -v`. Ask the user to paste text output rather than screenshots when possible.
- A `kn` of a stowed-exception fail-fast (frames `FailFastWithStowedExceptions`, `ReportUnhandledError`) shows only where the error was reported, not where it was thrown. The HRESULT, exception type and managed stack are in the stowed exception record (`!analyze -v`) or in crash.log.
- Match the dump to the build: Microsoft.UI.Xaml.dll was 3.1.8.0 with Windows App SDK 1.8. After the 2.x upgrade the version differs, so symbols and offsets from the old dumps no longer apply.
- Check that the user is running the build you think. One earlier "new crash" came from an old download; the user later confirmed and updated.
- Exit dumps (stowed exception on close) and real crashes need separate handling: look at the stack first.

## Open items (nothing here is done unless the git log says so)

- Two sessions ended with no "window closed normally" and no `[xaml]` line, so they are unexplained: 15 Oct (started 11:42, last line 13:02) and 16 Oct (started 15:16, last line 23:42:21). The second ended right after the WebView2 GPU process crashed (`GpuProcessExited`, reason Crashed, exit code 34, logged once per tab, 9 tabs). At 21:31 the machine was at 7377 of 8107 MB RAM with 19 msedgewebview2 processes. Out-of-memory is only a guess. Needs the WER dump or Event Viewer entry for 23:42.
- No catch-all around handlers exists yet. Only the Reload path was hardened (finding 3). Option, not approved: a small guard around every handler so a failed one is logged instead of ending the app.
- UI stalls of 2 to 8 s at startup or idle (`[stall]` lines), cause unknown; some coincide with `history.add` on the UI thread (up to 3751 ms earlier, 100 to 270 ms lately).
- Move SQLite off the UI thread and enable WAL; drop the per-page `Stats()` queries.
- Fix the diagnostics flaws above.
- Hover fix (finding 2) and the reload fix (finding 3) still need runtime confirmation from the user.
- WebView2 runtime version should be re-checked against NuGet when touching the csproj.
- Delete the obsolete exit dumps in C:\CrashDumps (keep neat.exe.1884.dmp as the reference for finding 2).

## Rules that would have saved time

1. State only what the evidence shows. Mark guesses as guesses, and say "not compiled / not run" for every unverified change.
2. Read the dump stack before forming a theory. The first theories here (many tabs, WebView2) were wrong.
3. Check which build and which Microsoft.UI.Xaml version produced a dump before comparing dumps.
4. When a diagnostic number looks odd, suspect the diagnostic first (see the known flaws).
5. Prefer removing an unsafe code path to hiding its symptom, but record that it is not a root-cause proof.
6. Re-fetch origin before branching, and confirm local and remote heads with `git ls-remote` instead of trusting hook warnings.
7. Evidence beats reasoning about framework internals. In the reload case a plausible argument said a Click handler would not appear on that stack; the log showed it did. Do not rule a suspect out from how you think XAML dispatches events.
8. Ask for crash.log before asking for dump output, and say which lines to send. It is cheaper for the user and usually decisive.
9. After any push, if the stop hook still complains, check `git branch -r --contains HEAD`: the commit is often already merged into `origin/main`. The hook appears to inspect the clone in `/home/claude/neat` (the shell cwd resets there); keeping that clone on an up-to-date `main` silenced it.
