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

- Windows App SDK was 1.8.12 (1.8.260921001), then moved to 2.5.1 stable on branch `chore/winappsdk-2.5.1` (not verified at runtime when this was written; check the csproj for the current value). Self-contained, unpackaged (`WindowsPackageType=None`), net8.0-windows10.0.19041.0, x64 only.
- WebView2 is a Fixed Version runtime bundled next to neat.exe (NuGet `WebView2.Runtime.X64` + `.Core`, same version for both). Microsoft.Data.Sqlite 8.0.8.
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
- Do not claim this is the root cause. The test is whether new dumps with this stack stop appearing. If one appears again, the experiment failed; reopen the investigation with the new dump.

### 3. Hypotheses that were disproved
- "Many tabs cause the freeze": the logs showed no correlation.
- "WebView2 crashes": `ProcessFailed` logging showed none before the crashes; the faulting module is Microsoft.UI.Xaml.dll, not WebView2.
- Most 0xc000027b events were the exit bug (finding 1), not mid-session crashes.

## Diagnostics in the app (src/neat/web/log.cs)

- `Log` writes crash.log in `%LOCALAPPDATA%\NEAT`, rotated at 1 MB. `Log.Timed` / `Log.TimedValue` wrap SQLite calls. A line "window closed normally" means a clean exit; its absence means a crash.
- `Heartbeat` is a UI-stall watchdog (2 s threshold). App.xaml.cs hooks AppDomain, TaskScheduler and Application unhandled-exception events; they log but never mark the exception handled.
- Known flaws, not yet fixed: the WebView2 process count includes ALL msedgewebview2 processes on the machine, not just NEAT's; "system memory 0" appears before the first GC and is meaningless; stall lines can be logged out of order. Do not draw conclusions from those numbers.

## Analysing dumps

- Dumps come from WER LocalDumps into `C:\CrashDumps`. In WinDbg: `.ecxr`, `kn`, `!analyze -v`. Ask the user to paste text output rather than screenshots when possible.
- Match the dump to the build: Microsoft.UI.Xaml.dll was 3.1.8.0 with Windows App SDK 1.8. After the 2.x upgrade the version differs, so symbols and offsets from the old dumps no longer apply.
- Check that the user is running the build you think. One earlier "new crash" came from an old download; the user later confirmed and updated.
- Exit dumps (stowed exception on close) and real crashes need separate handling: look at the stack first.

## Open items (nothing here is done unless the git log says so)

- UI stalls of 2 to 6.6 s at startup or idle, cause unknown; one coincided with `history.add` taking 3751 ms on the UI thread.
- Move SQLite off the UI thread and enable WAL; drop the per-page `Stats()` queries.
- Fix the diagnostics flaws above.
- Verify the Windows App SDK 2.5.1 build: Build workflow green, then run it and hover tabs. Check the csproj comment and the `winui-fluent-ui` skill text match the version in use.
- WebView2 runtime version should be re-checked against NuGet when touching the csproj.
- Delete the obsolete exit dumps in C:\CrashDumps (keep neat.exe.1884.dmp as the reference for finding 2).

## Rules that would have saved time

1. State only what the evidence shows. Mark guesses as guesses, and say "not compiled / not run" for every unverified change.
2. Read the dump stack before forming a theory. The first theories here (many tabs, WebView2) were wrong.
3. Check which build and which Microsoft.UI.Xaml version produced a dump before comparing dumps.
4. When a diagnostic number looks odd, suspect the diagnostic first (see the known flaws).
5. Prefer removing an unsafe code path to hiding its symptom, but record that it is not a root-cause proof.
6. Re-fetch origin before branching, and confirm local and remote heads with `git ls-remote` instead of trusting hook warnings.
