// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;

namespace PostSharp.Engineering.BuildTools.Build.Model
{
    /// <summary>
    /// A string that can contain the following parameters: <c>$(PackageVersion)</c>, <c>$(Configuration)</c> or <c>$(MSBuildConfiguration)</c>.
    /// </summary>
    [PublicAPI]
    public readonly struct ParametricString
    {
        private readonly string? _value;

        public ParametricString( string value )
        {
            this._value = value;
        }

        public override string ToString() => this._value ?? "<null>";

        public string ToString( BuildArguments parameters )
        {
            var value = this._value;

            if ( value == null )
            {
                throw new ArgumentNullException();
            }

            Replace( ref value, "PackageVersion", parameters.PackageVersion );
            Replace( ref value, "PackagePreviewVersion", parameters.PackagePreviewVersion );
            Replace( ref value, "Configuration", parameters.Configuration );
            Replace( ref value, "MSBuildConfiguration", parameters.MSBuildConfiguration );

            // Historic typo.
            Replace( ref value, "MSSBuildConfiguration", parameters.MSBuildConfiguration );

            if ( value.Contains( "$(", StringComparison.Ordinal ) )
            {
                throw new InvalidOperationException( $"The {nameof(ParametricString)} contains an unresolved parameter." );
            }

            return value;
        }

        private static void Replace( ref string s, string parameterName, string? value )
        {
            var placeholder = $"$({parameterName})";

            if ( s.Contains( placeholder, StringComparison.OrdinalIgnoreCase ) )
            {
                if ( value == null )
                {
                    throw new InvalidOperationException(
                        $"The parametric string contains a reference to the '{parameterName}' parameter, but it was not supplied." );
                }

                s = s.Replace( placeholder, value, StringComparison.OrdinalIgnoreCase );
            }
        }

        public static implicit operator ParametricString( string value ) => new( value );
    }
}