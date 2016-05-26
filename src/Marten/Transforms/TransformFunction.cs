using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;

namespace Marten.Transforms
{
    public class TransformFunction
    {
        public static readonly string Prefix = "mt_transform_";

        private readonly StoreOptions _options;
        private bool _checked;

        public TransformFunction(StoreOptions options, string name, string body)
        {
            _options = options;
            Name = name;
            Body = body;

            Function = new FunctionName(options.DatabaseSchemaName, $"{Prefix}{name.ToLower().Replace(".", "_")}");
        }

        public string Name { get; set; }
        public string Body { get; set; }

        public FunctionName Function { get; }

        public void ResetSchemaCheck()
        {
            _checked = false;
        }

        public void LoadIfNecessary(IDocumentSchema schema, Action<string> executeSql)
        {
            if (!schema.DbObjects.SchemaFunctionNames().Contains(Function))
            {
                executeSql(GenerateFunction());
                _checked = true;
            }
        }

        public string GenerateFunction()
        {
            var defaultExport = "{export: {}}";

            return
                $@"
CREATE OR REPLACE FUNCTION {Function.QualifiedName}(doc JSONB) RETURNS JSONB AS $$

  var module = {defaultExport};

  {Body}

  var func = module.exports;

  return func(doc);

$$ LANGUAGE plv8 IMMUTABLE STRICT;
";
        }


        public static TransformFunction ForFile(StoreOptions options, string file)
        {
            var body = new FileSystem().ReadStringFromFile(file);
            var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

            return new TransformFunction(options, name, body);
        }
    }
}