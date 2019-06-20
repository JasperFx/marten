using System.IO;
using Marten.Storage;

namespace Marten.Schema
{
    public class SystemFunction: Function
    {
        private readonly string _args;
        private readonly string _dropSql;
        private readonly DbObjectName _function;

        public SystemFunction(StoreOptions options, string functionName, string args)
            : this(options.DatabaseSchemaName, functionName, args)
        {
        }

        public SystemFunction(string schema, string functionName, string args)
            : base(new DbObjectName(schema, functionName))
        {
            _args = args;
            _function = new DbObjectName(schema, functionName);
            _dropSql = $"drop function if exists {schema}.{functionName}({args}) cascade;";

            Name = functionName;
        }

        public string Name { get; }

        public override void Write(DdlRules rules, StringWriter writer)
        {
            var body = SchemaBuilder.GetSqlScript(Identifier.Schema, Identifier.Name);

            writer.WriteLine(body);
            writer.WriteLine("");
        }

        protected override string toDropSql()
        {
            return _dropSql;
        }
    }
}
