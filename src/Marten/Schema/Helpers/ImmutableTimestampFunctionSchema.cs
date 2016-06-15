using System;
using System.IO;
using Marten.Services;
using Marten.Util;
using System.Reflection;
using Baseline;

namespace Marten.Schema.Helpers
{
    public class ImmutableTimestampFactory : ISchemaObjects
    {
        private static readonly string DropFunctionSql = "drop function if exists public.mt_immutable_timestamp";

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, SchemaPatch patch)
        {
            if (autoCreateSchemaObjectsMode == AutoCreate.None)
            {
                throw new InvalidOperationException($"mt_immutable_timestamp function is missing, but {nameof(StoreOptions.AutoCreateSchemaObjects)} is {autoCreateSchemaObjectsMode}");
            }

            WritePatch(schema, patch);
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            writer.WriteLine(GetCreateFunctionSql());
            writer.WriteLine("");
            writer.WriteLine("");
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {

            connection.Execute(cmd => cmd.Sql(DropFunctionSql).ExecuteNonQuery());
        }

        public void ResetSchemaExistenceChecks()
        {
            
        }

        public void WritePatch(IDocumentSchema schema, SchemaPatch patch)
        {
            patch.Updates.Apply(this, GetCreateFunctionSql());
        }

        public string Name { get; } = "mt_immutable_timestamp";

        private string GetCreateFunctionSql()
        {
            var resourceName = $"{typeof(SchemaBuilder).Namespace}.SQL.{Name}.sql";
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                throw new InvalidOperationException("Could not find embedded resource: " + Name);
            }

            return  stream.ReadAllText();
        }
    }
}
