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

        public static bool PublishDirectory(
            BuildContext context,
            PublishSettings settings,
            (string Private, string Public) directories,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments,
            bool isPublic,
            ref bool hasTarget )
        {
            var publishers = isPublic ? configuration.PublicPublishers?.ToList() : configuration.PrivatePublishers?.ToList();

            if ( publishers == null || publishers.Count == 0 )
            {
                return true;
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