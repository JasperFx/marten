using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Patching;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Composites;

/// <summary>
/// An <see cref="EventProjection" /> member of a composite runs through JasperFx's
/// <c>ProjectionExecution</c>, which disposed the batch it was handed. Inside a composite that batch is
/// the parent's shared batch, so a member released it while later stages and the composite's own flush
/// still had operations to write: a stage-2 Patch was silently dropped, writes from a stage after an
/// EventProjection were lost, and once JasperFx 2.57 stopped running Block actions inline every batch
/// faulted with an ObjectDisposedException. The aggregation and IProjection members were never affected,
/// which is why the other composite tests did not catch it.
/// </summary>
public class event_projection_members_write_into_the_shared_batch: OneOffConfigurationsContext
{
    private void configure(bool eventProjectionFirst = false) => StoreOptions(opts =>
    {
        opts.Schema.For<SharedBatchLine>().Duplicate(x => x.InvoiceId).Duplicate(x => x.Customer);

        opts.Projections.CompositeProjectionFor("SharedBatch", composite =>
        {
            if (eventProjectionFirst)
            {
                composite.Add<SharedBatchAuditProjection>();
                composite.Snapshot<SharedBatchInvoice>(2);
            }
            else
            {
                composite.Snapshot<SharedBatchInvoice>();
                composite.Add<SharedBatchLineProjection>(2);
            }
        });
    });

    private async Task catchUp()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());
        await daemon.StopAllAsync();
    }

    private async Task<SharedBatchLine[]> lines()
    {
        await using var query = theStore.QuerySession();
        return (await query.Query<SharedBatchLine>().ToListAsync()).OrderBy(x => x.Amount).ToArray();
    }

    [Fact]
    public async Task a_stage_two_event_projection_writes_rows_from_the_upstream_snapshot()
    {
        configure();

        var invoiceId = Guid.NewGuid();
        theSession.Events.StartStream<SharedBatchInvoice>(invoiceId, new SharedBatchInvoiceOpened("Acme", [10m, 20m]));
        await theSession.SaveChangesAsync();
        await catchUp();

        (await lines()).Select(x => x.Amount).ShouldBe([10m, 20m]);

        theSession.Events.Append(invoiceId, new SharedBatchAmountsChanged([5m]));
        await theSession.SaveChangesAsync();
        await catchUp();

        (await lines()).Select(x => x.Amount).ShouldBe([5m]);
    }

    [Fact]
    public async Task a_stage_two_event_projection_patch_is_applied()
    {
        configure();

        theSession.Events.StartStream<SharedBatchInvoice>(Guid.NewGuid(), new SharedBatchInvoiceOpened("Acme", [10m]));
        theSession.Events.StartStream<SharedBatchInvoice>(Guid.NewGuid(), new SharedBatchInvoiceOpened("Other", [20m]));
        await theSession.SaveChangesAsync();
        await catchUp();

        theSession.Events.Append(Guid.NewGuid(), new SharedBatchCustomerFlagged("Acme", "overdue"));
        await theSession.SaveChangesAsync();
        await catchUp();

        (await lines()).Select(x => x.Flag).ShouldBe(["overdue", null]);
    }

    [Fact]
    public async Task a_stage_after_an_event_projection_still_writes()
    {
        configure(eventProjectionFirst: true);

        var invoiceId = Guid.NewGuid();
        theSession.Events.StartStream<SharedBatchInvoice>(invoiceId, new SharedBatchInvoiceOpened("Acme", [10m]));
        await theSession.SaveChangesAsync();
        await catchUp();

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<SharedBatchAudit>(invoiceId)).ShouldNotBeNull();
        (await query.LoadAsync<SharedBatchInvoice>(invoiceId)).ShouldNotBeNull();
    }
}

public record SharedBatchInvoiceOpened(string Customer, decimal[] Amounts);
public record SharedBatchAmountsChanged(decimal[] Amounts);
public record SharedBatchCustomerFlagged(string Customer, string Flag);

public class SharedBatchInvoice
{
    public Guid Id { get; set; }
    public string Customer { get; set; }
    public decimal[] Amounts { get; set; } = [];

    public void Apply(SharedBatchInvoiceOpened e)
    {
        Customer = e.Customer;
        Amounts = e.Amounts;
    }

    public void Apply(SharedBatchAmountsChanged e) => Amounts = e.Amounts;
}

public class SharedBatchLine
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string Customer { get; set; }
    public decimal Amount { get; set; }
    public string? Flag { get; set; }
}

public class SharedBatchAudit
{
    public Guid Id { get; set; }
}

public partial class SharedBatchLineProjection: EventProjection
{
    public void Project(Updated<SharedBatchInvoice> updated, IDocumentOperations ops)
    {
        var invoice = updated.Entity;
        ops.DeleteWhere<SharedBatchLine>(x => x.InvoiceId == invoice.Id);
        foreach (var amount in invoice.Amounts)
        {
            ops.Store(new SharedBatchLine
            {
                Id = Guid.NewGuid(), InvoiceId = invoice.Id, Customer = invoice.Customer, Amount = amount
            });
        }
    }

    public void Project(SharedBatchCustomerFlagged e, IDocumentOperations ops)
        => ops.Patch<SharedBatchLine>(x => x.Customer == e.Customer).Set(x => x.Flag, e.Flag);
}

public partial class SharedBatchAuditProjection: EventProjection
{
    public void Project(IEvent<SharedBatchInvoiceOpened> e, IDocumentOperations ops)
        => ops.Store(new SharedBatchAudit { Id = e.StreamId });
}
