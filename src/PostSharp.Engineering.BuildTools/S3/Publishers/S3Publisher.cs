// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Amazon.S3;
using Amazon.S3.Model;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;

namespace PostSharp.Engineering.BuildTools.S3.Publishers
{
    public class S3Publisher : ArtifactPublisher
    {
        private readonly ImmutableArray<S3PublisherConfiguration> _configuration;

        public S3Publisher( IReadOnlyCollection<S3PublisherConfiguration> configurations )
            : base( Pattern.Create( configurations.Select( c => c.PackageFileName ).ToArray() ) )
        {
            this._configuration = ImmutableArray.Create<S3PublisherConfiguration>().AddRange( configurations );
        }

        public override SuccessCode PublishFile(
            BuildContext context,
            PublishSettings settings,
            string file,
            BuildInfo buildInfo,
            BuildConfigurationInfo configuration )
        {
            var fileName = Path.GetFileName( file );
            var packageConfiguration = this._configuration.Single( c => c.PackageFileName.ToString( buildInfo ) == fileName );
            var hasEnvironmentError = false;

            if ( string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "AWS_ACCESS_KEY_ID" ) ) )
            {
                context.Console.WriteError( $"The AWS_ACCESS_KEY_ID environment variable is not defined." );
                hasEnvironmentError = true;
            }

            if ( string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "AWS_SECRET_ACCESS_KEY" ) ) )
            {
                context.Console.WriteError( $"The AWS_SECRET_ACCESS_KEY environment variable is not defined." );
                hasEnvironmentError = true;
            }

            if ( hasEnvironmentError )
            {
                return SuccessCode.Fatal;
            }

            var awsAccessKeyId = Environment.GetEnvironmentVariable( "AWS_ACCESS_KEY_ID" );
            var awsSecretAccessKey = Environment.GetEnvironmentVariable( "AWS_SECRET_ACCESS_KEY" );

            var message =
                $"Publishing '{file}' file to '{packageConfiguration.KeyName}' in '{packageConfiguration.BucketName}' bucket in '{packageConfiguration.RegionEndpoint}' region. AWS access key ID: '{awsAccessKeyId}'.";

            if ( settings.Dry )
            {
                context.Console.WriteImportantMessage( $"Dry run: {message}" );

                return SuccessCode.Success;
            }
            else
            {
                try
                {
                    var putRequest = new PutObjectRequest
                    {
                        BucketName = packageConfiguration.BucketName, Key = packageConfiguration.KeyName.ToString( buildInfo ), FilePath = file
                    };

                    context.Console.WriteImportantMessage( message );

                    using var client = new AmazonS3Client( awsAccessKeyId, awsSecretAccessKey, packageConfiguration.RegionEndpoint );
                    var putResponse = client.PutObjectAsync( putRequest ).GetAwaiter().GetResult();

                    if ( putResponse.HttpStatusCode != HttpStatusCode.OK )
                    {
                        context.Console.WriteError( "AmazonS3Client failed to publish the file." );

                        return SuccessCode.Fatal;
                    }

                    return SuccessCode.Success;
                }
                catch ( AmazonS3Exception e )
                {
                    context.Console.WriteError(
                        "AWS S3 error encountered. Message:'{0}' when writing an object",
                        e.Message );

                    return SuccessCode.Fatal;
                }
                catch ( Exception e )
                {
                    context.Console.WriteError(
                        "Unknown error encountered. Message:'{0}' when writing an object",
                        e.Message );

                    return SuccessCode.Fatal;
                }
            }
        }
    }
}