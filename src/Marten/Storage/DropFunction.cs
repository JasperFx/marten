using System.Data.Common;
using System.IO;
using Marten.Schema;

namespace Marten.Storage
{
    public class DropFunction: SystemFunction
    {
        public DropFunction(string schema, string functionName, string args) :
            base(schema, functionName, args, true)
        {
        }

        public override void Write(DdlRules rules, StringWriter writer)
        {
            writer.WriteLine($"drop function if exists {_function.Schema}.{Name} cascade;");
        }
    }
}
