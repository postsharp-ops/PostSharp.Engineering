[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)]
  [String]$apiKey
)

# Edit these versions.
$compilerVersion = "2023.0.109"
$frameworkVersion = "2023.0.114"
$backstageVersion = "2023.0.106"

dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Backstage $backstageVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Compiler.Sdk $compilerVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Extensions.Architecture $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Extensions.DependencyInjection $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Extensions.DependencyInjection.ServiceLocator $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Extensions.Metrics $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Extensions.Metrics.Redist $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Extensions.Multicast $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Framework $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Framework.Engine $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Framework.Redist $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Framework.Sdk $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Framework.Workspaces $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Framework.Introspection $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Framework.CompileTimeContracts $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.LinqPad $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Migration $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Testing.AspectTesting $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Testing.UnitTesting $frameworkVersion
dotnet nuget delete --non-interactive -s  https://www.nuget.org -k $apiKey Metalama.Tool $frameworkVersion
