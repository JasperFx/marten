using System;
using System.Collections.Generic;
using System.Linq;
using Marten.CLI.Commands;
using Marten.CLI.Infrastructure;

namespace Marten.CLI
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            var cmds = new AvailableCommands().ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);

            if (!args.Any())
            {
                RunInteractive(cmds);
            }
            else
            {
                RunNonInteractive(cmds, args);
            }       
        }
 
        private static void RunInteractive(IDictionary<string, CommandDescriptor> cmds)
        {
            var help = cmds[CommandContracts.Help].Create(Guid.NewGuid());
            
            Console.WriteLine("-- Marten CLI --");

            while (true)
            {
                help.Execute(new InteractiveContext());                
                var consolecmd = Console.ReadLine();
                CommandDescriptor cmdDescriptor;
                if (cmds.TryGetValue(consolecmd, out cmdDescriptor))
                {
                    Console.WriteLine($"Executing {cmdDescriptor.Name}");
                    var cmd = cmdDescriptor.Create(Guid.NewGuid());                    
                    try
                    {
                        using (var ctx = new InteractiveContext())
                        {
                            cmd.Execute(ctx).Wait();
                        }
                    }
                    catch (AggregateException e)
                    {
                        Console.WriteLine(
                            $"\tCommand {cmd} failed: {string.Join(Environment.NewLine, e.InnerExceptions.Select(x => x.Message))}");
                    }
                    continue;
                }
                Console.WriteLine($"Unknown command {consolecmd}");
            }
        }

        private static void RunNonInteractive(Dictionary<string, CommandDescriptor> cmds, string[] args)
        {
            using (var ctx = new NonInteractiveContext(args))
            {
                // incomplete
            }
        }
    }
}
