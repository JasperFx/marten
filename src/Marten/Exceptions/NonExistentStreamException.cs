using System;

namespace Marten.Exceptions
{
    public class NonExistentStreamException: Exception
    {
        public object Id { get; }

        public NonExistentStreamException(object id) : base((string)$"Attempt to append to a nonexistent event stream '{id}'")
        {
            Id = id;
        }

    }
}
