// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using System;

namespace PostSharp.Engineering.BuildTools.Build.Swapping;

/// <summary>
/// Swaps two deployment slots.
/// </summary>
[UsedImplicitly]
internal class SwapCommand : BaseCommand<SwapSettings>
{
    protected override bool ExecuteCore( BuildContext context, SwapSettings settings ) => Execute( context, settings );

    public static bool ExecuteAfterPublishing( BuildContext context, PublishSettings publishSettings )
    {
        var swapSettings = new SwapSettings() { BuildConfiguration = publishSettings.BuildConfiguration, Dry = publishSettings.Dry };

        return Execute( context, swapSettings );
    }

    private static bool Execute( BuildContext context, SwapSettings settings )
    {
        var product = context.Product;
        var configuration = product.Configurations.GetValue( settings.BuildConfiguration );

        var buildArguments = BuildArguments.ReadFromArtifactManifest( context, settings.BuildConfiguration );

        var directories = product.GetArtifactsAbsoluteDirectories( context, settings.BuildConfiguration );

        var success = true;

        if ( configuration.Swappers != null )
        {
            foreach ( var swapper in configuration.Swappers )
            {
                if ( !swapper.IsEnabled( buildArguments ) )
                {
                    swapper.WarnSkipped( context, buildArguments );

                    // The source slot keeps the deployed pre-release, but there is no reason to keep it running.
                    if ( !CleanUp( context, settings, configuration, buildArguments, swapper, ref success ) )
                    {
                        return false;
                    }

                    continue;
                }

                switch ( swapper.Execute( context, settings, configuration, buildArguments ) )
                {
                    case SuccessCode.Success:
                        var isReverted = false;

                        foreach ( var tester in swapper.Testers )
                        {
                            var testerResult = tester.Execute( context, directories.Private, buildArguments, settings.Dry );

                            if ( testerResult == SuccessCode.Fatal )
                            {
                                return false;
                            }

                            if ( testerResult == SuccessCode.Success )
                            {
                                continue;
                            }

                            // If any of the testers fail during swap, we do swap again to get the slots to their original state.
                            context.Console.WriteError( "Tester failed after swapping staging and production slots. Attempting to revert the swap." );

                            switch ( swapper.Execute( context, settings, configuration, buildArguments ) )
                            {
                                case SuccessCode.Success:
                                    context.Console.WriteMessage( "Successfully reverted swap." );

                                    break;

                                case SuccessCode.Error:
                                    context.Console.WriteError( "Failed to revert swap." );

                                    break;

                                case SuccessCode.Fatal:
                                    return false;

                                default:
                                    throw new NotImplementedException();
                            }

                            // The remaining testers would run against the reverted deployment.
                            success = false;
                            isReverted = true;

                            break;
                        }

                        // After a revert, we leave the source slot running so that the failed deployment can be
                        // investigated and swapped back manually.
                        if ( !isReverted && !CleanUp( context, settings, configuration, buildArguments, swapper, ref success ) )
                        {
                            return false;
                        }

                        break;

                    case SuccessCode.Error:
                        success = false;

                        break;

                    case SuccessCode.Fatal:
                        return false;

                    default:
                        throw new NotImplementedException();
                }
            }
        }

        return success;
    }

    /// <summary>
    /// Returns <c>false</c> when the whole command must be aborted.
    /// </summary>
    private static bool CleanUp(
        BuildContext context,
        SwapSettings settings,
        BuildConfigurationInfo configuration,
        BuildArguments buildArguments,
        Swapper swapper,
        ref bool success )
    {
        switch ( swapper.CleanUpAfterSwap( context, settings, configuration, buildArguments ) )
        {
            case SuccessCode.Success:
                return true;

            case SuccessCode.Error:
                context.Console.WriteError( $"'{swapper.GetType().Name}' failed to clean up after the swap." );
                success = false;

                return true;

            case SuccessCode.Fatal:
                return false;

            default:
                throw new NotImplementedException();
        }
    }
}