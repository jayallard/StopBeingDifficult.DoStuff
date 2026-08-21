# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All commands run from the repository root (`Sbd.DoStuff.slnx`).

```bash
dotnet build Sbd.DoStuff.slnx                                          # build everything
dotnet test tests/Sbd.DoStuff.UnitTests/Sbd.DoStuff.UnitTests.csproj   # run all tests
dotnet test --filter "FullyQualifiedName~TaskDefinitionResolverTests"  # run one test class
dotnet test --filter "DisplayName~Cycle_Throws"                        # run one test by name
dotnet run --project src/Sbd.DoStuff.WebApp                            # run the app (http://localhost:5299 by default)
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

- **Task Library**: the full pool of `TaskDefinition`s, loaded from every `*.yaml` file in `Data/TaskLibrary/` (one file may contain one or many definitions — it's just deserialized as an array).
- **Task Definition**: one reusable, parameterized template for a unit of work (e.g. "Delete Folder" with a `FolderName` parameter). A definition can *inherit* another via `BaseTaskId`, pinning some of the base's parameter values (e.g. "Delete Temp Folder" = "Delete Folder" with `FolderName` fixed to `C:\temp`). A definition is either a **base** (`BaseTaskId` is null; sets `Type`/`Command`/etc. directly) or **derived** (`BaseTaskId` set; must leave `Type`/`Command`/`WorkingDirectory`/`EnvironmentVariables`/`Parameters` null — `YamlTaskLibrary` enforces this at load time).
- **Task List**: a curated, categorized collection loaded from `Data/TaskLists/` (one file per list). Each entry references a Task Definition by id, assigns it one or more dot-notation category paths (e.g. `"cleanup.temp"` — split into a tree for the UI, and the same entry can appear under multiple paths), and may supply parameter values.

### Parameter resolution pipeline

This is the trickiest part of the domain and spans several files:

1. `TaskDefinitionResolver.Resolve` walks a definition's `BaseTaskId` chain up to its root, accumulating pinned parameter values (most-derived wins on conflict) and detecting cycles/missing bases (`TaskDefinitionCycleException`). Produces an `EffectiveTaskDefinition` — Type/Command/etc. from the root, but the **full, unreduced** parameter list (nothing is removed for being pinned).
2. `TaskParameterResolver.Resolve` resolves each parameter's final value with precedence **Task-List-supplied value > inherited pinned value > declared default > error if required**. A Task List entry can override a value even if it was pinned by the definition's own inheritance chain — nothing is ever "locked."
3. `ParameterTemplate.Substitute` does the actual `{ParamName}` → value substitution into `Command`/`WorkingDirectory`/`EnvironmentVariables`, done by `TaskFactory` when building the runnable `ITask`.
4. `CategoryTreeBuilder.Build` ties it together per Task List: for each entry, resolves definition → effective definition → final parameter values, and files the result (`TaskListEntryView`) into the category tree used by the UI.

`TaskListValidationHostedService` re-runs steps 1–2 for every definition and every list entry at app **startup**, so a broken base chain or a missing required parameter fails fast with a clear message instead of surfacing when a user clicks "Run" in the browser.

### Execution & live output

`ITask.RunAsync(ITaskExecutionContext, CancellationToken)` is the pluggable task contract; `ShellCommandTask` (type `"powershell"`) is the only built-in implementation, running a command via `IProcessRunner`. Cross-platform differences are isolated to `WindowsProcessRunner` (`powershell.exe -NoProfile -NonInteractive -EncodedCommand`, Base64/UTF-16LE-encoded to avoid quoting issues with multiline scripts) and `UnixProcessRunner` (`/bin/sh -c`), both sharing process-launch/kill logic in `ProcessRunnerBase`.

`ITaskExecutionContext` has three ways for a task to report back: `Report` (raw stdout/stderr lines, wired up automatically by `ShellCommandTask`), `Log` (task-authored messages), and `SetResult(int resultCode, string message)` (a structured outcome, independent of whether the run is considered Failed by the engine). All three funnel through one internal `Append` in `TaskExecutionContext` that appends to the `TaskRun.OutputLines` timeline and raises `ITaskExecutionEngine.RunChanged`.

The Blazor UI subscribes to `RunChanged` and calls `InvokeAsync(StateHasChanged)` (required since the event fires from the background execution thread) — see `TaskRunView.razor`. `ITaskExecutionEngine`, `ITaskRunStore`, `ITaskLibrary`, and `ITaskListRepository` are all registered as singletons (`AddDoStuffDomain` in `ServiceCollectionExtensions.cs`), since the app models a single local user's process. Run history is in-memory only and does not survive a restart.
