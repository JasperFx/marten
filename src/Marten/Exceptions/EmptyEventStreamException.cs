using System;
using JasperFx.Core;

namespace Marten.Exceptions;

public class EmptyEventStreamException: MartenException
{
    public static readonly string MessageTemplate =
        "A new event stream ('{0}') cannot be started without any events";

    public EmptyEventStreamException(string key): base(string.Format(MessageTemplate, key))
    {
    }

    public EmptyEventStreamException(Guid id): base(string.Format(MessageTemplate, id))
    {
    }
}
