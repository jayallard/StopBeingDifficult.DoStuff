# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All commands run from the repository root (`Sbd.DoStuff.slnx`).

```bash
dotnet build Sbd.DoStuff.slnx                                          # build everything
dotnet test tests/Sbd.DoStuff.UnitTests/Sbd.DoStuff.UnitTests.csproj   # run all tests
dotnet test --filter "FullyQualifiedName~TaskDefinitionResolverTests"  # run one test class
dotnet test --filter "DisplayName~Cycle_Throws"                        # run one test by name
dotnet run --project src/Sbd.DoStuff.WebApp                            # run the app (http://localhost:5096 by default, per launchSettings.json)
```

Package versions are centrally managed in `Directory.Packages.props` — add new dependencies there, not with inline versions in a `.csproj`.

### Tailwind CSS

`wwwroot/app.css` is generated from `Styles/app.css` by the standalone Tailwind CLI, invoked automatically as an MSBuild target (`TailwindBuild`, in `Sbd.DoStuff.WebApp.csproj`) before every build. The CLI binary itself is **not** checked in — download it to `.tools/tailwindcss.exe` (Windows) or `.tools/tailwindcss` (Linux/macOS) from the [Tailwind releases page](https://github.com/tailwindlabs/tailwindcss/releases/latest). If the binary isn't present, the build emits a warning and skips CSS regeneration rather than failing — the checked-in `wwwroot/app.css` is used as-is in that case.

## Architecture

The solution is split so that all OS/process/business logic lives in a plain class library, independent of the web framework:

- **`Sbd.DoStuff.Domain`** — no ASP.NET Core dependency. Contains the task model, cross-platform process execution, and the execution engine. Internal implementation types (e.g. `YamlTaskLibrary`, `TaskExecutionEngine`) are exercised directly in tests via `InternalsVisibleTo("Sbd.DoStuff.UnitTests")` in `AssemblyInfo.cs`.
- **`Sbd.DoStuff.WebApp`** — a Blazor **Server** app. The server process itself runs on the user's machine and has full OS access (spawns processes directly); the browser is purely the UI. There is no separate desktop shell, background agent, or extra SignalR hub — Blazor Server's own persistent circuit is the live-update transport.

Components use code-behind: markup and directives (`@page`, `@inject`, `@implements`, etc.) stay in the `.razor` file, and `@code` logic goes in a matching `ComponentName.razor.cs` partial class. No inline `@code` blocks in new or edited components.

### Task model: three-tier terminology

- **Task Library**: the full pool of `TaskDefinition`s, loaded from every `*.yaml` file (recursively) in the `TaskLibrary` subfolder of each directory listed under `FileDataSource:Directories` in `appsettings.json` (one file may contain one or many definitions — it's just deserialized as an array). Ids must be unique across all directories.
- **Task Definition**: one reusable, parameterized template for a unit of work (e.g. "Delete Folder" with a `FolderName` parameter). A definition can *inherit* another via `BaseTaskId`, pinning some of the base's parameter values (e.g. "Delete Temp Folder" = "Delete Folder" with `FolderName` fixed to `C:\temp`). A definition is either a **base** (`BaseTaskId` is null; sets `Type` and exactly one of `Command`/`ScriptPath`, etc. directly) or **derived** (`BaseTaskId` set; must leave `Type`/`Command`/`ScriptPath`/`WorkingDirectory`/`EnvironmentVariables`/`Parameters` null — `YamlTaskLibrary` enforces this at load time).
  - `ScriptPath` points at a `.ps1` file instead of an inline `Command`. A relative path is resolved against the `Assets` subfolder of each `FileDataSource:Directories` entry (first match wins); a rooted path is used as-is. Either way the file must exist at load time.
  - **PowerShell modules** are the preferred home for non-trivial task logic: each lives at `<assets dir>/Modules/<Name>/<Name>.psd1` + `.psm1` (e.g. `Data/Assets/Modules/Sbd.Git`), and the task's `Command` is just `Import-Module <Name>` plus a call to the matching function, so the same function can be used directly from a PowerShell session. Module functions must not call `exit` (it would close an interactive shell) — they write `:`/`!` output lines and set `$global:LASTEXITCODE` like a native command. Task commands should **not** end with `exit $LASTEXITCODE`: `WindowsProcessRunner`'s wrapper already exits with a nonzero `$LASTEXITCODE`, or 1 if any error record was written, which also catches a failed `Import-Module` (an explicit `exit $LASTEXITCODE` would turn that into exit 0).
  - **Parameter types** (`type:` on a parameter) pick the UI input: text (default), multilineText, filePath, boolean, and `selectList` — a single-select dropdown over the parameter's `options` list, in the order written. Its first option is the default when no `defaultValue` is given, and a supplied value outside `options` throws `InvalidParameterOptionException`.
  - `CanOverride` maps a parameter name to `false` to forbid Task Lists from overriding it. It is inherited down the chain and sticky — once any level sets `false`, no more-derived level can re-enable it.
- **Task List**: a curated, categorized collection loaded from every `*.yaml` file (recursively) in the `TaskLists` subfolder of each directory listed under `FileDataSource:Directories` (one file per list). Each entry references a Task Definition by id, assigns it one or more dot-notation category paths (e.g. `"cleanup.temp"` — split into a tree for the UI, and the same entry can appear under multiple paths), and may supply parameter values. An entry with a `disabledReason` is disabled: it still displays (with the reason), but looks greyed out and its Run button is disabled.

### Parameter resolution pipeline

This is the trickiest part of the domain and spans several files:

1. `TaskDefinitionResolver.Resolve` walks a definition's `BaseTaskId` chain up to its root, accumulating pinned parameter values (most-derived wins on conflict) and detecting cycles/missing bases (`TaskDefinitionCycleException`). Produces an `EffectiveTaskDefinition` — Type/Command/etc. from the root, but the **full, unreduced** parameter list (nothing is removed for being pinned).
2. `TaskParameterResolver.Resolve` resolves each parameter's final value with precedence **Task-List-supplied value > inherited pinned value > declared default > error if required**. A Task List entry can override a value even if it was pinned by the definition's own inheritance chain, **unless** the chain marked that parameter non-overridable via `CanOverride` — then supplying a value throws `ParameterNotOverridableException` rather than silently being ignored.
3. `TaskFactory` builds the runnable `ITask`: for `WorkingDirectory`/`EnvironmentVariables`, `ParameterTemplate.Substitute` does `{ParamName}` → value substitution. For `Command`, parameters are **not** substituted into the script text — instead `TaskFactory` prepends one `$ParamName = '...'` assignment per parameter (value single-quote-escaped for PowerShell), wrapped in `# --- Parameters ---` / `# --- End Parameters ---` comment markers, before the task-authored script, so the script references `$ParamName` directly rather than embedding raw values inline. For `ScriptPath`, the script is invoked with the call operator (`& 'path' -ParamName 'value' ...`), so the `.ps1` must declare a matching `param(...)` block.
4. `CategoryTreeBuilder.Build` ties it together per Task List: for each entry, resolves definition → effective definition → final parameter values, and files the result (`TaskListEntryView`) into the category tree used by the UI.

`TaskListValidationHostedService` re-runs steps 1–2 for every definition and every list entry at app **startup**, so a broken base chain or a missing required parameter fails fast with a clear message instead of surfacing when a user clicks "Run" in the browser.

### Execution & live output

`ITask.RunAsync(ITaskExecutionContext, CancellationToken)` is the pluggable task contract; `ShellCommandTask` (type `"powershell"`) is the only built-in implementation, running a command via `IProcessRunner`. Cross-platform differences are isolated to `WindowsProcessRunner` (`-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand`, Base64/UTF-16LE-encoded to avoid quoting issues with multiline scripts; the process-scoped `Bypass` lets asset `.ps1`/`.psm1` files load under 5.1's default `Restricted` policy) and `UnixProcessRunner` (`/bin/sh -c`), both sharing process-launch/kill logic in `ProcessRunnerBase`. `ProcessRunnerBase` prepends each assets directory's `Modules` subfolder to the child process's `PSModulePath` (a task's own `PSModulePath` environment variable still wins), which is what lets task commands `Import-Module` asset modules by name. `WindowsProcessRunner` runs `pwsh.exe` (PowerShell 7+) for task type `powershell` and `powershell.exe` (Windows PowerShell 5.1) for task type `windows-powershell` (for tasks that depend on 5.1-only modules or behavior). There is no fallback between them: if the requested executable isn't on `PATH`, it throws `PowerShellNotFoundException` and the run fails. `UnixProcessRunner` throws the same for `windows-powershell`.

`ITaskExecutionContext` has three ways for a task to report back: `Report` (raw stdout/stderr lines, wired up automatically by `ShellCommandTask`), `Log` (task-authored messages), and `SetResult(int resultCode, string message)` (a structured outcome, independent of whether the run is considered Failed by the engine). All three funnel through one internal `Append` in `TaskExecutionContext` that appends to the `TaskRun.OutputLines` timeline and raises `ITaskExecutionEngine.RunChanged`.

The Blazor UI subscribes to `RunChanged` and calls `InvokeAsync(StateHasChanged)` (required since the event fires from the background execution thread) — see `TaskRunView.razor`. `ITaskExecutionEngine`, `ITaskRunStore`, `ITaskLibrary`, and `ITaskListRepository` are all registered as singletons (`AddDoStuffDomain` in `ServiceCollectionExtensions.cs`), since the app models a single local user's process.

Output-line conventions in `TaskRunView`: a line starting with `!` is an error message (shown in red, sigil stripped); lines starting with `:` or `!` are also collected into the run's message summary.

Run history storage is chosen by the `TaskRunStore` setting: `"memory"` (`InMemoryTaskRunStore`, lost on restart; the fallback if unset) or `"yaml"` (`YamlTaskRunStore`, persisted to `taskrunstore:yaml:file`, default `~/.sbd/TaskRuns.yaml`; `~/` is expanded and the directory is created on first save). The checked-in `appsettings.json` uses `"yaml"`.

### Configuration & per-user data

`AddDoStuffDomain` reads one directory list, `FileDataSource:Directories`, required to be non-empty. For each directory it looks for optional `TaskLibrary`, `TaskLists` and `Assets` subfolders and loads whatever exists; missing ones are skipped silently. Paths starting with `~/` expand to the user profile; relative paths resolve against the app's working directory. The default pairs the repo's sample `Data` folder with an external per-user folder (not in the repo; copy it by hand when moving machines). New public task lists are saved under the first source's `TaskLists`, private ones under the last.

Each directory is also registered as an `IDataSource` (`FileDataSource`), which reports the library files, lists and assets it contributed; the `/configuration` page renders that inventory. A future non-file store (e.g. a database) would implement `IDataSource` to appear there.
