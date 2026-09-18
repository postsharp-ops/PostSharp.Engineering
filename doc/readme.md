# PostSharp.Engineering Design

Design documentation for the PostSharp.Engineering build SDK. Read in the following order:

1. [Vision](vision.md) - Project goals and principles
2. [Use Cases](use-cases.md) - Supported scenarios and workflows
3. [Build Flow](build-flow.md) - Build process diagrams
4. [Product Publishing](publish-flow.md) - Publishing workflow
5. [One-Click Publishing](publish-button.md) - TeamCity "Publish All" configuration
6. [Dependencies](dependencies.md) - Metalama dependency graph and management
7. [DockerBuild.ps1](dockerbuild.md) - Containerized builds and Claude sandboxing
8. [Docker-Based Tests](docker-tests.md) - Tests that each need a container of their own
9. [Scenario Solutions](scenario-solutions.md) - `ManyDotNetSolutions`, `ManyMSBuildSolutions` and `test.json`
10. [Opening a Version Line](open-version-line.md) - Creating a new `YYYY.N` of a product family, end to end
11. [Replacing a Build Configuration](custom-build-configuration.md) - `SnapshotDependencies` and `CustomBuildConfiguration`, for a product whose build consumes an earlier build configuration of the same product