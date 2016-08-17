using System;
using System.Threading.Tasks;
using Marten.CLI.Infrastructure;

namespace Marten.CLI.Commands
{    
    internal sealed class OpenStore : CommandBase
    {
        public OpenStore(Guid causation) : base(causation)
        {
        }

        public override Task<ICommandContext> Execute(ICommandContext context)
        {
            var cstring = context.Ask("store", "Give connection string for storage initialization");

            return Task.Factory.StartNew(() =>
            {
                var store = DocumentStore.For(c =>
                {
                    c.Connection(cstring);
                });

                context.Record(store);

                return context;
            });
        }
    }
}