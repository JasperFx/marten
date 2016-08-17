using System;
using System.Threading.Tasks;

namespace Marten.CLI.Infrastructure
{
    internal abstract class CommandBase : ICommand
    {
        internal CommandBase(Guid? causation = null)
        {
            Id = causation ?? Guid.NewGuid();            
            CausationId = causation;
        }

        public Guid Id { get; }
        public Guid? CausationId { get; }
        public abstract Task<ICommandContext> Execute(ICommandContext context);
    }
}