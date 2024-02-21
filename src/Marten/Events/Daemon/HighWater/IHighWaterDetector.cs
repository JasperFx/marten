using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon.HighWater;

public interface IHighWaterDetector
{
    Task<HighWaterStatistics> DetectInSafeZone(CancellationToken token);
    Task<HighWaterStatistics> Detect(CancellationToken token);
    string DatabaseName { get; }
}
