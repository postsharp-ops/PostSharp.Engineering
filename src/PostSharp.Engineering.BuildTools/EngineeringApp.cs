// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools;

[PublicAPI]
public sealed class EngineeringApp
{
    private readonly CommandApp _app;

    public Product Product { get; }

    public EngineeringApp( Product product )
    {
        this.Product = product;
        this._app = new CommandApp();
        this._app.AddCommands( this.Product );
    }

    public void Configure( Action<IConfigurator> configure )
    {
        this._app.Configure( configure );
    }

    public int Run( IEnumerable<string> args ) => this._app.Run( args );
}