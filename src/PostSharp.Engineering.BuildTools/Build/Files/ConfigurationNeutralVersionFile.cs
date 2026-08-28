// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Evaluation;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Files;

/// <summary>
/// Reads and writes the <c>Versions.g.props</c> file.
/// </summary>
internal static class ConfigurationNeutralVersionFile
{
    private static string GetPath( BuildContext context )
    {
        var product = context.Product;

        return Path.Combine( context.RepoDirectory, product.EngineeringDirectory, "Versions.g.props" );
    }

    internal static BuildConfiguration? ReadDefaultConfiguration( BuildContext context )
    {
        var path = GetPath( context );

        if ( !File.Exists( path ) )
        {
            return null;
        }

        var versionFile = Project.FromFile( path, MSBuildLoadOptions.IgnoreImportErrors );

        var configuration = versionFile.Properties.SingleOrDefault( p => p.Name == "EngineeringConfiguration" )
            ?.UnevaluatedValue;

        if ( configuration == null )
        {
            return null;
        }

        // Note that the version suffix is not copied from the dependency, only the main version. 

        ProjectCollection.GlobalProjectCollection.UnloadAllProjects();

        return Enum.Parse<BuildConfiguration>( configuration );
    }

    private static string ConvertToWslPath( string path )
    {
        // Convert Windows path to WSL: C:\path -> /mnt/c/path
        if ( path is [_, ':', _, ..] && (path[2] == '\\' || path[2] == '/') )
        {
            var drive = char.ToLower( path[0], System.Globalization.CultureInfo.InvariantCulture );
            var remainder = path.Substring( 2 ).Replace( "\\", "/", StringComparison.Ordinal );

            return $"/mnt/{drive}{remainder}";
        }

        return path;
    }

    /// <summary>
    /// Returns the import file of a local PostSharp.Engineering dependency, or <c>null</c> when the dependency is not
    /// consumed from a local build. The product definition project is excluded from the generated version files, so
    /// this file is the only way an engineering override written by the <c>dependencies set local</c> command can
    /// reach it.
    /// </summary>
    private static string? GetLocalEngineeringImportFile( BuildContext context, DependenciesConfigurationFile? dependenciesConfigurationFile )
    {
        var dependencyName = DevelopmentDependencies.PostSharpEngineering.Name;

        if ( dependenciesConfigurationFile == null
             || !dependenciesConfigurationFile.Dependencies.TryGetValue( dependencyName, out var dependencySource )
             || dependencySource.SourceKind != DependencySourceKind.Local )
        {
            return null;
        }

        return Path.GetFullPath(
            Path.Combine( dependencySource.GetResolvedLocalPath( context, dependencyName ), dependencyName + ".Import.props" ) );
    }

    public static void Write(
        BuildContext context,
        CommonCommandSettings settings,
        BuildConfiguration buildConfiguration,
        DependenciesConfigurationFile? dependenciesConfigurationFile = null )
    {
        var configurationNeutralVersionsFilePath = GetPath( context );
        var configurationSpecificVersionFilePath = DependenciesConfigurationFile.GetPath( context, settings, buildConfiguration );

        context.Console.WriteMessage( $"Writing '{configurationNeutralVersionsFilePath}'." );

        // The product definition project ('Build*.csproj') is the project whose restore decides which version of
        // PostSharp.Engineering.BuildTools runs. It must therefore never see the generated version files: those pin the version
        // chosen by a previous run, which would make the pin its own input -- bumping the version in Directory.Packages.props, or
        // with 'dependencies update-eng', would have no effect until the generated files are deleted. Excluding the whole chain,
        // rather than just the PostSharpEngineeringVersion property, also keeps the version files of the dependencies out: those
        // carry the version of PostSharp.Engineering that the dependency was built with, and would win once the property below is
        // no longer assigned. The same 'Build' prefix already excludes these projects from the VerifyProductDependencies target.
        const string productDefinitionCondition = "$(MSBuildProjectName.StartsWith('Build'))";
        const string notProductDefinitionCondition = "!" + productDefinitionCondition;

        // The exclusion above keeps the pinned version away from the product definition project, but a local
        // PostSharp.Engineering dependency has to reach it, because that project is precisely the one whose restore
        // decides which build tool runs. Its import file assigns PostSharpEngineeringVersion and adds the local
        // artifacts to RestoreAdditionalProjectSources, so without it the 'dependencies set local' command reports an
        // override that has no effect and the product definition keeps running the version from the feed. The import
        // is restricted to the product definition project because the other projects already receive it through the
        // configuration-specific file.
        var localEngineeringImportFile = GetLocalEngineeringImportFile( context, dependenciesConfigurationFile );

        var localEngineeringImport = localEngineeringImportFile == null
            ? ""
            : $@"
    <!-- Local PostSharp.Engineering build, imported by the product definition project only. -->
    <Import Project=""{localEngineeringImportFile}"" Condition=""'$(DoNotLoadGeneratedVersionFiles)'!='True' AND {productDefinitionCondition} AND Exists('{localEngineeringImportFile}')""/>";

        string content;

        if ( context.Product.AddWslSupport )
        {
            var wslVersionFilePath = DependenciesConfigurationFile.GetWslPath( context, settings, buildConfiguration );

            // Transform WSL path to WSL format for use in the Import element
            var wslVersionFilePathInWslFormat = ConvertToWslPath( wslVersionFilePath );

            content = $@"
<!-- File generated by PostSharp.Engineering {VersionHelper.EngineeringVersion}, method {nameof(ConfigurationNeutralVersionFile)}.{nameof(Write)}. -->
<Project>
    <PropertyGroup>
        <EngineeringConfiguration>{buildConfiguration}</EngineeringConfiguration>
    </PropertyGroup>
    <!-- Load WSL version if running under Unix/WSL -->
    <Import Project=""{wslVersionFilePathInWslFormat}"" Condition=""'$(DoNotLoadGeneratedVersionFiles)'!='True' AND {notProductDefinitionCondition} AND '$([MSBuild]::IsOSPlatform(Linux))' == 'true' AND Exists('{wslVersionFilePathInWslFormat}')""/>
    <!-- Load Windows version if running under Windows -->
    <Import Project=""{configurationSpecificVersionFilePath}"" Condition=""'$(DoNotLoadGeneratedVersionFiles)'!='True' AND {notProductDefinitionCondition} AND '$([MSBuild]::IsOSPlatform(Windows))' == 'true' AND Exists('{configurationSpecificVersionFilePath}')""/>{localEngineeringImport}
</Project>
";
        }
        else
        {
            content = $@"
<!-- File generated by PostSharp.Engineering {VersionHelper.EngineeringVersion}, method {nameof(ConfigurationNeutralVersionFile)}.{nameof(Write)}. -->
<Project>
    <PropertyGroup>
        <EngineeringConfiguration>{buildConfiguration}</EngineeringConfiguration>
    </PropertyGroup>
    <Import Project=""{configurationSpecificVersionFilePath}"" Condition=""'$(DoNotLoadGeneratedVersionFiles)'!='True' AND {notProductDefinitionCondition} AND Exists('{configurationSpecificVersionFilePath}')""/>{localEngineeringImport}
</Project>
";
        }

        TextFileHelper.WriteIfDifferent(
            configurationNeutralVersionsFilePath,
            content,
            context );
    }
}