using System;
using System.IO;
using Marten.Services;
using Marten.Util;

namespace Marten.Schema
{
    public class SystemFunction : ISchemaObjects
    {
        private readonly FunctionName _function;
        private readonly string _dropSql;

        public SystemFunction(StoreOptions options, string functionName)
        {
            _function = new FunctionName(options.DatabaseSchemaName, functionName);
            _dropSql = $"drop function if exists {options.DatabaseSchemaName}.mt_immutable_timestamp cascade";

            Name = functionName;
        }

        public bool Checked { get; private set; }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, SchemaPatch patch)
        {
            if (Checked) return;

            Checked = true;

            var diff = createFunctionDiff(schema);

            if (!diff.HasChanged) return;

            if (autoCreateSchemaObjectsMode == AutoCreate.None)
            {
                throw new InvalidOperationException($"{_function.QualifiedName} function is missing, but {nameof(StoreOptions.AutoCreateSchemaObjects)} is {autoCreateSchemaObjectsMode}");
            }

            diff.WritePatch(patch);
        }

        private FunctionDiff createFunctionDiff(IDocumentSchema schema)
        {
            var actual = schema.DbObjects.DefinitionForFunction(_function);

            var expectedBody = SchemaBuilder.GetSqlScript(schema.StoreOptions.DatabaseSchemaName, _function.Name);

            var expected = new FunctionBody(_function, new string[] { _dropSql }, expectedBody);

            var diff = new FunctionDiff(expected, actual);
            return diff;
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            var body = SchemaBuilder.GetSqlScript(schema.StoreOptions.DatabaseSchemaName, _function.Name);


            writer.WriteLine(body);
            writer.WriteLine("");
            writer.WriteLine("");
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            connection.Execute(cmd => cmd.Sql(_dropSql).ExecuteNonQuery());
        }

        public void ResetSchemaExistenceChecks()
        {
            Checked = false;
        }

        public void WritePatch(IDocumentSchema schema, SchemaPatch patch)
        {
            createFunctionDiff(schema).WritePatch(patch);
        }

        public string Name { get; }
    }
}