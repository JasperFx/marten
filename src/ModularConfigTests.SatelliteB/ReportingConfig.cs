using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;

namespace ModularConfigTests.SatelliteB;

/// <summary>
/// Satellite-owned <see cref="IAsyncConfigureMarten"/> that registers
/// this assembly's MultiStreamProjection with the host's StoreOptions.
/// Composes with the sync <see cref="IConfigureMarten"/> from SatelliteA
/// via DI — the chip's async-compose pin test asserts both contributions
/// appear in the final store options.
/// </summary>
public class ReportingConfig : IAsyncConfigureMarten
{
    public ValueTask Configure(StoreOptions options, CancellationToken cancellationToken)
    {
        options.Projections.Add<DailyProjection>(ProjectionLifecycle.Inline);
        return ValueTask.CompletedTask;
    }
}
