using System.Collections;
using System.Collections.Generic;
using Marten.CLI.Infrastructure;

namespace Marten.CLI.Commands
{
    internal sealed class CommandContracts
    {
        public readonly string Value;

        private bool Equals(CommandContracts other)
        {
            return string.Equals(Value, other.Value, System.StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is CommandContracts && Equals((CommandContracts)obj);
        }

        public override int GetHashCode()
        {
            return Value?.ToLowerInvariant()?.GetHashCode() ?? 0;
        }        

        public static implicit operator string(CommandContracts item)
        {
            return item.Value;
        }

        private CommandContracts(string value)
        {
            Value = value;
        }

        public static readonly CommandContracts WAM = new CommandContracts("WAM");
        public static readonly CommandContracts DDL = new CommandContracts("DDL");
        public static readonly CommandContracts Help = new CommandContracts("Help");
    }
    internal sealed class AvailableCommands : IEnumerable<CommandDescriptor>
    {
        private readonly List<CommandDescriptor> commands = new List<CommandDescriptor>()
        {
            CommandDescriptor.Create(CommandContracts.WAM, "Wiped all Marten objects from the store", id => new WipeMartenObjects(id)),
            CommandDescriptor.Create(CommandContracts.DDL, "Generate SQL statements from Marten schema", id => new GenerateDDL(id)),
            CommandDescriptor.Create(CommandContracts.Help, "Display help", id => new DisplayHelp(id))
        };

        public IEnumerator<CommandDescriptor> GetEnumerator()
        {
            return commands.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}