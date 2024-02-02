using System;

namespace Marten.Events.Daemon.New;

public interface ISubscriptionAgent
{
    void Pause(TimeSpan time);
    void MarkSuccess(long processedCeiling);
}