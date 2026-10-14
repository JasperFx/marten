#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Linq.SoftDeletes;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DaemonTests.Backfills;

public record InvoiceOpened(string Customer, DateOnly Date);

public record InvoiceLineAdded(string Customer, decimal Amount);

public record InvoiceVoided;

public class InvoiceDetails
{
    public int Month { get; set; }
}

public class InvoiceSummary
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }

    // Added without a version bump
    public int? Year { get; set; }
    public int LineCount { get; set; }
    public InvoiceDetails? Details { get; set; }
}

public class CustomerSummary
{
    public string Id { get; set; } = "";
    public decimal Total { get; set; }

    // Added without a version bump
    public int LineCount { get; set; }
}

public class InvoiceSummaryBeforeTheChange: SingleStreamProjection<InvoiceSummary, Guid>
{
    public InvoiceSummaryBeforeTheChange()
    {
        Name = "InvoiceSummary";
        DeleteEvent<InvoiceVoided>();
    }

    public InvoiceSummary Create(IEvent<InvoiceOpened> e) => new() { Id = e.StreamId, Customer = e.Data.Customer };

    public void Apply(InvoiceSummary summary, InvoiceLineAdded e) => summary.Total += e.Amount;
}

public class InvoiceSummaryAfterTheChange: SingleStreamProjection<InvoiceSummary, Guid>
{
    public InvoiceSummaryAfterTheChange(): this(TimeSpan.Zero)
    {
    }

    public InvoiceSummaryAfterTheChange(TimeSpan slowness)
    {
        Name = "InvoiceSummary";
        DeleteEvent<InvoiceVoided>();

        Backfill("year-and-line-count")
            .On<InvoiceOpened>((patch, e) => patch
                .Set(x => x.Year, e.Date.Year)
                .Set(x => x.Details!.Month, e.Date.Month))
            .On<InvoiceLineAdded>((patch, _) =>
            {
                if (slowness > TimeSpan.Zero) Thread.Sleep(slowness);
                patch.Increment(x => x.LineCount);
            });
    }

    public InvoiceSummary Create(IEvent<InvoiceOpened> e) => new()
    {
        Id = e.StreamId,
        Customer = e.Data.Customer,
        Year = e.Data.Date.Year,
        Details = new InvoiceDetails { Month = e.Data.Date.Month }
    };

    public void Apply(InvoiceSummary summary, InvoiceLineAdded e)
    {
        summary.Total += e.Amount;
        summary.LineCount++;
    }
}

public class CustomerSummaryBeforeTheChange: MultiStreamProjection<CustomerSummary, string>
{
    public CustomerSummaryBeforeTheChange()
    {
        Name = "CustomerSummary";
        Identity<InvoiceLineAdded>(x => x.Customer);
    }

    public void Apply(CustomerSummary summary, InvoiceLineAdded e) => summary.Total += e.Amount;
}

public class CustomerSummaryAfterTheChange: MultiStreamProjection<CustomerSummary, string>
{
    public CustomerSummaryAfterTheChange()
    {
        Name = "CustomerSummary";
        Identity<InvoiceLineAdded>(x => x.Customer);

        Backfill("line-count").On<InvoiceLineAdded>((patch, _) => patch.Increment(x => x.LineCount));
    }

    public void Apply(CustomerSummary summary, InvoiceLineAdded e)
    {
        summary.Total += e.Amount;
        summary.LineCount++;
    }
}

public class backfilling_a_member_without_a_version_bump: OneOffConfigurationsContext
{
    private readonly List<(Guid Invoice, string Customer, int Year, int Lines)> _invoices = new();

    private static void beforeTheChange(StoreOptions opts, Action<StoreOptions>? tenancy)
    {
        tenancy?.Invoke(opts);
        opts.Projections.CompositeProjectionFor("Invoicing", x =>
        {
            x.Add(new InvoiceSummaryBeforeTheChange());
            x.Add(new CustomerSummaryBeforeTheChange());
        });
    }

    private static void afterTheChange(StoreOptions opts, Action<StoreOptions>? tenancy, TimeSpan slowness = default)
    {
        tenancy?.Invoke(opts);
        opts.Schema.For<InvoiceSummary>().Duplicate(x => x.Year);
        opts.Projections.CompositeProjectionFor("Invoicing", x =>
        {
            x.Add(new InvoiceSummaryAfterTheChange(slowness));
            x.Add(new CustomerSummaryAfterTheChange());

            // Small pages, so a backfill over a test-sized store still takes several pages alongside the live shard.
            // A backfill pages by its shard, which for a member is the composite
            x.Options.BatchSize = 5;
        });
    }

    private async Task appendInvoicesAsync(IDocumentStore store, int count, string tenantId = "*DEFAULT*")
    {
        await using var session = store.LightweightSession(tenantId);
        for (var i = 0; i < count; i++)
        {
            var invoice = Guid.NewGuid();
            var customer = $"customer-{_invoices.Count % 3}";
            var year = 2020 + _invoices.Count % 5;
            var lines = 1 + _invoices.Count % 4;

            var events = new List<object> { new InvoiceOpened(customer, new DateOnly(year, 3, 1)) };
            events.AddRange(Enumerable.Range(0, lines).Select(_ => new InvoiceLineAdded(customer, 10m)));
            session.Events.StartStream(invoice, events.ToArray());

            _invoices.Add((invoice, customer, year, lines));
        }

        await session.SaveChangesAsync();
    }

    private static async Task runDaemonToTheEndAsync(DocumentStore store)
    {
        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        // Non-stale data includes completed backfills
        await daemon.WaitForNonStaleData(60.Seconds());

        await daemon.StopAllAsync();
    }

    private async Task everyDocumentIsCompleteAsync(IDocumentStore store, string tenantId = "*DEFAULT*")
    {
        await using var query = store.QuerySession(tenantId);

        foreach (var invoice in _invoices)
        {
            var summary = await query.LoadAsync<InvoiceSummary>(invoice.Invoice);
            summary.ShouldNotBeNull();
            summary.Year.ShouldBe(invoice.Year);
            summary.Details.ShouldNotBeNull().Month.ShouldBe(3);
            summary.LineCount.ShouldBe(invoice.Lines);
            summary.Total.ShouldBe(invoice.Lines * 10m);
        }

        foreach (var customer in _invoices.GroupBy(x => x.Customer))
        {
            var summary = await query.LoadAsync<CustomerSummary>(customer.Key);
            summary.ShouldNotBeNull();
            summary.LineCount.ShouldBe(customer.Sum(x => x.Lines));
            summary.Total.ShouldBe(customer.Sum(x => x.Lines) * 10m);
        }

        // The duplicated column over a backfilled member follows the hand-over
        foreach (var invoice in _invoices)
        {
            (await scalarAsync<int?>($"select year from {SchemaName}.mt_doc_invoicesummary where id = '{invoice.Invoice}'"))
                .ShouldBe(invoice.Year);
        }
    }

    private async Task<T> scalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull or null ? default! : (T)value;
    }

    private async Task<Dictionary<string, (long Position, long Ceiling, bool Completed)>> backfillStateAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select name, position, ceiling, completed is not null from {SchemaName}.mt_projection_backfills", conn);

        var state = new Dictionary<string, (long, long, bool)>();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                state[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2), reader.GetBoolean(3));
            }
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Not created yet
        }

        return state;
    }

    [Fact]
    public async Task the_new_members_are_filled_for_the_events_the_old_code_applied()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 20);
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, null), cleanAll: false);
        await appendInvoicesAsync(after, 10); // not yet projected when the new code starts
        await runDaemonToTheEndAsync(after);

        await everyDocumentIsCompleteAsync(after);

        var state = await backfillStateAsync();
        state.Keys.ShouldBe(["InvoiceSummary:All:year-and-line-count", "CustomerSummary:All:line-count"], ignoreOrder: true);
        state.Values.ShouldAllBe(x => x.Completed && x.Position == x.Ceiling);

        (await scalarAsync<long>($"select count(*) from {SchemaName}.mt_doc_invoicesummary_backfill")).ShouldBe(0);
    }

    [Fact]
    public async Task the_live_projection_keeps_projecting_while_the_backfill_runs()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 30);
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, null, slowness: 50.Milliseconds()), cleanAll: false);
        using var daemon = await after.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var existing = _invoices.Count;
        await appendInvoicesAsync(after, 5);
        var fresh = _invoices.Skip(existing).Select(x => x.Invoice).ToArray();

        // The new invoices are projected while the backfill is still folding its first pages
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            await using var query = after.QuerySession();
            if ((await query.LoadManyAsync<InvoiceSummary>(fresh)).Count == fresh.Length) break;
            if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException("The live projection did not project the new invoices");
            await Task.Delay(50);
        }

        (await backfillStateAsync()).Values.ShouldNotContain(x => x.Completed);

        await daemon.WaitForNonStaleData(60.Seconds());
        (await backfillStateAsync()).Values.Count(x => x.Completed).ShouldBe(2);
        await daemon.StopAllAsync();

        await everyDocumentIsCompleteAsync(after);
    }

    [Fact]
    public async Task the_hand_over_restores_a_member_the_live_projection_overwrote_after_its_merge()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 30);
        await runDaemonToTheEndAsync(before);

        // Stopped halfway through the catch-up, with part of the documents merged
        var slow = StoreOptions(opts => afterTheChange(opts, null, slowness: 50.Milliseconds()), cleanAll: false);
        using (var daemon = await slow.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();

            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (!(await backfillStateAsync()).TryGetValue("InvoiceSummary:All:year-and-line-count", out var state) || state.Position == 0)
            {
                if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException("The backfill did not start");
                await Task.Delay(50);
            }

            await daemon.StopAllAsync();
        }

        (await scalarAsync<long>($"select count(*) from {SchemaName}.mt_doc_invoicesummary_backfill where merged_xmin is not null"))
            .ShouldBeGreaterThan(0);

        // What a live write from a stale aggregate does to a member the backfill already merged
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $"update {SchemaName}.mt_doc_invoicesummary set data = jsonb_set(data, '{{LineCount}}', '999')", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var after = StoreOptions(opts => afterTheChange(opts, null), cleanAll: false);
        await appendInvoicesAsync(after, 1);
        await runDaemonToTheEndAsync(after);

        await everyDocumentIsCompleteAsync(after);
    }

    [Fact]
    public async Task a_completed_backfill_does_not_run_again()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 10);
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, null), cleanAll: false);
        await appendInvoicesAsync(after, 2);
        await runDaemonToTheEndAsync(after);

        // Restarted after the hand-over with new events: an Increment applied twice would show in every count
        await appendInvoicesAsync(after, 10);
        await runDaemonToTheEndAsync(after);

        await everyDocumentIsCompleteAsync(after);
    }

    [Fact]
    public async Task a_projection_that_starts_from_nothing_records_the_backfill_as_done_without_work()
    {
        var store = StoreOptions(opts => afterTheChange(opts, null));
        await appendInvoicesAsync(store, 10);
        await runDaemonToTheEndAsync(store);

        await everyDocumentIsCompleteAsync(store);

        var state = await backfillStateAsync();
        state["InvoiceSummary:All:year-and-line-count"].ShouldBe((0, 0, true));
        state["CustomerSummary:All:line-count"].ShouldBe((0, 0, true));
    }

    [Fact]
    public async Task a_rebuild_settles_the_backfill_because_the_new_code_replays_everything()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 10);
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, null), cleanAll: false);
        using (var daemon = await after.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync("Invoicing", 60.Seconds(), default);
        }

        await everyDocumentIsCompleteAsync(after);
        (await backfillStateAsync()).Values.ShouldAllBe(x => x.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_deleted_document_is_not_patched(bool softDeleted)
    {
        void deletes(StoreOptions opts)
        {
            if (softDeleted) opts.Schema.For<InvoiceSummary>().SoftDeleted();
        }

        var before = StoreOptions(opts => beforeTheChange(opts, deletes));
        await appendInvoicesAsync(before, 10);
        var voided = _invoices.Take(3).ToArray();

        if (softDeleted)
        {
            // A projection's delete event removes the row; a soft-deleted projected document comes from the application
            await runDaemonToTheEndAsync(before);
            await using var session = before.LightweightSession();
            foreach (var invoice in voided) session.Delete<InvoiceSummary>(invoice.Invoice);
            await session.SaveChangesAsync();
        }
        else
        {
            await using (var session = before.LightweightSession())
            {
                foreach (var invoice in voided) session.Events.Append(invoice.Invoice, new InvoiceVoided());
                await session.SaveChangesAsync();
            }

            await runDaemonToTheEndAsync(before);
        }

        var after = StoreOptions(opts => afterTheChange(opts, deletes), cleanAll: false);
        await appendInvoicesAsync(after, 2);
        await runDaemonToTheEndAsync(after);

        await using var query = after.QuerySession();
        foreach (var invoice in voided)
        {
            if (softDeleted)
            {
                var row = await query.Query<InvoiceSummary>().Where(x => x.IsDeleted() && x.Id == invoice.Invoice)
                    .SingleAsync();
                row.Year.ShouldBeNull();
                row.LineCount.ShouldBe(0);
            }
            else
            {
                // Not brought back by a patch either
                (await scalarAsync<long>($"select count(*) from {SchemaName}.mt_doc_invoicesummary where id = '{invoice.Invoice}'"))
                    .ShouldBe(0);
            }
        }

        _invoices.RemoveAll(x => voided.Contains(x));
        foreach (var invoice in _invoices)
        {
            var summary = await query.LoadAsync<InvoiceSummary>(invoice.Invoice);
            summary.ShouldNotBeNull().LineCount.ShouldBe(invoice.Lines);
        }
    }

    [Fact]
    public async Task each_tenant_shard_backfills_its_own_documents()
    {
        Action<StoreOptions> tenancy = opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Policies.AllDocumentsAreMultiTenanted();
        };

        var before = StoreOptions(opts => beforeTheChange(opts, tenancy));
        await before.Advanced.AddMartenManagedTenantsAsync(default, "red", "blue");
        await appendInvoicesAsync(before, 10, "red");
        var redInvoices = _invoices.ToList();
        _invoices.Clear();
        await appendInvoicesAsync(before, 10, "blue");
        var blueInvoices = _invoices.ToList();
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, tenancy), cleanAll: false);

        _invoices.Clear();
        _invoices.AddRange(redInvoices);
        await appendInvoicesAsync(after, 2, "red");
        redInvoices = _invoices.ToList();

        _invoices.Clear();
        _invoices.AddRange(blueInvoices);
        await appendInvoicesAsync(after, 2, "blue");
        blueInvoices = _invoices.ToList();

        await runDaemonToTheEndAsync(after);

        _invoices.Clear();
        _invoices.AddRange(redInvoices);
        await everyDocumentIsCompleteAsync(after, "red");

        _invoices.Clear();
        _invoices.AddRange(blueInvoices);
        await everyDocumentIsCompleteAsync(after, "blue");

        var state = await backfillStateAsync();
        state.Keys.ShouldBe(new[]
        {
            "InvoiceSummary:All:red:year-and-line-count", "InvoiceSummary:All:blue:year-and-line-count",
            "CustomerSummary:All:red:line-count", "CustomerSummary:All:blue:line-count"
        }, ignoreOrder: true);
        state.Values.ShouldAllBe(x => x.Completed);
    }

    [Fact]
    public async Task waiting_for_non_stale_data_waits_for_the_backfill_even_when_nothing_new_happened()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 20);
        await runDaemonToTheEndAsync(before);

        // The progression is already at the high water mark, so only the backfill can keep the wait open
        var after = StoreOptions(opts => afterTheChange(opts, null, slowness: 20.Milliseconds()), cleanAll: false);
        await runDaemonToTheEndAsync(after);

        (await backfillStateAsync()).Values.Count(x => x.Completed).ShouldBe(2);
        await everyDocumentIsCompleteAsync(after);
    }

    [Fact]
    public async Task a_timeout_names_the_backfills_that_did_not_complete()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 30);
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, null, slowness: 200.Milliseconds()), cleanAll: false);
        using var daemon = await after.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var timeout = await Should.ThrowAsync<TimeoutException>(() => after.WaitForNonStaleProjectionDataAsync(2.Seconds()));
        timeout.Message.ShouldContain("InvoiceSummary:All:year-and-line-count");

        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task waiting_for_one_document_type_waits_for_its_backfill()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 20);
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, null, slowness: 20.Milliseconds()), cleanAll: false);
        await appendInvoicesAsync(after, 1);
        using var daemon = await after.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await after.Storage.Database.WaitForNonStaleProjectionDataAsync(typeof(InvoiceSummary), 60.Seconds(),
            TestContext.Current.CancellationToken);

        (await backfillStateAsync())["InvoiceSummary:All:year-and-line-count"].Completed.ShouldBeTrue();
        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task a_forced_catch_up_backfills_as_well()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 20);
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, null), cleanAll: false);
        await appendInvoicesAsync(after, 2);
        using (var daemon = await after.BuildProjectionDaemonAsync())
        {
            await daemon.CatchUpAsync(60.Seconds(), TestContext.Current.CancellationToken);
        }

        await everyDocumentIsCompleteAsync(after);
        (await backfillStateAsync()).Values.Count(x => x.Completed).ShouldBe(2);
    }

    [Fact]
    public async Task where_each_backfill_stands_can_be_read_from_the_store()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 10);
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, null), cleanAll: false);
        await runDaemonToTheEndAsync(after);

        var states = await after.Advanced.AllProjectionBackfills(token: TestContext.Current.CancellationToken);
        states.Select(x => x.Name).ShouldBe(["CustomerSummary:All:line-count", "InvoiceSummary:All:year-and-line-count"]);
        states.ShouldAllBe(x => x.IsCompleted && x.Position == x.Ceiling);
    }
    [Fact]
    public async Task the_backfill_progress_is_published_on_the_shard_state_for_monitoring()
    {
        var before = StoreOptions(opts => beforeTheChange(opts, null));
        await appendInvoicesAsync(before, 30);
        await runDaemonToTheEndAsync(before);

        var after = StoreOptions(opts => afterTheChange(opts, null), cleanAll: false);
        using var daemon = await after.BuildProjectionDaemonAsync();

        var published = new ConcurrentQueue<BackfillProgress>();
        using var subscription = daemon.Tracker.Subscribe(new BackfillRecorder(published));

        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(60.Seconds());
        await daemon.StopAllAsync();

        var byName = published.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.ToList());
        byName.Keys.Order().ShouldBe(["CustomerSummary:All:line-count", "InvoiceSummary:All:year-and-line-count"]);

        foreach (var progress in byName.Values)
        {
            progress.ShouldContain(x => !x.Completed && x.Position < x.Ceiling);
            progress.ShouldContain(x => x.Completed && x.Position == x.Ceiling);
            progress.ShouldAllBe(x => x.Failure == null);
        }
    }

    private sealed class BackfillRecorder(ConcurrentQueue<BackfillProgress> published): IObserver<ShardState>
    {
        public void OnNext(ShardState value)
        {
            foreach (var backfill in value.Backfills ?? []) published.Enqueue(backfill);
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
