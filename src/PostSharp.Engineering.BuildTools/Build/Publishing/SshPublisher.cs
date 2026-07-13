// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;

namespace PostSharp.Engineering.BuildTools.Build.Publishing;

/// <summary>
/// A <see cref="Publisher"/> that deploys a build's <c>.zip</c> artifact to a single target machine over SSH: the
/// archive is transferred with SCP, extracted on the target, and its <c>deploy.ps1</c> bootstrapper is run over SSH.
/// Add one instance per target machine to a build configuration's public or private publishers.
/// </summary>
/// <remarks>
/// <para>
/// Unlike other publishers, this one performs no work at <c>b publish</c> time: the transfer and the remote bootstrap
/// are carried out by TeamCity's native <c>SSH Upload</c> and <c>SSH Exec</c> runners, which the TeamCity settings
/// generator emits into a dedicated deployment configuration when it finds an <see cref="SshPublisher"/> among a
/// build configuration's publishers. This publisher therefore only carries the target's configuration for the
/// generator to read; its <see cref="Publish"/> method is a no-op.
/// </para>
/// <para>
/// The private key is provided by the TeamCity <c>SSH Agent</c> build feature, which loads the uploaded SSH key named
/// <see cref="SshKeyName"/>. All SSH publishers of the same build configuration must use the same
/// <see cref="SshKeyName"/>, because a build configuration can load only one key into the SSH agent.
/// </para>
/// </remarks>
[PublicAPI]
public class SshPublisher : Publisher
{
    /// <summary>
    /// Gets the host name (or IP address) of the target machine.
    /// </summary>
    public string HostName { get; init; }

    /// <summary>
    /// Gets the SSH port of the target machine. The default is <c>22</c>.
    /// </summary>
    public int Port { get; init; } = 22;

    /// <summary>
    /// Gets the user name used to authenticate to the target machine over SSH.
    /// </summary>
    public string UserName { get; init; }

    /// <summary>
    /// Gets the name of the TeamCity-uploaded SSH key that the <c>SSH Agent</c> build feature loads for authentication.
    /// By convention, the default is <c>PostSharp.Engineering</c>.
    /// </summary>
    public string SshKeyName { get; init; } = "PostSharp.Engineering";

    /// <summary>
    /// Gets the file-name glob, relative to the private artifacts directory, of the <c>.zip</c> archive to transfer.
    /// The default is <c>*.zip</c>.
    /// </summary>
    public string ArchivePattern { get; init; } = "*.zip";

    /// <summary>
    /// Gets the directory on the target machine to which the archive is uploaded and in which it is extracted.
    /// </summary>
    public string RemoteDirectory { get; init; }

    /// <summary>
    /// Gets the command executed on the target machine over SSH after the archive has been uploaded. When <c>null</c>
    /// (the default), a Windows PowerShell (<c>pwsh</c>) one-liner is used that extracts the most recently uploaded
    /// archive from <see cref="RemoteDirectory"/> into a <c>current</c> subdirectory and runs the <c>deploy.ps1</c>
    /// it contains.
    /// </summary>
    public string? BootstrapperCommand { get; init; }

    public SshPublisher( string hostName, string userName, string remoteDirectory )
    {
        this.HostName = hostName;
        this.UserName = userName;
        this.RemoteDirectory = remoteDirectory;
    }

    protected override bool Publish(
        BuildContext context,
        PublishSettings settings,
        (string Private, string Public) directories,
        BuildConfigurationInfo configuration,
        BuildArguments buildArguments,
        bool isPublic,
        ref bool hasTarget )
    {
        // Intentionally a no-op: the actual SCP transfer and remote bootstrap are performed by the native TeamCity
        // SSH Upload / SSH Exec runners generated for this publisher, not by the 'b publish' step.
        return true;
    }
}
