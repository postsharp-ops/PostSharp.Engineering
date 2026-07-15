// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Publishing
{
    /// <summary>
    /// An abstract publisher class used in <see cref="PublishCommand"/> to publish artifacts or execute publishing step.
    /// </summary>
    [PublicAPI]
    public abstract class Publisher : IBuildComponent
    {
        /// <summary>
        /// When set to false, the publisher will not publish pre-release artifacts. Default is true.
        /// </summary>
        public bool PublishPrerelease { get; init; } = true;

        /// <summary>
        /// Gets the name of the deployment this publisher belongs to. Publishers sharing the same deployment name are
        /// grouped into a single TeamCity deployment configuration. When <c>null</c> (the default), the publisher joins
        /// the <see cref="DefaultDeploymentName"/> group.
        /// </summary>
        public string? DeploymentName { get; init; }

        /// <summary>
        /// Gets the deployment name used when <see cref="DeploymentName"/> is not set. The base implementation returns
        /// <c>"default"</c>; <see cref="SshPublisher"/> overrides it to <c>"ssh"</c>.
        /// </summary>
        protected virtual string DefaultDeploymentName => "default";

        /// <summary>
        /// Gets the effective deployment name of this publisher: <see cref="DeploymentName"/> when set, otherwise
        /// <see cref="DefaultDeploymentName"/>. This is the key by which publishers are grouped into deployments.
        /// </summary>
        internal string EffectiveDeploymentName => this.DeploymentName ?? this.DefaultDeploymentName;

        /// <summary>
        /// Gets a value indicating whether this publisher does nothing during <c>b publish</c> because it is deployed by
        /// another mechanism (e.g. <see cref="SshPublisher"/>, which is deployed by native TeamCity SSH runners). Such a
        /// publisher does not define a deployment that <c>b publish</c> can target.
        /// </summary>
        internal virtual bool IsInertAtPublishTime => false;

        public virtual bool VerifyContainerRequirements( BuildContext context, ContainerRequirements requirements ) => true;

        IEnumerable<IBuildComponent> IBuildComponent.Children => [];

        public virtual void AddDependencies( List<Publisher> publishers, int currentIndex ) { }

        protected abstract bool Publish(
            BuildContext context,
            PublishSettings settings,
            (string Private, string Public) directories,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments,
            bool isPublic,
            ref bool hasTarget );

        /// <summary>
        /// Gets the distinct names of the deployments that <c>b publish</c> can target in
        /// <paramref name="configuration"/>. Publishers that are inert at publish time (such as SSH publishers, deployed
        /// by native TeamCity runners) are excluded, because they define no deployment that <c>b publish</c> acts on.
        /// </summary>
        public static IReadOnlyList<string> GetPublishDeploymentNames( BuildConfigurationInfo configuration )
            => ( configuration.PublicPublishers ?? [] )
                .Concat( configuration.PrivatePublishers ?? [] )
                .Where( p => !p.IsInertAtPublishTime )
                .Select( p => p.EffectiveDeploymentName )
                .Distinct( StringComparer.Ordinal )
                .ToList();

        public static bool PublishDirectory(
            BuildContext context,
            PublishSettings settings,
            (string Private, string Public) directories,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments,
            bool isPublic,
            ref bool hasTarget,
            string? deploymentName = null )
        {
            var publishers = isPublic ? configuration.PublicPublishers?.ToList() : configuration.PrivatePublishers?.ToList();

            if ( publishers == null || publishers.Count == 0 )
            {
                return true;
            }

            // When a specific deployment was requested, publish only the publishers of that deployment.
            if ( deploymentName != null )
            {
                publishers = publishers.Where( p => string.Equals( p.EffectiveDeploymentName, deploymentName, StringComparison.Ordinal ) ).ToList();

                if ( publishers.Count == 0 )
                {
                    return true;
                }
            }

            for ( var i = 0; i < publishers.Count; i++ )
            {
                publishers[i].AddDependencies( publishers, i );
            }

            var publishingSucceeded = true;

            foreach ( var publisher in publishers )
            {
                if ( buildArguments.IsPrerelease && !publisher.PublishPrerelease )
                {
                    context.Console.WriteWarning(
                        $"Skip publishing by '{publisher.GetType().Name}' because '{buildArguments.PackageVersion}' is a pre-release." );

                    continue;
                }

                context.Console.WriteHeading( $"Publishing with {publisher.GetType().Name}" );

                try
                {
                    if ( !publisher.Publish(
                            context,
                            settings,
                            directories,
                            configuration,
                            buildArguments,
                            isPublic,
                            ref hasTarget ) )
                    {
                        publishingSucceeded = false;
                    }
                }
                catch ( Exception e )
                {
                    context.Console.WriteError( $"Publisher '' failed with {e.GetType().Name}: {e}" );
                    publishingSucceeded = false;
                }
            }

            return publishingSucceeded;
        }
    }
}