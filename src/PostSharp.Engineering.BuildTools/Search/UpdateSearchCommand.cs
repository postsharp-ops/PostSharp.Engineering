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
public class UpdateSearchCommand : BaseCommand<UpdateSearchCommandSettings>
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

        var productExtension = context.Product.Extensions.OfType<UpdateSearchProductExtension>().Single();

        // When the collection is set explicitly, we don't work with an alias.
        var alias = settings.Collection == null ? productExtension.Source : null;
        string targetCollection;
        (string? Production, string Staging) targetCollections;

        if ( settings.Dry )
        {
            updater = productExtension.CreateUpdater( new DrySearchBackend( console ) );
            targetCollection = "dry"; // Console backend doesn't work with collection names.
            targetCollections = (null, targetCollection);
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

            targetCollections = alias == null
                ? (null, settings.Collection!)
                : await GetTargetCollectionsForAliasAsync( backend, alias );

            targetCollection = targetCollections.Staging;

            console.WriteMessage( $"Resetting '{targetCollection}' collection." );
        }

        await ResetCollectionAsync( updater, targetCollection );

        var success = await updater.UpdateAsync( context, settings, targetCollection );

        if ( success && !settings.Dry && alias != null )
        {
            var sourceCollectionDescription = targetCollections.Production == null
                ? "none"
                : $"'{targetCollections.Production}' collection";

            console.WriteMessage( $"Swapping '{alias}' from {sourceCollectionDescription} to '{targetCollection}' collection." );
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