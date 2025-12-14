# Metalama Dependency Graph (2026.0)

This diagram shows the build dependencies between Metalama repositories. Arrows point from dependent to dependency (A → B means A depends on B).

```mermaid
graph BT
    subgraph Core["Core Components"]
        PostSharp.Engineering["PostSharp.Engineering"]
        Metalama.Compiler["Metalama.Compiler"]
        Metalama["Metalama"]
    end

    subgraph Premium["Premium & Extensions"]
        Metalama.Premium["Metalama.Premium"]
        Metalama.Vsx["Metalama.Vsx"]
    end

    subgraph Content["Samples & Documentation"]
        Metalama.Samples["Metalama.Samples"]
        Metalama.Documentation["Metalama.Documentation"]
        Metalama.Community["Metalama.Community"]
    end

    subgraph Testing["Test Projects"]
        Metalama.Tests.NopCommerce["Metalama.Tests.NopCommerce"]
        Metalama.Tests.DotNetSdk["Metalama.Tests.DotNetSdk"]
        Metalama.Performance["Metalama.Performance"]
    end

    %% Core dependencies
    Metalama.Compiler --> PostSharp.Engineering
    Metalama --> PostSharp.Engineering
    Metalama --> Metalama.Compiler

    %% Premium dependencies
    Metalama.Premium --> PostSharp.Engineering
    Metalama.Premium --> Metalama
    Metalama.Vsx --> PostSharp.Engineering
    Metalama.Vsx --> Metalama

    %% Samples & Docs dependencies
    Metalama.Samples --> PostSharp.Engineering
    Metalama.Samples --> Metalama.Premium
    Metalama.Community --> PostSharp.Engineering
    Metalama.Community --> Metalama
    Metalama.Documentation --> PostSharp.Engineering
    Metalama.Documentation --> Metalama.Samples
    Metalama.Documentation -.-> Metalama.Community

    %% Test project dependencies
    Metalama.Tests.NopCommerce --> PostSharp.Engineering
    Metalama.Tests.NopCommerce --> Metalama
    Metalama.Tests.DotNetSdk --> PostSharp.Engineering
    Metalama.Tests.DotNetSdk --> Metalama
    Metalama.Performance --> PostSharp.Engineering
    Metalama.Performance --> Metalama
```

## Dependency Sources

Dependencies can be resolved from three sources:

| Source | Description | Use Case |
|--------|-------------|----------|
| **Feed** | Published NuGet packages | Production builds, stable versions |
| **BuildServer** | TeamCity artifacts | CI/CD builds, latest compatible version |
| **Local** | Local repository clone | Local development, debugging |

## Managing Dependencies

```powershell
# List all dependencies
Build.ps1 dependencies list

# Use local build of a dependency
Build.ps1 dependencies set local Metalama

# Reset to default (feed/build server)
Build.ps1 dependencies reset --all

# Fetch latest from build server
Build.ps1 dependencies fetch

# Update to newest available version
Build.ps1 dependencies update
```