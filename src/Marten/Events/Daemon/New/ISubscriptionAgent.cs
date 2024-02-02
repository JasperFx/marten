using System;

namespace Marten.Events.Daemon.New;

public interface ISubscriptionAgent : IShardAgent
{
    void MarkSuccess(long processedCeiling);
}
