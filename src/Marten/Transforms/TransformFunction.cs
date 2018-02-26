using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Baseline;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Transforms
{
    public class TransformFunction : Function
    {
        public static readonly string Prefix = "mt_transform_";



        public readonly IList<string> OtherArgs = new List<string>();

        public TransformFunction(StoreOptions options, string name, string body)
            : base(new DbObjectName(options.DatabaseSchemaName, "mt_transform_" + name.Replace(".", "_")))
        {
            Name = name;
            Body = body;
        }

        public string Name { get; set; }
        public string Body { get; set; }

        private IEnumerable<string> allArgs()
        {
            return new[] {"doc"}.Concat(OtherArgs);
        }

        public override void Write(DdlRules rules, StringWriter writer)
        {
            writer.WriteLine(GenerateFunction());
            writer.WriteLine();
        }

        protected override string toDropSql()
        {
            return ToDropSignature();
        }


        public string ToDropSignature()
        {
            var signature = allArgs().Select(x => $"JSONB").Join(", ");
            return $"DROP FUNCTION IF EXISTS {Identifier}({signature});";
        }

        public string GenerateFunction()
        {
            var defaultExport = "{export: {}}";

            var signature = allArgs().Select(x => $"{x} JSONB").Join(", ");
            var args = allArgs().Join(", ");

            return
                $@"
CREATE OR REPLACE FUNCTION {Identifier}({signature}) RETURNS JSONB AS $$

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

        public override string ToString()
        {
            return $"Transform Function '{Name}'";
        }

        public void WritePatchForAllDocuments(SchemaPatch patch, string tableName, string fileName, bool includeImmediateInvocation = false)
        {
            patch.WriteTransactionalFile(fileName, GenerateTransformExecutionScript(tableName, includeImmediateInvocation));
        }

        public string GenerateTransformExecutionScript(string tableName, bool shouldImmediatelyInvoke = false)
        {
            var sqlBodyBuilder = new StringBuilder();
            sqlBodyBuilder.Append(GenerateFunction());

            var runExecutionOnDocumentData = $"var transformedDoc = plv8.execute('SELECT {Function.QualifiedName}($1)', doc.data);";
            var updateExistingDocumentData = $"plv8.execute('UPDATE {tableName} SET data = $1 WHERE id = $2', [transformedDoc[0][\"{Function.Name}\"], doc.id]);";

            var updateAllDocs = "docs.forEach(function(doc) {" + Environment.NewLine +
                                "    " + runExecutionOnDocumentData + Environment.NewLine +
                                "    " + updateExistingDocumentData + Environment.NewLine +
                                "  });";

            var executeTransformFunctionName = $"{_options.DatabaseSchemaName}.execute_transform_{Function.Name}";

            var functionInvocation = $@"
CREATE OR REPLACE FUNCTION {executeTransformFunctionName}()

RETURNS VOID as $$

  var docs = plv8.execute('select id, data from {tableName}');
  {updateAllDocs}

$$ LANGUAGE PLV8 IMMUTABLE STRICT;";

            sqlBodyBuilder.Append(functionInvocation);
            sqlBodyBuilder.AppendLine("");

            if (shouldImmediatelyInvoke)
            {
                sqlBodyBuilder.Append($"PERFORM {executeTransformFunctionName}();");
            }

            return sqlBodyBuilder.ToString();
        }
    }
}