using System;
using Oakton;

namespace Marten.CommandLine.Commands
{
    [Description("Run the async projections daemon")]
    public class RunDaemon: MartenCommand<MartenInput>
    {
        protected override bool execute(IDocumentStore store, MartenInput input)
        {
            throw new NotImplementedException("REDO");
        }
    }
}
