using System;
using System.Collections.Generic;
using System.IO;
using Marten.Util;
using Npgsql;

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
        public static string ToDDL(this ISchemaObject @object, DdlRules rules)
        {
            var writer = new StringWriter();
            @object.Write(rules, writer);

            return writer.ToString();
        }

        public static void Write(this IFeatureSchema schema, DdlRules rules, StringWriter writer)
        {
            foreach (var schemaObject in schema.Objects)
            {
                schemaObject.Write(rules, writer);
            }

            // TODO -- deal with the stupid DDL template stuff here
        }

        public static void WriteDropStatements(this IFeatureSchema schema, DdlRules rules, StringWriter writer)
        {
            foreach (var schemaObject in schema.Objects)
            {
                schemaObject.WriteDropStatement(rules, writer);
            }
        }

        public static void RemoveAllObjects(this IFeatureSchema schema, DdlRules rules, NpgsqlConnection conn)
        {
            var writer = new StringWriter();
            schema.WriteDropStatements(rules, writer);

            var sql = writer.ToString();
            var cmd = conn.CreateCommand(sql);

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                throw new MartenCommandException(cmd, e);
            }
        }
    }
}