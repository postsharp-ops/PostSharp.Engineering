# DockerBuild.ps1 chained-image test suite

Integration tests for the **chained-Dockerfile resolver** in the generated `DockerBuild.ps1` (template:
`src/PostSharp.Engineering.BuildTools/Resources/DockerBuild.ps1`).

A chained image declares its parent with `ARG BASE_IMAGE=<parent>.Dockerfile` and builds `FROM ${BASE_IMAGE}`.
`DockerBuild.ps1` resolves the parent to its content-hash tag (building or pulling it first), folds the
parent's hash (and the OS) into the child's tag, injects `--build-arg BASE_IMAGE`, and builds the chain
parent-first. The image **name** is the Dockerfile **stem** (e.g. `myproduct-2025.1-build.Dockerfile` →
image `myproduct-2025.1-build`).

These tests run the **real** `DockerBuild.ps1` (template filled with a `dbtest` prefix) against tiny,
fast, chained **fixture** images, asserting on Docker state — no product build required.

## Running

```pwsh
pwsh tests/dockerbuild/Run-DockerBuildTests.ps1
# -KeepSandbox   keep the temp sandbox and images for debugging
```

This suite is **not** part of `dotnet test` — it needs a running Docker engine.

## Requirements

- A running Docker engine. The suite auto-detects the container OS:
  - **Windows-container mode** → `fixtures/windows/` (nanoserver).
  - **Linux-container mode** (e.g. Docker Desktop with the WSL2 backend, or a Linux docker context) →
    `fixtures/linux/` (alpine).
- PowerShell 7.5+ (`pwsh`).

## What is covered

| # | Case | How it is asserted |
|---|------|--------------------|
| 1 | Chain build order (parent-first) | After `-BuildImage`, all ancestor images exist |
| 2 | Content-hash caching | A second identical build leaves image IDs unchanged |
| 3 | Base-hash fold (parent change cascades) | Editing the root Dockerfile produces new tags for root + every descendant |
| 4 | Line-ending invariance | Rewriting a Dockerfile CRLF↔LF leaves the tag unchanged |
| 7 | Per-image build context | Each image carries only its own `docker-context/<stem>` sentinel |
| 8 | Registry push/pull | Build+push to the **configured** registry, drop local images, rebuild → pulled (skipped if `$env:DOCKER_REGISTRY` is unset) |
| 10 | Leaf selection | default builds the `build` leaf; `-Claude` builds the `claude` leaf |

## Out of scope here (covered by real product builds)

| # | Case | Why |
|---|------|-----|
| 5 | OS build-arg fold (ltsc2025 vs ltsc2022) | needs two Windows host editions; the `WINDOWS_VERSION` arg + hash fold are exercised, not asserted |
| 6 | Boot image + MOUNTPOINTS creation | needs `docker run` of a PowerShell-7 image |
| 9 | Runtime env/init delivery | needs `docker run` of a PowerShell-7 image |

The registry case (8) uses the **configured** registry from `$env:DOCKER_REGISTRY` (with optional
`$env:DOCKER_USERNAME` / `$env:DOCKER_PASSWORD` for auth) — the suite does **not** deploy its own registry.
It is **skipped** when `$env:DOCKER_REGISTRY` is unset. Pushed test images use the `dbtest-` prefix and tiny
content-hash tags; they are not deleted from the remote registry by the suite.

### Running in WSL (Linux-container mode)

WSL does not inherit Windows environment variables, so forward the Docker settings via `WSLENV` before
invoking the suite (otherwise the registry case is skipped):

```pwsh
$env:WSLENV = 'DOCKER_REGISTRY:DOCKER_USERNAME:DOCKER_PASSWORD'
wsl -e pwsh -NoProfile -File /mnt/c/src/PostSharp.Engineering/tests/dockerbuild/Run-DockerBuildTests.ps1
```

The WSL Docker engine must also be able to reach and authenticate to the registry (trust its certificate).

## Fixtures

`fixtures/{windows,linux}/` each contain a three-image chain plus per-image build contexts:

```
docker/vs.Dockerfile               # chain ROOT  (FROM the external base; WINDOWS_VERSION on Windows)
docker/build.Dockerfile            # FROM vs     (ARG BASE_IMAGE=vs.Dockerfile)
docker/claude.Dockerfile           # FROM build  (ARG BASE_IMAGE=build.Dockerfile)  -- the leaf
docker-context/vs/sentinel.txt          # "context-vs"     (unique per layer -> proves context isolation)
docker-context/build/sentinel.txt       # "context-build"
docker-context/claude/sentinel.txt      # "context-claude"
```

Dockerfile names are **prefix-free** (just the layer). The runner uses a `dbtest` image prefix, so the
resolved image names are `dbtest-vs`, `dbtest-build`, `dbtest-claude` (the prefix lives in the tag, not the
file name). The runner stages these into a temporary `eng/`-shaped sandbox and drives
`DockerBuild.ps1 -BuildImage` against them. Everything (images + sandbox) is cleaned up on exit unless
`-KeepSandbox` is given.
