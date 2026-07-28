// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Utilities
{
    /// <summary>
    /// Starts and stops Azure AppService sites and deployment slots.
    /// </summary>
    [PublicAPI]
    public static class AppServiceHelper
    {
        /// <summary>
        /// The name of the slot that Azure considers to be the site itself. The <c>--slot</c> argument must not be
        /// passed for this slot.
        /// </summary>
        public const string ProductionSlotName = "production";

        private static readonly TimeSpan _defaultWarmUpTimeout = TimeSpan.FromMinutes( 5 );

        private static readonly TimeSpan _warmUpRetryInterval = TimeSpan.FromSeconds( 5 );

        /// <summary>
        /// Starts a site or a deployment slot, then waits until it answers an HTTP request. A slot that fails to warm
        /// up within <paramref name="warmUpTimeout"/> is reported as a warning, not as an error.
        /// </summary>
        public static bool Start(
            BuildContext context,
            string subscriptionId,
            string resourceGroupName,
            string siteName,
            string? slotName,
            bool dry,
            TimeSpan? warmUpTimeout = null )
        {
            context.Console.WriteMessage( $"Starting {FormatSite( siteName, slotName )}." );

            if ( !AzHelper.Run( context, CreateArgs( "webapp start", subscriptionId, resourceGroupName, siteName, slotName ), dry ) )
            {
                return false;
            }

            if ( dry )
            {
                return true;
            }

            return WarmUp( context, subscriptionId, resourceGroupName, siteName, slotName, warmUpTimeout ?? _defaultWarmUpTimeout );
        }

        /// <summary>
        /// Stops a site or a deployment slot.
        /// </summary>
        public static bool Stop(
            BuildContext context,
            string subscriptionId,
            string resourceGroupName,
            string siteName,
            string? slotName,
            bool dry )
        {
            context.Console.WriteMessage( $"Stopping {FormatSite( siteName, slotName )}." );

            return AzHelper.Run( context, CreateArgs( "webapp stop", subscriptionId, resourceGroupName, siteName, slotName ), dry );
        }

        /// <summary>
        /// Swaps <paramref name="sourceSlot"/> into <paramref name="targetSlot"/>, which defaults to the production
        /// slot. Unlike the other operations here, this one names the target slot explicitly: <c>slot swap</c> takes
        /// <c>--target-slot production</c> literally, where <c>webapp start</c> and friends require its absence.
        /// </summary>
        public static bool Swap(
            BuildContext context,
            string subscriptionId,
            string resourceGroupName,
            string siteName,
            string sourceSlot,
            string? targetSlot,
            bool dry )
        {
            targetSlot ??= ProductionSlotName;

            context.Console.WriteMessage(
                $"Swapping the '{sourceSlot}' slot of the '{siteName}' app service into '{targetSlot}'." );

            var args = CreateSwapArgs( subscriptionId, resourceGroupName, siteName, sourceSlot, targetSlot );

            if ( dry )
            {
                context.Console.WriteImportantMessage( $"Dry run: {args}." );

                return true;
            }

            return AzHelper.Run( context, args, dry );
        }

        /// <summary>
        /// Requests the root of the site until it returns any HTTP response, so that the caller does not pay the cold
        /// start. Any response, including a server error, means that the worker is up.
        /// </summary>
        private static bool WarmUp(
            BuildContext context,
            string subscriptionId,
            string resourceGroupName,
            string siteName,
            string? slotName,
            TimeSpan timeout )
        {
            var queryArgs = CreateArgs( "webapp show", subscriptionId, resourceGroupName, siteName, slotName )
                            + " --query defaultHostName --output tsv";

            if ( !AzHelper.Query( context, queryArgs, false, out var hostName ) )
            {
                return false;
            }

            hostName = hostName.Trim();

            if ( string.IsNullOrEmpty( hostName ) )
            {
                context.Console.WriteError( $"Cannot determine the host name of {FormatSite( siteName, slotName )}." );

                return false;
            }

            var url = $"https://{hostName}/";
            context.Console.WriteMessage( $"Waiting for {url} to warm up." );

            using var httpClient = new HttpClient() { Timeout = _warmUpRetryInterval };
            var stopwatch = Stopwatch.StartNew();

            while ( true )
            {
                try
                {
                    var response = httpClient.GetAsync( url ).GetAwaiter().GetResult();
                    context.Console.WriteMessage( $"{url} answered {(int) response.StatusCode} after {stopwatch.Elapsed.TotalSeconds:F0} s." );

                    return true;
                }
                catch ( Exception e ) when ( e is HttpRequestException or TaskCanceledException )
                {
                    if ( stopwatch.Elapsed >= timeout )
                    {
                        // The site may still be usable, and the testers that run next give a better diagnostic than we
                        // could, so we do not fail the build here.
                        context.Console.WriteWarning(
                            $"{url} did not answer within {timeout.TotalSeconds:F0} s. Continuing anyway. The last error was: {e.Message}" );

                        return true;
                    }

                    Thread.Sleep( _warmUpRetryInterval );
                }
            }
        }

        /// <summary>
        /// Builds the arguments of a slot swap. Deliberately not <see cref="CreateArgs"/>: that one drops
        /// <c>--slot</c> for the production slot, because <c>webapp start</c> and <c>webapp stop</c> address the site
        /// itself by omission, whereas <c>slot swap</c> takes <c>--target-slot production</c> literally and is the one
        /// command that names it.
        /// </summary>
        internal static string CreateSwapArgs(
            string subscriptionId,
            string resourceGroupName,
            string siteName,
            string sourceSlot,
            string targetSlot )
            => $"webapp deployment slot swap --subscription {subscriptionId} --resource-group {resourceGroupName} "
               + $"--name {siteName} --slot {sourceSlot} --target-slot {targetSlot}";

        internal static string CreateArgs( string command, string subscriptionId, string resourceGroupName, string siteName, string? slotName )
        {
            var args = $"{command} --subscription {subscriptionId} --resource-group {resourceGroupName} --name {siteName}";

            if ( !IsProductionSlot( slotName ) )
            {
                args += $" --slot {slotName}";
            }

            return args;
        }

        /// <summary>
        /// Determines whether <paramref name="slotName"/> names the site itself rather than a deployment slot. Azure
        /// addresses it by the absence of <c>--slot</c>, and it is the one slot that cannot be swapped.
        /// </summary>
        public static bool IsProductionSlot( string? slotName )
            => string.IsNullOrEmpty( slotName ) || string.Equals( slotName, ProductionSlotName, StringComparison.OrdinalIgnoreCase );

        private static string FormatSite( string siteName, string? slotName )
            => IsProductionSlot( slotName ) ? $"the '{siteName}' app service" : $"the '{slotName}' slot of the '{siteName}' app service";
    }
}
