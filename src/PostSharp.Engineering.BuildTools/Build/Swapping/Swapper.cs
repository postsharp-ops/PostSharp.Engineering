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
        /// Gets or sets the list of testers that should be successfully executed before the swap is initiated.
        /// </summary>
        public Tester[] Testers { get; init; } = [];

        /// <summary>
        /// Executes the swap operation.
        /// </summary>
        public SuccessCode Execute( BuildContext context, SwapSettings settings, BuildConfigurationInfo configuration, BuildArguments buildArguments )
        {
            if ( buildArguments.IsPrerelease && !this.SwapPrerelease )
            {
                context.Console.WriteWarning( $"Skip swapping by '{this.GetType().Name}' because '{buildArguments.PackageVersion}' is a pre-release." );

                return SuccessCode.Success;
            }

            return this.ExecuteCore( context, settings, configuration, buildArguments );
        }

        protected abstract SuccessCode ExecuteCore(
            BuildContext context,
            SwapSettings settings,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments );

        public virtual bool VerifyContainerRequirements( BuildContext context, ContainerRequirements requirements ) => true;

        IEnumerable<IBuildComponent> IBuildComponent.Children => this.Testers;
    }
}