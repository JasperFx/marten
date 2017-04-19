using System;
using System.Collections.Generic;
using System.IO;

namespace Marten.Storage
{
    public interface IFeatureSchema
    {
        IEnumerable<Type> DependentTypes();
        bool IsActive { get; }
        ISchemaObject[] Objects { get; }
        Type StorageType { get; }

        /// <summary>
        /// Really just the filename
        /// </summary>
        string Identifier { get; }

        // TODO -- write permissions. Stupid DDL template stuff
        // for our idiot database team's endless bikeshedding
    }

    public static class FeatureSchemaExtensions
    {
        public static void Write(this IFeatureSchema schema, DdlRules rules, StringWriter writer)
        {
            foreach (var schemaObject in schema.Objects)
            {
                schemaObject.Write(rules, writer);
            }

            // TODO -- deal with the stupid DDL template stuff here
        }
    }
}