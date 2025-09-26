// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using Typesense;

namespace PostSharp.Engineering.BuildTools.Search.Backends.Typesense;

public static class CollectionSchemaFactory
{
    public static Schema CreateSchema<T>( string collectionName )
    {
        var fields = new List<Field>();
        var containsNestedFields = false;

        foreach ( var property in typeof(T).GetProperties() )
        {
            var jsonNameAttribute = property.GetCustomAttribute<JsonPropertyNameAttribute>()
                                    ?? throw new InvalidOperationException( $"Json property name not found for \"{nameof(T)}\".\"{property.Name}\" property." );

            FieldType type;

            if ( property.PropertyType.IsArray )
            {
                var arrayTypeCode = Type.GetTypeCode( property.PropertyType.GetElementType() );

                type = arrayTypeCode switch
                {
                    TypeCode.Int32 => FieldType.Int32Array,
                    TypeCode.String => FieldType.StringArray,
                    TypeCode.Object => FieldType.ObjectArray,
                    _ => throw new InvalidOperationException( $"Unknown array type code: \"{arrayTypeCode}\"" )
                };

                if ( arrayTypeCode == TypeCode.Object )
                {
                    containsNestedFields = true;
                }
            }
            else
            {
                var typeCode = Type.GetTypeCode( property.PropertyType );

                type = typeCode switch
                {
                    TypeCode.Int32 => FieldType.Int32,
                    TypeCode.String => FieldType.String,
                    TypeCode.Object => FieldType.Object,
                    _ => throw new InvalidOperationException( $"Unknown type code: \"{typeCode}\"" )
                };

                if ( typeCode == TypeCode.Object )
                {
                    containsNestedFields = true;
                }
            }

            var isFacet = property.GetCustomAttribute<FacetAttribute>() != null;

            fields.Add( new Field( jsonNameAttribute.Name, type, isFacet ) );
        }

        return new Schema( collectionName, fields ) { EnableNestedFields = containsNestedFields };
    }
}