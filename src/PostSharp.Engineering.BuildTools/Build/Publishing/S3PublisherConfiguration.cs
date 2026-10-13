// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Amazon;
using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using System;

namespace PostSharp.Engineering.BuildTools.Build.Publishing
{
    /// <summary>
    /// The configuration of one group of objects published by <see cref="S3Publisher"/>: the artifact files that
    /// belong to the group, the bucket they are written to, and the key each of them is written under.
    /// </summary>
    [PublicAPI]
    public class S3PublisherConfiguration
    {
        /// <summary>
        /// Gets the globbing pattern, relative to the artifact directory, of the files published by this
        /// configuration. A literal file name selects a single file. A pattern such as <c>**</c> selects a whole
        /// directory tree, in which case <see cref="KeyName"/> has to end with a slash.
        /// </summary>
        public ParametricString Files { get; init; }

        /// <summary>
        /// Gets the pattern of the files published by this configuration. This property is an alias of
        /// <see cref="Files"/>, which replaces it because the value is a globbing pattern and no longer has to be
        /// the name of a single package file.
        /// </summary>
        [Obsolete( "Renamed to Files." )]
        public ParametricString PackageFileName
        {
            get => this.Files;
            init => this.Files = value;
        }

        /// <summary>
        /// Gets the AWS region of <see cref="BucketName"/>.
        /// </summary>
        public RegionEndpoint RegionEndpoint { get; init; }

        /// <summary>
        /// Gets the name of the destination bucket.
        /// </summary>
        public string BucketName { get; init; }

        /// <summary>
        /// Gets the key the objects are written under. When it ends with a slash, it is a prefix, and the path of
        /// each file relative to the artifact directory is appended to it, so that a single configuration can
        /// publish a directory tree. When it does not end with a slash, it is the complete key, and
        /// <see cref="Files"/> is then expected to match a single file.
        /// </summary>
        public ParametricString KeyName { get; init; }

        /// <summary>
        /// Gets a value indicating whether the objects are written with a <c>Content-Disposition</c> header of
        /// <c>attachment; filename="&lt;file name&gt;"</c>. Such a header makes the browser save the object under
        /// the name of the published file instead of the last segment of the URL. The default is <c>true</c>,
        /// because these buckets normally serve downloads. Set it to <c>false</c> for a bucket whose objects are
        /// meant to be rendered by the browser.
        /// </summary>
        public bool IsAttachment { get; init; } = true;

        public S3PublisherConfiguration(
            ParametricString files,
            RegionEndpoint regionEndpoint,
            string bucketName,
            ParametricString keyName )
        {
            this.Files = files;
            this.RegionEndpoint = regionEndpoint;
            this.BucketName = bucketName;
            this.KeyName = keyName;
        }
    }
}