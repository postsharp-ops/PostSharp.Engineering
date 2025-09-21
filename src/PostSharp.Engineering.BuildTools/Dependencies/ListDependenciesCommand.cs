// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Dependencies.Model;

namespace PostSharp.Engineering.BuildTools.Dependencies
{
    /// <summary>
    /// Lists dependencies.
    /// </summary>
    [UsedImplicitly]
    internal class ListDependenciesCommand : BaseCommand<BaseDependenciesCommandSettings>
    {
        protected override bool ExecuteCore( BuildContext context, BaseDependenciesCommandSettings settings )
        {
            var productDependencies = context.Product.ParametrizedDependencies;

            if ( productDependencies is not { Length: > 0 } )
            {
                context.Console.WriteImportantMessage( $"{context.Product.ProductName} has no dependency." );
            }
            else
            {
                context.Console.WriteImportantMessage( $"{context.Product.ProductName} has {productDependencies.Length} explicit dependencies:" );

                if ( !settings.TryGetBuildConfiguration( context, out var configuration ) )
                {
                    return false;
                }

                if ( !DependenciesOverrideFile.TryLoad( context, settings, configuration, out var dependenciesOverrideFile ) )
                {
                    return false;
                }

                dependenciesOverrideFile.Print( context );
            }

            return true;
        }
    }
}