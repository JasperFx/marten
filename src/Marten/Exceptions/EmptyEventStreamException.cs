using System;
using Baseline;

namespace Marten.Exceptions
{
    public class EmptyEventStreamException : Exception
    {
        public static readonly string MessageTemplate =
            "A new event stream ('{0}') cannot be started without any events";

        public EmptyEventStreamException(string key) : base(MessageTemplate.ToFormat(key))
        {
        }

        public EmptyEventStreamException(Guid id) : base(MessageTemplate.ToFormat(id))
        {
        }
    }
}
