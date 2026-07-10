// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Microsoft.Extensions.FileSystemGlobbing;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Testing;
using System;
using System.Collections.Generic;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Publishing
{
    /// <summary>
    /// A publisher that publishes all artifact files specified in <see cref="Files"/> pattern.
    /// </summary>
    [PublicAPI]
    public abstract class ArtifactPublisher : Publisher
    {
        public Pattern Files { get; }

        public Tester[] Testers { get; init; } = [];

        protected ArtifactPublisher( Pattern files )
        {
            this.Files = files;
        }

        /// <summary>
        /// Executes the target for a specified artifact.
        /// </summary>
        public abstract SuccessCode PublishFile(
            BuildContext context,
            PublishSettings settings,
            string file,
            BuildArguments buildArguments,
            BuildConfigurationInfo configuration );

        /// <summary>
        /// Called after all artifact files have been successfully published and before the <see cref="Testers"/> are
        /// executed. When this method fails, the <see cref="Testers"/> are not executed.
        /// </summary>
        protected virtual SuccessCode OnFilesPublished(
            BuildContext context,
            PublishSettings settings,
            (string Private, string Public) directories,
            BuildArguments buildArguments,
            BuildConfigurationInfo configuration )
            => SuccessCode.Success;

        protected override bool Publish(
            BuildContext context,
            PublishSettings settings,
            (string Private, string Public) directories,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments,
            bool isPublic,
            ref bool hasTarget )
        {
            var success = true;

            var directory = isPublic ? directories.Public : directories.Private;

            var files = new List<FilePatternMatch>();

            if ( !this.Files.TryGetFiles( directory, buildArguments, files ) )
            {
                context.Console.WriteWarning( $"Created artifact files do not match the publisher pattern(s): '{this.Files}'" );

                return true;
            }

            var allFilesSucceeded = true;

            foreach ( var file in files )
            {
                if ( (file.Stem ?? file.Path).Contains( "-local-", StringComparison.OrdinalIgnoreCase ) )
                {
                    context.Console.WriteError( $"'{file.Path}': Cannot publish a local build." );

                    return false;
                }

                hasTarget = true;

                var filePath = Path.Combine( directory, file.Path );

                switch ( this.PublishFile( context, settings, filePath, buildArguments, configuration ) )
                {
                    case SuccessCode.Success:
                        break;

                    case SuccessCode.Error:
                        success = false;
                        allFilesSucceeded = false;

                        break;

                    case SuccessCode.Fatal:
                        return false;

                    default:
                        throw new NotImplementedException();
                }
            }

            if ( allFilesSucceeded )
            {
                var canRunTesters = true;

                switch ( this.OnFilesPublished( context, settings, directories, buildArguments, configuration ) )
                {
                    case SuccessCode.Success:
                        break;

                    case SuccessCode.Error:
                        // Running the testers would only add noise to the root cause.
                        success = false;
                        canRunTesters = false;

                        break;

                    case SuccessCode.Fatal:
                        return false;

                    default:
                        throw new NotImplementedException();
                }

                if ( canRunTesters )
                {
                    foreach ( var tester in this.Testers )
                    {
                        switch ( tester.Execute( context, directories.Private, buildArguments, settings.Dry ) )
                        {
                            case SuccessCode.Success:
                                break;

                            case SuccessCode.Error:
                                success = false;

                                break;

                            case SuccessCode.Fatal:
                                return false;

                            default:
                                throw new NotImplementedException();
                        }
                    }
                }
            }

            if ( !success )
            {
                context.Console.WriteError( "Artifact publishing has failed." );
            }

            return success;
        }
    }
}