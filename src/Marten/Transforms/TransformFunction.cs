using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Util;

namespace Marten.Transforms
{
    public class TransformFunction : ISchemaObjects
    {
        public static readonly string Prefix = "mt_transform_";

        private readonly StoreOptions _options;
        private bool _checked;

        public TransformFunction(StoreOptions options, string name, string body)
        {
            _options = options;
            Name = name;
            Body = body;

            Function = new DbObjectName(_options.DatabaseSchemaName, $"{Prefix}{Name.ToLower().Replace(".", "_")}");
        }

        public string Name { get; set; }
        public string Body { get; set; }

        public DbObjectName Function { get; }

        public readonly IList<string> OtherArgs = new List<string>();

        private IEnumerable<string> allArgs()
        {
            return new string[] {"doc"}.Concat(OtherArgs);
        }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, SchemaPatch patch)
        {
            if (_checked) return;


            var diff  = functionDiff(schema);
            if (!diff.HasChanged)
            {
                _checked = true;
                return;
            }


            if (autoCreateSchemaObjectsMode == AutoCreate.None)
            {
                string message =
                    $"The transform function {Function.QualifiedName} and cannot be created dynamically unless the {nameof(StoreOptions)}.{nameof(StoreOptions.AutoCreateSchemaObjects)} is higher than \"None\". See http://jasperfx.github.io/marten/documentation/documents/ for more information";
                throw new InvalidOperationException(message);
            }

            diff.WritePatch(patch);
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            writer.WriteLine(GenerateFunction());
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            var signature = allArgs().Select(x => "JSONB").Join(", ");
            var dropSql = $"DROP FUNCTION IF EXISTS {Function.QualifiedName}({signature})";
            connection.Execute(cmd => cmd.Sql(dropSql).ExecuteNonQuery());
        }

        public void ResetSchemaExistenceChecks()
        {
            _checked = false;
        }

        public void WritePatch(IDocumentSchema schema, SchemaPatch patch)
        {
            var diff = functionDiff(schema);

            if (diff.AllNew || diff.HasChanged)
            {
                diff.WritePatch(patch);
            }
        }

        public string ToDropSignature()
        {
            var signature = allArgs().Select(x => $"JSONB").Join(", ");
            return $"DROP FUNCTION IF EXISTS {Function.QualifiedName}({signature});";
        }

        public string GenerateFunction()
        {
            var defaultExport = "{export: {}}";

            var signature = allArgs().Select(x => $"{x} JSONB").Join(", ");
            var args = allArgs().Join(", ");

            return
                $@"
CREATE OR REPLACE FUNCTION {Function.QualifiedName}({signature}) RETURNS JSONB AS $$

  var module = {defaultExport};

  {Body}

  var func = module.exports;

  return func({args});

$$ LANGUAGE plv8 IMMUTABLE STRICT;
";
        }


        public static TransformFunction ForFile(StoreOptions options, string file, string name = null)
        {
            var body = new FileSystem().ReadStringFromFile(file);
            name = name ?? Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

            return new TransformFunction(options, name, body);
        }

        private FunctionDelta functionDiff(IDocumentSchema schema)
        {
            var body = schema.DbObjects.DefinitionForFunction(Function);
            var expected = new FunctionBody(Function, new string[] {ToDropSignature()}, GenerateFunction());

            return new FunctionDelta(expected, body);
        }

        public override string ToString()
        {
            return $"Transform Function '{Name}'";
        }
    }
}