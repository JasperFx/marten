using System;
using Marten.CLI.Infrastructure;

namespace Marten.CLI.Commands
{
    internal sealed class DisplayHelp : CompositeCommand
    {
        public DisplayHelp(Guid id) : base(id)
        {
            this.Prerequisite<GenerateHelp>();
            this.After<RecordToStdOut>();
        }
    }
}