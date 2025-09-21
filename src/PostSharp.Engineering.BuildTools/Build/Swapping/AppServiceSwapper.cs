// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;

namespace PostSharp.Engineering.BuildTools.Build.Swapping
{
    /// <summary>
    /// An implementation of <see cref="Swapper"/> for Azure AppService slots.
    /// </summary>
    [PublicAPI]
    public class AppServiceSwapper : Swapper
    {
        public string SubscriptionId { get; init; }

        public string ResourceGroupName { get; init; }

        public string AppServiceName { get; init; }

        public string SourceSlot { get; init; } = "staging";

        public string TargetSlot { get; init; } = "production";

        public AppServiceSwapper( string subscriptionId, string resourceGroupName, string appServiceName, string? sourceSlot = null, string? targetSlot = null )
        {
            this.SubscriptionId = subscriptionId;
            this.ResourceGroupName = resourceGroupName;
            this.AppServiceName = appServiceName;

            if ( sourceSlot != null )
            {
                this.SourceSlot = sourceSlot;
            }

            if ( targetSlot != null )
            {
                this.TargetSlot = targetSlot;
            }
        }

        protected override SuccessCode ExecuteCore( BuildContext context, SwapSettings settings, BuildConfigurationInfo configuration, BuildInfo buildInfo )
        {
            context.Console.WriteMessage( $"Swapping {this.SourceSlot} slot with {this.TargetSlot} slot of {this.AppServiceName} app service." );

            var args =
                $"webapp deployment slot swap --subscription {this.SubscriptionId} --resource-group {this.ResourceGroupName} --name {this.AppServiceName} --slot {this.SourceSlot} --target-slot {this.TargetSlot}";

            if ( settings.Dry )
            {
                context.Console.WriteImportantMessage( $"Dry run: {args}." );

                return SuccessCode.Success;
            }

            return AzHelper.Run( context.Console, args, settings.Dry ) ? SuccessCode.Success : SuccessCode.Error;
        }
    }
}