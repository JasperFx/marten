using System;

namespace Marten.Events
{
    public interface IAggregate
    {
        Guid Id { get; set; }
    }
}