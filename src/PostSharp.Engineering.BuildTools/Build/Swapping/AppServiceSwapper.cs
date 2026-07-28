// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Docker;
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

        public string TargetSlot { get; init; } = AppServiceHelper.ProductionSlotName;

        /// <summary>
        /// When set to <c>true</c>, the default, <see cref="SourceSlot"/> is started before the swap. Azure aborts a
        /// swap whose source slot does not answer an HTTP request, and the source slot is typically stopped between
        /// deployments.
        /// </summary>
        public bool StartSourceSlotBeforeSwap { get; init; } = true;

        /// <summary>
        /// When set to <c>true</c>, the default, <see cref="SourceSlot"/> is stopped after a successful swap. After the
        /// swap, the source slot runs the application that was previously in production, which we do not want to keep
        /// running.
        /// </summary>
        public bool StopSourceSlotAfterSwap { get; init; } = true;

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

        protected override SuccessCode ExecuteCore(
            BuildContext context,
            SwapSettings settings,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments )
        {
            // Also covers the revert swap and the case where the swap runs as a separate build, long after the slot
            // has been deployed to and stopped. Starting a running slot is a no-op.
            if ( this.StartSourceSlotBeforeSwap
                 && !AppServiceHelper.Start( context, this.SubscriptionId, this.ResourceGroupName, this.AppServiceName, this.SourceSlot, settings.Dry ) )
            {
                return SuccessCode.Error;
            }

            return AppServiceHelper.Swap(
                context,
                this.SubscriptionId,
                this.ResourceGroupName,
                this.AppServiceName,
                this.SourceSlot,
                this.TargetSlot,
                settings.Dry )
                ? SuccessCode.Success
                : SuccessCode.Error;
        }

        public override SuccessCode CleanUpAfterSwap(
            BuildContext context,
            SwapSettings settings,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments )
        {
            if ( !this.StopSourceSlotAfterSwap )
            {
                return SuccessCode.Success;
            }

            return AppServiceHelper.Stop( context, this.SubscriptionId, this.ResourceGroupName, this.AppServiceName, this.SourceSlot, settings.Dry )
                ? SuccessCode.Success
                : SuccessCode.Error;
        }

        public override bool VerifyContainerRequirements( BuildContext context, ContainerRequirements requirements )
            => base.VerifyContainerRequirements( context, requirements )
               && requirements.RequireComponent<AzureCliComponent>( context );
    }
}