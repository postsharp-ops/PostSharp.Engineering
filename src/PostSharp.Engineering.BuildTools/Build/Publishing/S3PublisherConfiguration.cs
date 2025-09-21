// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Amazon;
using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;

namespace PostSharp.Engineering.BuildTools.Build.Publishing
{
    [PublicAPI]
    public class S3PublisherConfiguration
    {
        public ParametricString PackageFileName { get; init; }

        public RegionEndpoint RegionEndpoint { get; init; }

        public string BucketName { get; init; }

        public ParametricString KeyName { get; init; }

        public S3PublisherConfiguration(
            ParametricString packageFileName,
            RegionEndpoint regionEndpoint,
            string bucketName,
            string keyName )
        {
            this.PackageFileName = packageFileName;
            this.RegionEndpoint = regionEndpoint;
            this.BucketName = bucketName;
            this.KeyName = keyName;
        }
    }
}