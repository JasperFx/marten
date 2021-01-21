using System.IO;
using Marten.Schema;

namespace Marten.Storage
{
    public class SystemFunction: Function
    {
        private readonly string _args;
        private readonly string _dropSql;
        protected readonly DbObjectName _function;

        public SystemFunction(StoreOptions options, string functionName, string args, bool isRemoved=false)
            : this(options.DatabaseSchemaName, functionName, args, isRemoved)
        {
        }

        public SystemFunction(string schema, string functionName, string args, bool isRemoved=false)
            : base(new DbObjectName(schema, functionName), isRemoved)
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
