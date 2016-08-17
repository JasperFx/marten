using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Marten.CLI.Infrastructure
{
    internal abstract class CompositeCommand : CommandBase, ICompositeCommand
    {
        public IList<ICommand> Prerequisites { get; }
        public IList<ICommand> ExecuteAfter { get; }

        protected CompositeCommand(Guid id) : base(id)
        {            
            Prerequisites = new List<ICommand>();
            ExecuteAfter = new List<ICommand>();
        }

        protected virtual Task<ICommandContext> BeforePrerequisites(ICommandContext context)
        {
            return Task.FromResult(context);
        }

        protected virtual Task<ICommandContext> AfterPrerequisites(ICommandContext context)
        {
            return Task.FromResult(context);
        }

        public override async Task<ICommandContext> Execute(ICommandContext context)
        {
            context = await BeforePrerequisites(context);           
            foreach (var e in Prerequisites)
            {
                context = await e.Execute(context);
            }
            context = await AfterPrerequisites(context);
            foreach (var e in ExecuteAfter)
            {
                context = await e.Execute(context);
            }            
            return context;
        }        
    }
}