using System;
using System.Threading.Tasks;
using Marten.CLI.Infrastructure;
using Marten.CLI.Model;

namespace Marten.CLI.Commands
{
    internal sealed class WipeMartenObjects : CompositeCommand
    {
        public WipeMartenObjects() : this(Guid.NewGuid())
        {
        }

        public WipeMartenObjects(Guid id) : base(id)
        {
            this.Prerequisite<OpenStore>();
            this.After<RecordToStdOut>();
        }

        protected override Task<ICommandContext> AfterPrerequisites(ICommandContext context)
        {
            var store = context.Single<IDocumentStore>();

            return Task.Factory.StartNew(() =>
            {
                store.Advanced.Clean.CompletelyRemoveAll();
                context.Record(new StringRecord("Wiped all Marten objects from the store"));
                return context;
            });
        }
    }
}