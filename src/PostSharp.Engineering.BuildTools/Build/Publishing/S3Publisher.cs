// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.FileSystemGlobbing;
using PostSharp.Engineering.BuildTools.Build.Model;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;

namespace PostSharp.Engineering.BuildTools.Build.Publishing
{
    /// <summary>
    /// A <see cref="Publisher"/> that uploads artifact files to Amazon S3. Each
    /// <see cref="S3PublisherConfiguration"/> selects its files with a globbing pattern, so a single configuration
    /// can publish one file or a whole directory tree.
    /// </summary>
    public class S3Publisher : ArtifactPublisher
    {
        private readonly ImmutableArray<S3PublisherConfiguration> _configurations;

        public S3Publisher( IReadOnlyCollection<S3PublisherConfiguration> configurations )
            : base( Pattern.Create( configurations.Select( c => c.Files ).ToArray() ) )
        {
            this._configurations = configurations.ToImmutableArray();
        }

        /// <summary>
        /// Publishes a file whose position in the artifact directory tree is unknown. The file name is then the
        /// only path the configuration can be matched against, and the only path a key prefix can be completed
        /// with, so this overload publishes correctly only the files that sit at the root of the artifact
        /// directory.
        /// </summary>
        public override SuccessCode PublishFile(
            BuildContext context,
            PublishSettings settings,
            string file,
            BuildArguments buildArguments,
            BuildConfigurationInfo configuration )
            => this.PublishFile( context, settings, file, Path.GetFileName( file ), buildArguments, configuration );

        protected override SuccessCode PublishFile(
            BuildContext context,
            PublishSettings settings,
            string file,
            string relativePath,
            BuildArguments buildArguments,
            BuildConfigurationInfo configuration )
        {
            var packageConfiguration = this.FindConfiguration( relativePath, buildArguments );

            if ( packageConfiguration == null )
            {
                context.Console.WriteError( $"'{relativePath}': no configuration of the S3 publisher matches this file." );

                return SuccessCode.Error;
            }

            var hasEnvironmentError = false;

            if ( string.IsNullOrEmpty( Environment.GetEnvironmentVariable( EnvironmentVariableNames.AwsAccessKeyId ) ) )
            {
                context.Console.WriteError( $"The AWS_ACCESS_KEY_ID environment variable is not defined." );
                hasEnvironmentError = true;
            }

            if ( string.IsNullOrEmpty( Environment.GetEnvironmentVariable( EnvironmentVariableNames.AwsAccessKeySecret ) ) )
            {
                context.Console.WriteError( $"The AWS_SECRET_ACCESS_KEY environment variable is not defined." );
                hasEnvironmentError = true;
            }

            if ( hasEnvironmentError )
            {
                return SuccessCode.Fatal;
            }

            var awsAccessKeyId = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AwsAccessKeyId );
            var awsSecretAccessKey = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AwsAccessKeySecret );

            var putRequest = CreatePutObjectRequest( packageConfiguration, file, relativePath, buildArguments );

            var message =
                $"Publishing '{file}' file to '{putRequest.Key}' in '{packageConfiguration.BucketName}' bucket in '{packageConfiguration.RegionEndpoint}' region. AWS access key ID: '{awsAccessKeyId}'.";

            if ( settings.Dry )
            {
                context.Console.WriteImportantMessage( $"Dry run: {message}" );

                return SuccessCode.Success;
            }
            else
            {
                try
                {
                    context.Console.WriteImportantMessage( message );

                    using var client = new AmazonS3Client( awsAccessKeyId, awsSecretAccessKey, packageConfiguration.RegionEndpoint );
                    var putResponse = client.PutObjectAsync( putRequest ).GetAwaiter().GetResult();

                    if ( putResponse.HttpStatusCode != HttpStatusCode.OK )
                    {
                        context.Console.WriteError( "AmazonS3Client failed to publish the file." );

                        return SuccessCode.Error;
                    }

                    return SuccessCode.Success;
                }
                catch ( AmazonS3Exception e )
                {
                    context.Console.WriteError(
                        "AWS S3 error encountered. Message:'{0}' when writing an object",
                        e.Message );

                    return SuccessCode.Error;
                }
                catch ( Exception e )
                {
                    context.Console.WriteError(
                        "Unknown error encountered. Message:'{0}' when writing an object",
                        e.Message );

                    return SuccessCode.Error;
                }
            }
        }

        /// <summary>
        /// Gets the first configuration whose <see cref="S3PublisherConfiguration.Files"/> pattern matches
        /// <paramref name="relativePath"/>, or <c>null</c> when no configuration matches it. The configurations are
        /// tried in the order in which they were given to the constructor, so a caller that lists a specific
        /// configuration before a general one publishes the files they both match under the specific one.
        /// </summary>
        internal S3PublisherConfiguration? FindConfiguration( string relativePath, BuildArguments buildArguments )
        {
            var normalizedPath = NormalizePath( relativePath );

            foreach ( var configuration in this._configurations )
            {
                var matcher = new Matcher( StringComparison.OrdinalIgnoreCase );
                matcher.AddInclude( configuration.Files.ToString( buildArguments ) );

                if ( matcher.Match( normalizedPath ).HasMatches )
                {
                    return configuration;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the request that writes <paramref name="file"/> to the bucket of
        /// <paramref name="configuration"/>.
        /// </summary>
        internal static PutObjectRequest CreatePutObjectRequest(
            S3PublisherConfiguration configuration,
            string file,
            string relativePath,
            BuildArguments buildArguments )
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = configuration.BucketName, Key = GetKey( configuration, relativePath, buildArguments ), FilePath = file
            };

            if ( configuration.IsAttachment )
            {
                putRequest.Headers.ContentDisposition = $"attachment; filename=\"{Path.GetFileName( relativePath )}\"";
            }

            return putRequest;
        }

        /// <summary>
        /// Gets the key <paramref name="relativePath"/> is written under. A
        /// <see cref="S3PublisherConfiguration.KeyName"/> that ends with a slash is a prefix, and the relative path
        /// completes it. Any other key name is the complete key.
        /// </summary>
        internal static string GetKey( S3PublisherConfiguration configuration, string relativePath, BuildArguments buildArguments )
        {
            var keyName = configuration.KeyName.ToString( buildArguments );

            return keyName.EndsWith( '/' ) ? keyName + NormalizePath( relativePath ) : keyName;
        }

        // The globbing matcher and the S3 keys both use forward slashes, while a relative path produced on Windows
        // can use backslashes.
        private static string NormalizePath( string path ) => path.Replace( '\\', '/' );
    }
}