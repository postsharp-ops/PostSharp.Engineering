// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Collections.Immutable;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model;

[PublicAPI]
public class ConfigurationSpecific<T>( T debug, T release, T @public )
{
    private ImmutableDictionary<BuildConfiguration, T>? _asDictionary;

    public T Debug { get; init; } = debug;

    public T Release { get; init; } = release;

    public T Public { get; init; } = @public;

    public ConfigurationSpecific( T value ) : this( value, value, value ) { }

    public ConfigurationSpecific( in (T Debug, T Release, T Public) values ) : this( values.Debug, values.Release, values.Public ) { }

    public T this[ BuildConfiguration configuration ]
        => configuration switch
        {
            BuildConfiguration.Debug => this.Debug,
            BuildConfiguration.Release => this.Release,
            BuildConfiguration.Public => this.Public,
            _ => throw new ArgumentOutOfRangeException()
        };

    public T GetValue( BuildConfiguration configuration ) => this[configuration];

    public ConfigurationSpecific<T> WithValue( BuildConfiguration configuration, T value )
        => configuration switch
        {
            BuildConfiguration.Debug => new ConfigurationSpecific<T>( value, this.Release, this.Public ),
            BuildConfiguration.Release => new ConfigurationSpecific<T>( this.Debug, value, this.Public ),
            BuildConfiguration.Public => new ConfigurationSpecific<T>( this.Debug, this.Release, value ),
            _ => throw new ArgumentOutOfRangeException()
        };

    public ConfigurationSpecific<T> WithValue( BuildConfiguration configuration, Func<T, T> func )
        => this.WithValue( configuration, func( this.GetValue( configuration ) ) );

    public override string ToString() => $"Debug={{{this.Debug}}}, Release={{{this.Release}}}, Public={{{this.Public}}}";

    public ImmutableDictionary<BuildConfiguration, T> AsDictionary()
    {
        this._asDictionary ??= ImmutableDictionary<BuildConfiguration, T>.Empty
            .Add( BuildConfiguration.Debug, this.Debug )
            .Add( BuildConfiguration.Release, this.Release )
            .Add( BuildConfiguration.Public, this.Public );

        return this._asDictionary;
    }
}