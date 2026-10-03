# Sbd.DoStuff

A small local web app for running curated, categorized lists of PowerShell tasks. It's a Blazor Server app that runs on your own machine: the server spawns processes directly, and the browser is only the UI. Task output streams live into the page.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json`)
- PowerShell 7 (`pwsh.exe`) is preferred on Windows. Windows PowerShell 5.1 is used if `pwsh` isn't on `PATH`, or for tasks that set `useWindowsPowerShell: true`. On Linux/macOS, commands run via `/bin/sh`.
- *(Optional)* the Tailwind CLI, only needed if you change styles. See below.

## Getting started

```bash
dotnet build Sbd.DoStuff.slnx
dotnet test tests/Sbd.DoStuff.UnitTests/Sbd.DoStuff.UnitTests.csproj
dotnet run --project src/Sbd.DoStuff.WebApp
```

The app listens on http://localhost:5096 by default (`Properties/launchSettings.json`).

## Tailwind CSS setup

`src/Sbd.DoStuff.WebApp/wwwroot/app.css` is generated from `Styles/app.css` by the standalone Tailwind CLI during every build. The generated file is checked in. If the CLI is missing, the build prints a warning and uses the checked-in CSS as-is. That's fine unless you're editing styles.

To regenerate CSS, download the standalone binary for your platform from the [Tailwind releases page](https://github.com/tailwindlabs/tailwindcss/releases/latest) and save it to the repo root as:

| Platform      | Path                          |
|---------------|-------------------------------|
| Windows       | `.tools/tailwindcss.exe`      |
| Linux / macOS | `.tools/tailwindcss` (`chmod +x`) |

`.tools/` is not checked in. The next `dotnet build` picks up the binary automatically.

## Where tasks come from

Tasks are defined in YAML and loaded at startup from the directories configured in `src/Sbd.DoStuff.WebApp/appsettings.json`:

| Setting                  | Contents                                   | Defaults                                         |
|--------------------------|--------------------------------------------|--------------------------------------------------|
| `TaskLibrary:Directories` | Task definitions (reusable templates)      | `Data/TaskLibrary`, `~/.sbd.dostuff/TaskLibrary` |
| `TaskLists:Directories`   | Task lists (categorized entries that reference definitions) | `Data/TaskLists`, `~/.sbd.dostuff/TaskLists` |
| `Artifacts:Directories`   | `.ps1` scripts referenced by a definition's `scriptPath`, and PowerShell modules under `Modules/` | `Data/Artifacts`, `~/.sbd.dostuff/Artifacts` |
| `TaskRunStore`            | `memory` or `yaml` run history             | `yaml` → `~/.sbd.dostuff/TaskRuns.yaml`          |

The `Data/...` folders in the repo hold sample data. Put your own tasks and lists under `~/.sbd.dostuff/`. Missing directories are skipped with a warning. That folder lives outside the repo, so **copy it yourself when moving to another machine**.

Broken definitions fail at startup with a clear error rather than when you click Run. Examples: a missing base task, an inheritance cycle, a missing required parameter, or a `scriptPath` that doesn't exist.

### Example

A task library file (any `*.yaml` under a library directory) holds an array of definitions:

```yaml
- id: delete-folder
  name: Delete Folder
  type: powershell
  command: 'Remove-Item -Recurse -Force "$FolderName"'
  parameters:
    - name: FolderName
      required: true
- id: delete-temp-folder          # inherits delete-folder, pinning a parameter
  name: Delete Temp Folder
  baseTaskId: delete-folder
  parameterValues:
    FolderName: 'C:\temp'
```

A task list file contains one list:

```yaml
id: my-list
name: My List
entries:
  - taskId: delete-folder
    categories: [cleanup.build-output]   # dot-notation → category tree
    parameterValues:
      FolderName: 'C:\repo\dist'
```

Parameter values are resolved in this order: task-list value, then inherited pinned value, then declared default. A definition can block task lists from overriding a parameter with `canOverride: { ParamName: false }`.

### PowerShell modules

Put real task logic in a PowerShell module rather than inline YAML, so you can also use it straight from a PowerShell session. Modules live under `<artifacts dir>/Modules/<Name>/`, and the app adds each of those `Modules` folders to `PSModulePath` for the processes it starts. A task then only imports the module and calls a function:

```yaml
- id: get-git-version
  name: Get Git CLI Version
  type: powershell
  parameters:
    - name: MinVersion
      defaultValue: 2.53.0
  command: |-
    Import-Module Sbd.Git
    Test-GitVersion -MinVersion $MinVersion
```

To use the same function yourself:

```powershell
Import-Module .\src\Sbd.DoStuff.WebApp\Data\Artifacts\Modules\Sbd.Git
Test-GitVersion -MinVersion 2.50
$LASTEXITCODE   # 0 = passed
```

Module functions report success or failure through `$LASTEXITCODE` instead of calling `exit`, so they won't close your shell. In the app, a nonzero `$LASTEXITCODE` (or any error, including a failed `Import-Module`) fails the run. The sample modules are `Sbd.Git`, `Sbd.WinGet`, and `Sbd.VsCode`.

The app starts PowerShell with `-ExecutionPolicy Bypass` for its own processes only, so these modules load even under Windows PowerShell's default `Restricted` policy. To import them in your own shell, your execution policy has to allow local scripts (e.g. `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`).

### Output conventions

In task output, start a line with `!` to flag it as an error (shown in red). Lines starting with `:` or `!` also appear in the run's message summary.

## Project layout

- `src/Sbd.DoStuff.Domain`: task model, parameter resolution, cross-platform process execution, execution engine. No ASP.NET Core dependency.
- `src/Sbd.DoStuff.WebApp`: Blazor Server UI and sample data.
- `tests/Sbd.DoStuff.UnitTests`: unit tests for the domain library.

See [CLAUDE.md](CLAUDE.md) for a deeper architecture walkthrough.

## License

[MIT](LICENSE)
