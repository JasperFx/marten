using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.CLI.Infrastructure
{
    internal static class CommandExtension
    {
        public static IEnumerable<ICommand> Then<T>(this IEnumerable<ICommand> command) where T : CommandBase, new()
        {
            var enumerable = command as ICommand[] ?? command.ToArray();
            var last = enumerable.Last();
            return enumerable.Concat(new[] { (ICommand)Activator.CreateInstance(typeof(T), last.Id) });
        }

        public static IEnumerable<ICommand> Then<T>(this ICommand command) where T : CommandBase
        {
            return new[] {command}.Concat(new[] {(ICommand) Activator.CreateInstance(typeof(T), command.Id)});
        }
    }
}