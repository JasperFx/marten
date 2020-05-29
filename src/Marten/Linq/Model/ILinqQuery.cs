using System;
using Marten.Util;
using Remotion.Linq;

namespace Marten.Linq.Model
{
    public interface ILinqQuery
    {
        QueryModel Model { get; }
        Type SourceType { get; }

        void ConfigureCommand(CommandBuilder command);

        void ConfigureCommand(CommandBuilder command, int limit);

        void ConfigureCount(CommandBuilder command);

        void ConfigureAny(CommandBuilder command);

        void ConfigureAggregate(CommandBuilder command, string @operator);
    }
}
