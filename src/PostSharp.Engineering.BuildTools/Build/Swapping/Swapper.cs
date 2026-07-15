// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.Docker;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Build.Swapping
{
    /// <summary>
    /// A swapper is some logic that swaps a deployment slot (typically a staging one) onto another deployment slop (typically the production one).
    /// </summary>
    [PublicAPI]
    public abstract class Swapper : IBuildComponent
    {
        /// <summary>
        /// When set to false, the swapper will not swap when the product is pre-release. Default is true.
        /// </summary>
        public bool SwapPrerelease { get; init; } = true;

        /// <summary>
        /// Gets the name of the deployment this swapper is linked to. A swapper swaps the slots of the deployment of the
        /// same name, and its generated TeamCity swap configuration depends on that deployment's configuration. When
        /// <c>null</c> (the default), the swapper is linked to the <c>default</c> deployment.
        /// </summary>
        public string? DeploymentName { get; init; }

        /// <summary>
        /// Gets the effective deployment name of this swapper: <see cref="DeploymentName"/> when set, otherwise
        /// <c>"default"</c>. This is the key by which swappers are grouped and linked to a deployment.
        /// </summary>
        internal string EffectiveDeploymentName => this.DeploymentName ?? "default";

        /// <summary>
        /// Gets or sets the list of testers that are executed against the target slot after the swap. When one of them
        /// fails, the swap is reverted.
        /// </summary>
        public Tester[] Testers { get; init; } = [];

        /// <summary>
        /// Determines whether this swapper swaps for the given build. A disabled swapper is skipped entirely.
        /// </summary>
        public bool IsEnabled( BuildArguments buildArguments ) => !buildArguments.IsPrerelease || this.SwapPrerelease;

        internal void WarnSkipped( BuildContext context, BuildArguments buildArguments )
            => context.Console.WriteWarning( $"Skip swapping by '{this.GetType().Name}' because '{buildArguments.PackageVersion}' is a pre-release." );

        /// <summary>
        /// Executes the swap operation.
        /// </summary>
        public SuccessCode Execute( BuildContext context, SwapSettings settings, BuildConfigurationInfo configuration, BuildArguments buildArguments )
        {
            if ( !this.IsEnabled( buildArguments ) )
            {
                this.WarnSkipped( context, buildArguments );

                return SuccessCode.Success;
            }

            return this.ExecuteCore( context, settings, configuration, buildArguments );
        }

        protected abstract SuccessCode ExecuteCore(
            BuildContext context,
            SwapSettings settings,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments );

        /// <summary>
        /// Releases the resources that the swap needed, typically by stopping the source slot. Called by
        /// <see cref="SwapCommand"/> once the swap and its <see cref="Testers"/> have completed, or when the swapper
        /// was skipped. It is not called after a reverted or a failed swap, because the source slot must then remain
        /// available for investigation and for a manual swap.
        /// </summary>
        public virtual SuccessCode CleanUpAfterSwap(
            BuildContext context,
            SwapSettings settings,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments )
            => SuccessCode.Success;

        public virtual bool VerifyContainerRequirements( BuildContext context, ContainerRequirements requirements ) => true;

        IEnumerable<IBuildComponent> IBuildComponent.Children => this.Testers;
    }
}