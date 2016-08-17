using System.Collections.Generic;

namespace Marten.CLI.Infrastructure
{
    internal interface ICompositeCommand : ICommand
    {
        IList<ICommand> Prerequisites { get; }
        IList<ICommand> ExecuteAfter { get; }
    }
}