using System;

namespace Marten.Events
{
    public interface IEvent
    {
        Guid Id { get; set; }
    }
}