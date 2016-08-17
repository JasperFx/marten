using System;

namespace Marten.CLI.Infrastructure
{
    internal sealed class CommandDescriptor
    {        
        private readonly Func<Guid, ICommand> create;

        public CommandDescriptor(string name, string description, Func<Guid, ICommand> create)
        {
            Name = name;
            Description = description;
            this.create = create;
        }

        public static CommandDescriptor Create(string name, string description, Func<Guid, ICommand> create)
        {
            return new CommandDescriptor(name, description, create);
        }

        public string Name { get;  }
        public string Description { get; }
        public ICommand Create(Guid? commandId = null)
        {
            return create(commandId ?? Guid.NewGuid());
        }
    }
}