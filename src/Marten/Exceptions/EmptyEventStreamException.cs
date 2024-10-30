using System;
using JasperFx.Core;

namespace Marten.Exceptions;

public class EmptyEventStreamException: MartenException
{
    public EmptyEventStreamException(string key): base($"A new event stream ('{key}') cannot be started without any events")
    {
    }

    public EmptyEventStreamException(Guid id): base($"A new event stream ('{id}') cannot be started without any events")
    {
    }
}
