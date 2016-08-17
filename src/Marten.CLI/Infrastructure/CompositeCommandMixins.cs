using System;
using System.Collections.Generic;
using Baseline;

namespace Marten.CLI.Infrastructure
{
    internal static class CompositeCommandMixins
    {
        public static ICompositeCommand Prerequisite<T>(this ICompositeCommand cmd)
        {
            var prerequisite = (ICommand) Activator.CreateInstance(typeof(T), cmd.Id);
            cmd.Prerequisites.Add(prerequisite);
            return cmd;
        }

        public static ICompositeCommand Prerequisite(this ICompositeCommand cmd, IEnumerable<ICommand> commands)
        {
            commands.Each(x => Prerequisite(cmd, x));
            return cmd;
        }

        public static ICompositeCommand Prerequisite(this ICompositeCommand cmd, ICommand command)
        {
            cmd.Prerequisites.Add(command);
            return cmd;
        }
        
        public static ICompositeCommand After<T>(this ICompositeCommand cmd)
        {
            var prerequisite = (ICommand)Activator.CreateInstance(typeof(T), cmd.Id);
            cmd.ExecuteAfter.Add(prerequisite);
            return cmd;
        }

        public static ICompositeCommand After(this ICompositeCommand cmd, IEnumerable<ICommand> commands)
        {
            commands.Each(x => cmd.After(x));
            return cmd;
        }

        public static ICompositeCommand After(this ICompositeCommand cmd, ICommand command)
        {
            cmd.ExecuteAfter.Add(command);
            return cmd;
        }
    }
}