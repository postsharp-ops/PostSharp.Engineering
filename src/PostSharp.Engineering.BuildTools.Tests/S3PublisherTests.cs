// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Amazon;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using System;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class S3PublisherTests
{
    private static readonly BuildArguments _buildArguments = new() { PackageVersion = "2024.0.1", Configuration = "Public", MSBuildConfiguration = "Release" };

    private static S3PublisherConfiguration CreateConfiguration( ParametricString files, ParametricString keyName )
        => new( files, RegionEndpoint.EUWest1, "download-sharpcrafters-com", keyName );

    [Fact]
    public void Files_AreTheUnionOfThePatternsOfAllConfigurations()
    {
        var publisher = new S3Publisher(
        [
            CreateConfiguration( "Index.xml", "postsharp/Index.xml" ),
            CreateConfiguration( "release/**", "postsharp/v$(PackageVersion)/" )
        ] );

        Assert.Equal( "+Index.xml +release/**", publisher.Files.ToString() );
    }

    [Fact]
    public void GetKey_KeyNameWithoutTrailingSlash_IsTheCompleteKey()
    {
        var configuration = CreateConfiguration( "PostSharp.zip", "postsharp/postsharp-2024.0/v$(PackageVersion)/PostSharp.zip" );

        Assert.Equal(
            "postsharp/postsharp-2024.0/v2024.0.1/PostSharp.zip",
            S3Publisher.GetKey( configuration, "PostSharp.zip", _buildArguments ) );
    }

    // A key name ending with a slash is a prefix, so one configuration publishes a whole tree: the path of each
    // file relative to the artifact directory becomes the rest of the key.
    [Fact]
    public void GetKey_KeyNameWithTrailingSlash_IsCompletedWithTheRelativePath()
    {
        var configuration = CreateConfiguration( "release/**", "postsharp/postsharp-2024.0/v$(PackageVersion)/" );

        Assert.Equal(
            "postsharp/postsharp-2024.0/v2024.0.1/release/bin/PostSharp.exe",
            S3Publisher.GetKey( configuration, "release/bin/PostSharp.exe", _buildArguments ) );
    }

    // A relative path produced on Windows uses backslashes, which are not key separators in S3.
    [Fact]
    public void GetKey_BackslashesOfTheRelativePath_BecomeKeySeparators()
    {
        var configuration = CreateConfiguration( "release/**", "postsharp/v1.0/" );

        Assert.Equal(
            "postsharp/v1.0/release/bin/PostSharp.exe",
            S3Publisher.GetKey( configuration, @"release\bin\PostSharp.exe", _buildArguments ) );
    }

    [Fact]
    public void FindConfiguration_MatchesTheRelativePathAgainstTheGlob()
    {
        var tree = CreateConfiguration( "release/**", "postsharp/v1.0/" );
        var publisher = new S3Publisher( [tree] );

        Assert.Same( tree, publisher.FindConfiguration( "release/bin/PostSharp.exe", _buildArguments ) );
        Assert.Same( tree, publisher.FindConfiguration( @"release\bin\PostSharp.exe", _buildArguments ) );
        Assert.Null( publisher.FindConfiguration( "other/PostSharp.exe", _buildArguments ) );
    }

    // The configurations are ordered, so a specific configuration listed before a general one wins for the files
    // that both of them match.
    [Fact]
    public void FindConfiguration_ReturnsTheFirstMatchingConfiguration()
    {
        var index = CreateConfiguration( "release/Index.xml", "postsharp/Index.xml" );
        var tree = CreateConfiguration( "release/**", "postsharp/v1.0/" );
        var publisher = new S3Publisher( [index, tree] );

        Assert.Same( index, publisher.FindConfiguration( "release/Index.xml", _buildArguments ) );
        Assert.Same( tree, publisher.FindConfiguration( "release/PostSharp.zip", _buildArguments ) );
    }

    [Fact]
    public void FindConfiguration_ExpandsTheParametersOfThePattern()
    {
        var configuration = CreateConfiguration( "PostSharp-$(PackageVersion).zip", "postsharp/PostSharp.zip" );
        var publisher = new S3Publisher( [configuration] );

        Assert.Same( configuration, publisher.FindConfiguration( "PostSharp-2024.0.1.zip", _buildArguments ) );
        Assert.Null( publisher.FindConfiguration( "PostSharp-2024.0.2.zip", _buildArguments ) );
    }

    [Fact]
    public void CreatePutObjectRequest_SetsContentDispositionFromTheFileName()
    {
        var configuration = CreateConfiguration( "release/**", "postsharp/v1.0/" );

        var request = S3Publisher.CreatePutObjectRequest(
            configuration,
            @"C:\artifacts\publish\public\release\bin\PostSharp.exe",
            "release/bin/PostSharp.exe",
            _buildArguments );

        Assert.Equal( "download-sharpcrafters-com", request.BucketName );
        Assert.Equal( "postsharp/v1.0/release/bin/PostSharp.exe", request.Key );
        Assert.Equal( @"C:\artifacts\publish\public\release\bin\PostSharp.exe", request.FilePath );
        Assert.Equal( "attachment; filename=\"PostSharp.exe\"", request.Headers.ContentDisposition, StringComparer.Ordinal );
    }

    [Fact]
    public void CreatePutObjectRequest_WhenNotAnAttachment_SetsNoContentDisposition()
    {
        var configuration = new S3PublisherConfiguration( "site/**", RegionEndpoint.EUWest1, "doc.postsharp.net", "doc/" ) { IsAttachment = false };

        var request = S3Publisher.CreatePutObjectRequest(
            configuration,
            @"C:\artifacts\site\index.html",
            "site/index.html",
            _buildArguments );

        Assert.Null( request.Headers.ContentDisposition );
    }

    [Fact]
    public void IsAttachment_IsEnabledByDefault()
    {
        Assert.True( CreateConfiguration( "PostSharp.zip", "postsharp/PostSharp.zip" ).IsAttachment );
    }

    // PackageFileName is the former name of Files and is kept for backward compatibility, so both directions of
    // the alias have to reach the same value.
#pragma warning disable CS0618
    [Fact]
    public void PackageFileName_IsAnAliasOfFiles()
    {
        var configuration = new S3PublisherConfiguration( "release/**", RegionEndpoint.EUWest1, "download-sharpcrafters-com", "postsharp/v1.0/" );

        Assert.Equal( "release/**", configuration.PackageFileName.ToString() );

        var initialized =
            new S3PublisherConfiguration(
                "unused",
                RegionEndpoint.EUWest1,
                "download-sharpcrafters-com",
                "postsharp/v1.0/" ) { PackageFileName = "release/**" };

        Assert.Equal( "release/**", initialized.Files.ToString() );
    }
#pragma warning restore CS0618
}