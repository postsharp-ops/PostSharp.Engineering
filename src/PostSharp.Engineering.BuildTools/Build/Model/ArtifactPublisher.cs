// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Extensions.FileSystemGlobbing;
using System;
using System.Collections.Generic;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Model
{
    /// <summary>
    /// A publisher that publishes all artifact files specified in <c>files</c> pattern.
    /// </summary>
    public abstract class ArtifactPublisher( Pattern files ) : Publisher
    {
        private readonly Tester[] _testers = [];

        /// <summary>
        /// Executes the target for a specified artifact.
        /// </summary>
        protected abstract SuccessCode PublishFile(
            BuildContext context,
            PublishSettings settings,
            string file,
            BuildInfo buildInfo );

        protected override bool Publish(
            BuildContext context,
            PublishSettings settings,
            (string Private, string Public) directories,
            BuildConfigurationInfo configuration,
            BuildInfo buildInfo,
            bool isPublic,
            ref bool hasTarget )
        {
            var success = true;

            var directory = isPublic ? directories.Public : directories.Private;

            var files1 = new List<FilePatternMatch>();

            if ( !files.TryGetFiles( directory, buildInfo, files1 ) )
            {
                context.Console.WriteWarning( $"Created artifact files do not match the publisher pattern(s): '{files}'" );

                return true;
            }

            var allFilesSucceeded = true;

            foreach ( var file in files1 )
            {
                if ( (file.Stem ?? file.Path).Contains( "-local-", StringComparison.OrdinalIgnoreCase ) )
                {
                    context.Console.WriteError( $"'{file.Path}': Cannot publish a local build." );

                    return false;
                }

                hasTarget = true;

                var filePath = Path.Combine( directory, file.Path );

                switch ( this.PublishFile( context, settings, filePath, buildInfo ) )
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
                foreach ( var tester in this._testers )
                {
                    switch ( tester.Execute( context, directories.Private, buildInfo, settings.Dry ) )
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

            if ( !success )
            {
                context.Console.WriteError( "Artifact publishing has failed." );
            }

            return success;
        }
    }
}