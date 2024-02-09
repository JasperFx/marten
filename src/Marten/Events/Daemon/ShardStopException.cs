using System;
using Marten.Exceptions;

namespace Marten.Events.Daemon;

/// <summary>
///     A projection shard failed to stop in a timely manner
/// </summary>
public class ShardStopException: MartenException
{
    public ShardStopException(string projectionIdentity, Exception innerException): base(
        $"Failure while trying to stop '{projectionIdentity}'", innerException)
    {
    }
}


