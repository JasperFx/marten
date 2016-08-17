using System;

namespace Marten.CLI.Infrastructure
{
    public interface IMessage
    {
        Guid Id { get; }
        Guid? CausationId { get; }
    }
}