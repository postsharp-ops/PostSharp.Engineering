// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Docfx.Dotnet;
using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.DocFx;

[UsedImplicitly]
internal class DocFxMetadataCommand : BaseCommand<DocFxSettings>
{
    protected override bool ExecuteCore( BuildContext context, DocFxSettings settings )
    {
        DotnetApiCatalog.GenerateManagedReferenceYamlFiles( settings.ConfigurationPath ).Wait();

        return true;
    }
}