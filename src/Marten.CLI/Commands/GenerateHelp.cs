using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten.CLI.Infrastructure;
using Baseline;
using Marten.CLI.Model;

namespace Marten.CLI.Commands
{
    internal sealed class GenerateHelp : CommandBase
    {
        public GenerateHelp(Guid? causation = null) : base(causation)
        {
        }

        public override Task<ICommandContext> Execute(ICommandContext context)
        {
            var cmds = new AvailableCommands().ToDictionary(x => x.Name, x => x);
            var menuwriter = new StringWriter();
            menuwriter.WriteLine($"<command>: <command description>{Environment.NewLine}");
            cmds.OrderBy(x => x.Key).Each(x => menuwriter.WriteLine($"{x.Key}: {x.Value.Description}"));
            var menu = menuwriter.ToString();
            context.Record(new StringRecord(menu));            
            return Task.FromResult(context);
        }
    }
}