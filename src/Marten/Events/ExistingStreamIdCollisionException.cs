using System;

namespace Marten.Events
{
    public class ExistingStreamIdCollisionException : Exception
    {
        public ExistingStreamIdCollisionException(object id) : base((string) $"Stream #{id} already exists in the database")
        {
        }
    }
}