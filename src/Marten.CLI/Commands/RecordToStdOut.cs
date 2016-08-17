using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.CLI.Infrastructure;
using Marten.CLI.Model;

namespace Marten.CLI.Commands
{
    internal sealed class RecordToStdOut : CommandBase
    {
        public RecordToStdOut(Guid? causation = null) : base(causation)
        {
        }        

        public override Task<ICommandContext> Execute(ICommandContext context)
        {
            var records = context.All<StringRecord>();

            Console.Out.WriteLine(string.Join(Environment.NewLine, records.Select(x => x.Value)));

            return Task.FromResult(context);
        }
    }
}