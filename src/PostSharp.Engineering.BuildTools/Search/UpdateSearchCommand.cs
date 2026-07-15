// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Search.Backends;
using PostSharp.Engineering.BuildTools.Search.Backends.Typesense;
using PostSharp.Engineering.BuildTools.Search.Updaters;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Search;

[UsedImplicitly]
internal class UpdateSearchCommand : BaseCommand<UpdateSearchCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, UpdateSearchCommandSettings settings )
        => ExecuteCoreAsync( context, settings ).GetAwaiter().GetResult();

    private static async Task<bool> ExecuteCoreAsync( BuildContext context, UpdateSearchCommandSettings settings )
    {
        var console = new ConsoleHelper();

        if ( settings.Debug )
        {
            console.WriteMessage( "Launching debugger." );
            Debugger.Launch();
        }

        CollectionUpdater updater;

        var searchExtensions = context.Product.Extensions.OfType<UpdateSearchProductExtension>().ToList();

        UpdateSearchProductExtension productExtension;

        if ( settings.Source != null )
        {
            productExtension = searchExtensions.SingleOrDefault( e => e.Source == settings.Source )
                               ?? throw new InvalidOperationException(
                                   $"No search collection with source '{settings.Source}' is defined. Available sources: {string.Join( ", ", searchExtensions.Select( e => e.Source ) )}." );
        }
        else if ( searchExtensions.Count == 1 )
        {
            productExtension = searchExtensions[0];
        }
        else
        {
            throw new InvalidOperationException(
                $"This product defines several search collections; specify which one to update as the [source] argument. Available sources: {string.Join( ", ", searchExtensions.Select( e => e.Source ) )}." );
        }

        // When the collection is set explicitly, we don't work with an alias.
        var alias = settings.Collection == null ? productExtension.Source : null;
        string targetCollection;

        // A full rebuild uses blue/green: it resets the inactive staging collection and then swaps the alias.
        // An incremental update writes in place to the live collection and does not reset or swap.
        bool doReset;
        bool doSwap;

        if ( settings.Dry )
        {
            updater = productExtension.CreateUpdater( new DrySearchBackend( console ) );
            targetCollection = "dry"; // Console backend doesn't work with collection names.
            doReset = true;
            doSwap = false;
        }
        else
        {
            var apiKey = Environment.GetEnvironmentVariable( EnvironmentVariableNames.TypeSenseApiKey );

            if ( apiKey == null )
            {
                console.WriteError( $"{EnvironmentVariableNames.TypeSenseApiKey} environment variable not set." );

                return false;
            }

            var uri = new Uri( productExtension.TypesenseUri );
            var backend = new TypesenseBackend( apiKey, uri.Host, uri.Port.ToString( CultureInfo.InvariantCulture ), uri.Scheme );
            updater = productExtension.CreateUpdater( backend );

            if ( settings.Incremental )
            {
                if ( alias == null )
                {
                    // Explicit collection (development): update it in place.
                    targetCollection = settings.Collection!;
                    doReset = false;
                    doSwap = false;
                }
                else
                {
                    var (production, _) = await GetTargetCollectionsForAliasAsync( backend, alias );

                    if ( production == null )
                    {
                        // Nothing live yet: fall back to a full build (reset staging, then swap the alias).
                        // The updater's incremental path treats an empty collection as "everything is new".
                        targetCollection = $"{alias}A";
                        console.WriteMessage(
                            $"No live '{alias}' collection found; performing a full build into '{targetCollection}' instead of an incremental update." );
                        doReset = true;
                        doSwap = true;
                    }
                    else
                    {
                        // Update the live collection in place.
                        targetCollection = production;
                        console.WriteMessage( $"Incrementally updating the live '{production}' collection (alias '{alias}')." );
                        doReset = false;
                        doSwap = false;
                    }
                }
            }
            else
            {
                var targetCollections = alias == null
                    ? (Production: (string?) null, Staging: settings.Collection!)
                    : await GetTargetCollectionsForAliasAsync( backend, alias );

                targetCollection = targetCollections.Staging;
                doReset = true;
                doSwap = true;

                console.WriteMessage( $"Resetting '{targetCollection}' collection." );
            }
        }

        if ( doReset )
        {
            await ResetCollectionAsync( updater, targetCollection );
        }

        var success = await updater.UpdateAsync( context, settings, targetCollection );

        if ( success && !settings.Dry && doSwap && alias != null )
        {
            console.WriteMessage( $"Swapping '{alias}' to '{targetCollection}' collection." );
            await updater.Backend.UpsertCollectionAliasAsync( alias, targetCollection );
        }

        if ( success )
        {
            console.WriteMessage( "Done." );
        }
        else
        {
            console.WriteError( "Failed. See the error messages above." );
        }

        return true;
    }

    private static async Task<(string? Production, string Staging)> GetTargetCollectionsForAliasAsync( SearchBackendBase search, string alias )
    {
        var aliasResponses = await search.RetrieveCollectionAliasesAsync();
        var aliasResponse = aliasResponses.SingleOrDefault( a => a.Name == alias );

        if ( aliasResponse == null )
        {
            return (null, $"{alias}A");
        }

        var productionTarget = aliasResponse.CollectionName;
        var match = Regex.Match( productionTarget, $"{alias}(?<code>[AB])" );

        if ( !match.Success )
        {
            throw new InvalidOperationException( $"Unexpected target collection \"{productionTarget}\" of alias \"{alias}\"." );
        }

        var productionTargetCode = match.Groups["code"].Value;
        var stagingTargetCode = productionTargetCode == "A" ? "B" : "A";
        var stagingTarget = $"{alias}{stagingTargetCode}";

        return (productionTarget, stagingTarget);
    }

    private static async Task ResetCollectionAsync( CollectionUpdater updater, string collection )
    {
        _ = await updater.Backend.TryDeleteCollectionAsync( collection );
        var schema = updater.CreateSchema( collection );
        await updater.Backend.CreateCollectionAsync( schema );
    }
}