// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Docker;

public class ClaudeComponent : ContainerComponent
{
    private const string _minNodeVersion = "22.0.0";

    public override string Name => "Install Claude CLI";

    public override ContainerComponentKind Kind => ContainerComponentKind.Claude;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            RUN npm install --global @anthropic-ai/claude-code

            # Add PostSharp.Engineering.AISkills as a plugin marketplace and install plugins
            RUN claude plugin marketplace add postsharp/PostSharp.Engineering.AISkills && `
                claude plugin marketplace update postsharp-engineering-aiskills
            """ );
    }

    public override void AddRequirements( IReadOnlyList<ContainerComponent> components, Action<ContainerComponent> add )
    {
        base.AddRequirements( components, add );

        var existingNodeJs = components.OfType<NodeJsComponent>().FirstOrDefault();

        if ( existingNodeJs == null )
        {
            // Auto-add NodeJsComponent with minimum required version
            add( new NodeJsComponent( _minNodeVersion ) );
        }
        else if ( Version.Parse( existingNodeJs.Version ) < Version.Parse( _minNodeVersion ) )
        {
            throw new InvalidOperationException(
                $"Claude CLI requires Node.js >= {_minNodeVersion}, but {existingNodeJs.Version} is configured." );
        }
    }
}
