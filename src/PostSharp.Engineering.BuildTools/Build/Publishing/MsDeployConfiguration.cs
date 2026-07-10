// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;

namespace PostSharp.Engineering.BuildTools.Build.Publishing
{
    /// <summary>
    /// Configuration of a deployment with <see cref="MsDeployPublisher"/>.
    /// </summary>
    [PublicAPI]
    public class MsDeployConfiguration
    {
        public ParametricString PackageFileName { get; init; }

        public string SubscriptionId { get; init; }

        public string ResourceGroupName { get; init; }

        public string SiteName { get; init; }

        public string SlotName { get; init; } = "staging";

        public string? VirtualDirectory { get; init; }

        /// <summary>
        /// When set to <c>true</c>, the default, <see cref="SlotName"/> is started after the package has been deployed
        /// to it, i.e. before the testers of the <see cref="MsDeployPublisher"/> are executed. Deployment slots are
        /// typically stopped when they are not being deployed or swapped. Note that deploying to a stopped slot is
        /// supported, because stopping a slot does not stop its SCM site, through which <c>MSDeploy</c> works.
        /// </summary>
        public bool StartSlotAfterDeployment { get; init; } = true;

        public MsDeployConfiguration(
            ParametricString packageFileName,
            string subscriptionId,
            string resourceGroupName,
            string siteName,
            string? slotName = null,
            string? virtualDirectory = null )
        {
            this.PackageFileName = packageFileName;
            this.SubscriptionId = subscriptionId;
            this.ResourceGroupName = resourceGroupName;
            this.SiteName = siteName;

            if ( slotName != null )
            {
                this.SlotName = slotName;
            }

            if ( virtualDirectory != null )
            {
                this.VirtualDirectory = virtualDirectory;
            }
        }
    }
}