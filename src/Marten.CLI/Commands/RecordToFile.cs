using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten.CLI.Infrastructure;
using Marten.CLI.Model;

namespace Marten.CLI.Commands
{
    internal sealed class RecordToFile : CommandBase
    {
        public RecordToFile(Guid? causation = null) : base(causation)
        {
        }        

        public override Task<ICommandContext> Execute(ICommandContext context)
        {
            var records = context.All<StringRecord>();

            if (records.Any())
            {
                var path = context.Ask("output", "Output file path");
                var entries = string.Join(Environment.NewLine, records.Select(x => x.Value));
                File.WriteAllText(path, entries);
            }            

            return Task.FromResult(context);
        }
    }
}