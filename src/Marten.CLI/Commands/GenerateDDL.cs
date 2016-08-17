using System;
using System.Threading.Tasks;
using Marten.CLI.Infrastructure;
using Marten.CLI.Model;

namespace Marten.CLI.Commands
{
    internal sealed class GenerateDDL : CompositeCommand
    {
        public GenerateDDL(Guid id) : base(id)
        {
            this.Prerequisite<OpenStore>();
            this.After<RecordToStdOut>().After<RecordToFile>();
        }

        protected override Task<ICommandContext> AfterPrerequisites(ICommandContext context)
        {
            var store = context.Single<IDocumentStore>();

            return Task.Factory.StartNew(() =>
            {
                var schema = store.Schema.ToDDL();
                context.Record(new StringRecord(schema));
                return context;
            });
        }
    }
}