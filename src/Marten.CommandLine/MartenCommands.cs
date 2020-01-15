using System;
using System.Reflection;
using Baseline;
using Oakton;

namespace Marten.CommandLine
{
    public class MartenCommands
    {
        public static CommandExecutor For(StoreOptions options, Action<CommandFactory> configure)
        {
            var executor = CommandExecutor.For(factory =>
            {
                factory.RegisterCommands(typeof(MartenCommands).GetTypeInfo().Assembly);
                configure(factory);
                factory.ConfigureRun = run =>
                {
                    if (!(run.Input is MartenInput))
                        return;

                    run.Input.As<MartenInput>().Options = options;
                };
            });

            return executor;
        }

        public static int Execute(StoreOptions options, string[] args)
        {
            var executor = For(options, f => { });
            return executor.Execute(args);
        }
    }
}
