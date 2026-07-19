# Scenario Solutions

`ManyDotNetSolutions` and `ManyMSBuildSolutions` discover every buildable project under a directory and build each
of them independently, as a distinct *scenario*. They are used for standalone repro projects that must be compiled
in isolation from the rest of the repository.

Both derive from the abstract `ManySolutions`, which owns discovery, scheduling, restore and reporting. The only
difference between them is the build engine, i.e. the command line:

| Type | Engine | Scenario type |
| --- | --- | --- |
| `ManyDotNetSolutions` | `dotnet` (CoreCLR-hosted compiler) | `DotNetSolution` |
| `ManyMSBuildSolutions` | desktop `MSBuild.exe` (.NET Framework-hosted compiler) | `MSBuildProjectSolution` |

Switching a directory from one to the other is a one-word change:

```csharp
Solutions =
[
    new RoslynSolution(),
    new ManyMSBuildSolutions( "src/Metalama/tests/Standalone" ) { IsTestOnly = true }
],
```

## When to use `ManyMSBuildSolutions`

Roslyn resolves analyzer and source-generator dependencies by completely different mechanisms per host:
`AnalyzerAssemblyLoader.Core.cs` uses a per-directory `AssemblyLoadContext`, while
`AnalyzerAssemblyLoader.Desktop.cs` — which is `#if !NETCOREAPP`, so it is not even compiled into the `dotnet build`
path — uses Fusion and an AppDomain pre-scan. A scenario that only reproduces under the desktop compiler therefore
*cannot* fail under `ManyDotNetSolutions`, no matter whether the bug is present. Note that
`dotnet build -p:BuildWithNetFrameworkHostedCompiler=true` is not a substitute.

`ManyMSBuildSolutions` never falls back to `dotnet` when MSBuild is missing — a silent fallback would turn the
scenario into a permanently green test asserting nothing. It fails with an actionable error instead.

### MSBuild discovery

`MSBuild.exe` is located in this order:

1. The `MSBuildExePath` property of the solution type.
2. The `ENG_MSBUILD_EXE` environment variable.
3. `vswhere.exe`, i.e. `vswhere -products * -requires Microsoft.Component.MSBuild -latest -find MSBuild\**\Bin\MSBuild.exe`.
   The 64-bit sibling in `Bin\amd64` is preferred when it exists.
4. The Visual Studio setup API.

On non-Windows platforms the scenarios are skipped with an explicit log line — not silently passed and not
hard-failed — so that a cross-platform product definition stays valid.

### Restore

Desktop MSBuild cannot restore .NET SDK projects reliably, so restore always runs through `dotnet restore`. The
build then passes `-p:RestorePackages=false` so that the restore graph is not silently re-evaluated by a different
engine.

## `test.json`

A `test.json` file placed next to a scenario asserts on the *diagnostics* of the build, not just on its exit code.
This matters when the symptom is a warning: a build can fail for unrelated reasons, or emit the warning while still
succeeding, so the exit code alone is both too weak and too noisy.

The file is read by `TestableSolution`, the common base of `DotNetSolution` and `MSBuildProjectSolution`, so it
behaves identically under both engines.

```json
{
    "BuildOnly": true,
    "IgnoreExitCode": true,
    "ExpectedDiagnosticsRegexes": [ "LAMA0120" ],
    "FailOnUnexpectedDiagnostics": true
}
```

| Property | Meaning |
| --- | --- |
| `BuildOnly` | Skips the `test` command for this scenario. |
| `IgnoreExitCode` | Does not fail on a non-zero exit code. |
| `ExpectedDiagnosticsRegexes` | Each pattern must match at least one diagnostic. |
| `ForbiddenDiagnosticsRegexes` | No diagnostic may match any of these patterns. |
| `FailOnUnexpectedDiagnostics` | Fails on any diagnostic not matched by `ExpectedDiagnosticsRegexes`. |
| `ErrorRegexes` | Fails if the whole output matches, when the build otherwise succeeded. |
| `Target` | The MSBuild target. Honored by `ManyMSBuildSolutions` only. Defaults to `Build`. |
| `Properties` | MSBuild properties passed to every run. |
| `Matrix` | See below. |

Prefer `ForbiddenDiagnosticsRegexes` over `FailOnUnexpectedDiagnostics` to express "must not appear": the latter
also fires on incidental unrelated warnings.

### Property matrix

A scenario can declare that it must be built more than once with different properties, each run asserted
independently:

```json
{
    "BuildOnly": true,
    "Target": "Rebuild",
    "ForbiddenDiagnosticsRegexes": [ "CS8785", "TypeLoadException", "CS9248" ],
    "Matrix": [
        { "Properties": { "UseSharedCompilation": "true" } },
        { "Properties": { "UseSharedCompilation": "false" } }
    ]
}
```

Any property of a matrix entry that is left unset falls back to the value defined on the `test.json` itself. An
entry may also set `Name`, `Target`, `IgnoreExitCode`, `FailOnUnexpectedDiagnostics`, `ErrorRegexes`,
`ExpectedDiagnosticsRegexes` and `ForbiddenDiagnosticsRegexes`.

`UseSharedCompilation=true` versus `false` is a genuine pair of test cases rather than a redundant repetition: the
compilation runs in `VBCSCompiler.exe` versus a fresh `csc.exe`, and those two processes start with different sets
of already-loaded assemblies.

Use `"Target": "Rebuild"` when an earlier matrix entry — or a preceding `dotnet build` control run — would
otherwise leave up-to-date outputs that make an incremental build mask the failure.

### Logs

Each run writes a binary log and a `minimal`-verbosity text log to the product's logs directory, named per scenario
and per matrix entry, so that a CI failure is diagnosable without re-running locally.

## Isolation

Scenarios inherit the MSBuild state of the repository root. Neutralise it with local files next to the scenario:

- `Directory.Build.props` and `Directory.Build.targets`, both empty or minimal.
- `Directory.Packages.props` with `ManagePackageVersionsCentrally=false`. This one is easy to miss: Central Package
  Management walks up the directory tree independently of `Directory.Build.props`, and its absence produces a
  confusing `NU1008` at restore time.
