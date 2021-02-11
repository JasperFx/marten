using System;

namespace Marten.Exceptions
{
    public class StreamLockedException : Exception
    {
        public StreamLockedException(object streamId, Exception innerException) : base($"Stream '{streamId}' may be locked for updates")
        {
        }
    }
}
