using System.Threading;
using Marten.Testing.Harness;
using System.Threading.Tasks;
using Marten.Services;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_3911_use_sticky_connections_when_otel_connection_tracking_is_on : BugIntegrationContext
{
    [Fact]
    public async Task do_not_blow_up()
    {
        StoreOptions(opts => opts.OpenTelemetry.TrackConnections = TrackLevel.Verbose);

        using var conn = theSession.Connection;
    }

    [Fact]
    public async Task do_not_blow_up_starting_an_async_transaction()
    {
        StoreOptions(opts => opts.OpenTelemetry.TrackConnections = TrackLevel.Verbose);

        await theSession.BeginTransactionAsync(CancellationToken.None);

    }
}
