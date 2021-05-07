using System;
using System.IO;
using System.Linq;
using Marten.Exceptions;
using Weasel.Postgresql;
using Npgsql;

namespace Marten.Storage
{
    internal static class FeatureSchemaExtensions
    {
        public static void AssertValidNames(this IFeatureSchema schema, StoreOptions options)
        {
            AssertValidNames(schema.Objects, options);
        }

        public static void AssertValidNames(this ISchemaObject[] schemaObjects, StoreOptions options)
        {
            foreach (var objectName in schemaObjects.SelectMany(x => x.AllNames()))
            {
                options.AssertValidIdentifier(objectName.Name);
            }
        }

        public static void Write(this IFeatureSchema schema, DdlRules rules, TextWriter writer)
        {
            foreach (var schemaObject in schema.Objects)
            {
                schemaObject.WriteCreateStatement(rules, writer);
            }

            schema.WritePermissions(rules, writer);
        }
    }
}
