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
                switch ( swapper.Execute( context, settings, configuration, buildArguments ) )
                {
                    case SuccessCode.Success:
                        foreach ( var tester in swapper.Testers )
                        {
                            switch ( tester.Execute( context, directories.Private, buildArguments, settings.Dry ) )
                            {
                                case SuccessCode.Success:
                                    break;

                                // If any of the testers fail during swap, we do swap again to get the slots to their original state.
                                case SuccessCode.Error:
                                    context.Console.WriteError( $"Tester failed after swapping staging and production slots. Attempting to revert the swap." );

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
                                    }

                                    success = false;

                                    break;

                                case SuccessCode.Fatal:
                                    return false;

                                default:
                                    throw new NotImplementedException();
                            }
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
}