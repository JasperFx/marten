using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Bugs;

// Regression for marten#4730 (fixed upstream in JasperFx 2.9.10, jasperfx#444).
//
// A stream with an out-of-order (Update-before-Create) sequence throws inside the projection's
// Apply. That exception used to cause an UNRELATED, correctly-ordered "control" stream in the same
// page to have its Update applied TWICE. Root cause: the in-memory aggregate cache was written
// during batch build (before commit); when the poison event triggered a skip-and-rebuild, the
// rebuild read the already-mutated control aggregate back from the cache and re-applied its events.
//
// The fix turned the cache off by default AND made it populate only after a successful commit.
// This test guards BOTH: it runs with the cache disabled (the default) and explicitly enabled, and
// asserts the control stream's Update is applied exactly once either way.
public class Bug_4730_double_apply_with_out_of_order_stream: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public Bug_4730_double_apply_with_out_of_order_stream(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(0)]    // cache disabled (the post-#4730 default)
    [InlineData(1000)] // cache enabled — guards the "populate only on commit" half of the fix
    public async Task control_stream_update_applied_exactly_once_despite_out_of_order_sibling(int cacheLimitPerTenant)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new AccountProjection4730 { Options = { CacheLimitPerTenant = cacheLimitPerTenant } },
                ProjectionLifecycle.Async);
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamOutOfOrder = Guid.NewGuid();
        var streamControl = Guid.NewGuid();
        var delta = 123.45m;
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-1);

        // Out-of-order stream: Update appended before Create -> the projection's Apply throws.
        theSession.Events.StartStream(streamOutOfOrder,
            new Updated4730(streamOutOfOrder, delta, t1),
            new Created4730(streamOutOfOrder, t0));

        // Control stream: correct order. Its Update must be applied exactly once.
        theSession.Events.StartStream(streamControl,
            new Created4730(streamControl, t0),
            new Updated4730(streamControl, delta, t1));

        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        try
        {
            await daemon.WaitForNonStaleData(15.Seconds());
        }
        catch (Exception e)
        {
            // The out-of-order stream's poison event is skipped+dead-lettered; non-stale wait can still
            // time out in some runs. The assertion below is what matters.
            _output.WriteLine($"WaitForNonStaleData threw: {e.GetType().Name}: {e.Message}");
        }

        await using var query = theStore.QuerySession();
        var control = await query.LoadAsync<AccountReadModel4730>(streamControl);

        _output.WriteLine($"cacheLimit={cacheLimitPerTenant} control TotalDelta actual={control?.TotalDelta} expected={delta}");

        control.ShouldNotBeNull();
        control.TotalDelta.ShouldBe(delta);
    }
}

public record Created4730(Guid Id, DateTimeOffset CreatedAt);

public record Updated4730(Guid Id, decimal Delta, DateTimeOffset UpdatedAt);

public class AccountReadModel4730
{
    public Guid Id { get; set; }
    public decimal TotalDelta { get; set; }
}

public partial class AccountProjection4730: SingleStreamProjection<AccountReadModel4730, Guid>
{
    public AccountReadModel4730 Create(Created4730 e) => new() { Id = e.Id, TotalDelta = 0m };

    public void Apply(Updated4730 e, AccountReadModel4730 model)
    {
        if (model == null || model.Id == Guid.Empty)
            throw new InvalidOperationException("Update received before creation event.");

        model.TotalDelta += e.Delta;
    }
}
