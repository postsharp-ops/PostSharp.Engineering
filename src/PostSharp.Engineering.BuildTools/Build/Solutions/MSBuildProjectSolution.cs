// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Globalization;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// An implementation of <see cref="TestableSolution"/> that builds a single project with the desktop
/// (.NET Framework) <c>MSBuild.exe</c> shipped with Visual Studio, as opposed to <see cref="DotNetSolution"/>,
/// which builds with <c>dotnet</c>. This is required to cover behaviors that only manifest under the
/// .NET Framework-hosted compiler.
/// </summary>
/// <remarks>
/// Restore is always performed by <c>dotnet restore</c>, because desktop MSBuild cannot reliably restore .NET SDK
/// projects, and the build then runs with <c>-p:RestorePackages=false</c> so that the restore graph is not
/// re-evaluated by a different engine.
/// </remarks>
[PublicAPI]
public class MSBuildProjectSolution : TestableSolution
{
    public MSBuildProjectSolution( string solutionPath ) : base( solutionPath ) { }

    /// <summary>
    /// Gets the full path of <c>MSBuild.exe</c>. When not set, MSBuild is located using <c>vswhere.exe</c>, or using
    /// the <c>ENG_MSBUILD_EXE</c> environment variable.
    /// </summary>
    public string? MSBuildExePath { get; init; }

    /// <summary>
    /// Gets the default MSBuild target. It can be overridden per scenario, and per matrix entry, by the
    /// <see cref="TestOptions.Target"/> property of <c>test.json</c>.
    /// </summary>
    public string DefaultTarget { get; init; } = "Build";

    public override bool Pack( BuildContext context, BuildSettings settings ) => throw new NotSupportedException();

    public override bool Restore( BuildContext context, BuildSettings settings )
        => DotNetHelper.Run(
            context,
            settings,
            this.GetFinalSolutionPath( context ),
            "restore",
            "--no-cache",
            false,
            this.CreateInvocationOptions() );

    protected override bool Invoke(
        BuildContext context,
        BuildSettings settings,
        SolutionCommand command,
        EffectiveTestOptions options,
        string logName,
        bool captureOutput,
        out int exitCode,
        out string output )
    {
        exitCode = 0;
        output = "";

        var msbuildPath = MSBuildHelper.FindMSBuildExe( context, explicitPath: this.MSBuildExePath );

        if ( msbuildPath == null )
        {
            // FindMSBuildExe has written an actionable error. We must not silently fall back to `dotnet`, which would
            // turn this scenario into a permanently green test.
            return false;
        }

        var target = options.Target ?? this.DefaultTarget;
        var project = this.GetFinalSolutionPath( context );
        var logsDirectory = Path.Combine( context.RepoDirectory, context.Product.LogsDirectory );

        Directory.CreateDirectory( logsDirectory );

        var binaryLogFilePath = Path.Combine( logsDirectory, $"{logName}.{target}.binlog" );
        var textLogFilePath = Path.Combine( logsDirectory, $"{logName}.{target}.log" );

        var argsBuilder = new System.Text.StringBuilder();

        argsBuilder.Append(
            CultureInfo.InvariantCulture,
            $"\"{project}\" -t:{target} -p:Configuration={context.Product.DependencyDefinition.MSBuildConfiguration[settings.BuildConfiguration]}" );

        // The restore has been performed by `dotnet restore`, so the restore graph must not be re-evaluated here.
        argsBuilder.Append( " -p:RestorePackages=false" );

        argsBuilder.Append( CultureInfo.InvariantCulture, $" -v:{settings.Verbosity.ToAlias()} -NoLogo" );
        argsBuilder.Append( settings.NoConcurrency ? " -m:1" : " -m" );
        argsBuilder.Append( CultureInfo.InvariantCulture, $" -bl:\"{binaryLogFilePath}\"" );

        // A `minimal` file log still carries all warnings and errors, which is all the assertions need, and it keeps
        // the log small enough to be attached to a CI build.
        argsBuilder.Append( CultureInfo.InvariantCulture, $" -flp:LogFile=\"{textLogFilePath}\";Verbosity=minimal" );

        foreach ( var property in settings.Properties )
        {
            argsBuilder.Append( CultureInfo.InvariantCulture, $" -p:{property.Key}={property.Value}" );
        }

        if ( context.IsContinuousIntegrationBuild )
        {
            argsBuilder.Append( " -p:ContinuousIntegrationBuild=True" );
        }

        if ( settings.NoSign )
        {
            argsBuilder.Append( " -p:DoNotSign=True" );
        }

        var invocationOptions = this.CreateInvocationOptions()
            .WithEnvironmentVariables( TeamCityHelper.GetSimulatedContinuousIntegrationEnvironmentVariables( settings ) );

        var arguments = argsBuilder.ToString();
        var workingDirectory = context.GetWorkingDirectory( project );

        if ( !captureOutput )
        {
            return ToolInvocationHelper.InvokeTool( context.Console, msbuildPath, arguments, workingDirectory, invocationOptions );
        }

        return ToolInvocationHelper.InvokeTool(
            context.Console,
            msbuildPath,
            arguments,
            workingDirectory,
            out exitCode,
            out output,
            invocationOptions );
    }
}
