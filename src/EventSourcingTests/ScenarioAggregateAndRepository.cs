using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

[Collection("string_identified_streams")]
public class ScenarioAggregateAndRepository:
    StoreContext<StringIdentifiedStreamsFixture>,
    IAsyncLifetime
{
    public ScenarioAggregateAndRepository(StringIdentifiedStreamsFixture fixture): base(fixture)
    {
    }

    public Task InitializeAsync()
    {
        return theStore.Advanced.Clean.DeleteAllEventDataAsync();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CanStoreAndHydrateAggregate()
    {
        var invoice = CreateInvoice();

        #region sample_scenario-aggregate-storeandreadinvoice

        var repository = new AggregateRepository(theStore);

        await repository.StoreAsync(invoice);

        var invoiceFromRepository = await repository.LoadAsync<Invoice>(invoice.Id);

        Assert.Equal(invoice.ToString(), invoiceFromRepository.ToString());
        Assert.Equal(invoice.Total, invoiceFromRepository.Total);

        #endregion
    }

    [Fact]
    public async Task CanStoreAndHydrateAggregatePreviousVersion()
    {
        var repository = new AggregateRepository(theStore);

        var invoice = CreateInvoice();

        await repository.StoreAsync(invoice);

        #region sample_scenario-aggregate-versionedload

        var invoiceFromRepository = await repository.LoadAsync<Invoice>(invoice.Id, 2);

        Assert.Equal(124, invoiceFromRepository.Total);

        #endregion
    }

    [Fact]
    public async Task CanGuardVersion()
    {
        var repository = new AggregateRepository(theStore);

        #region sample_scenario-aggregate-conflict

        var invoice = CreateInvoice();
        var invoiceWithSameIdentity = CreateInvoice();

        await repository.StoreAsync(invoice);

        await Assert.ThrowsAsync<EventStreamUnexpectedMaxEventIdException>(() =>
            repository.StoreAsync(invoiceWithSameIdentity)
        );

        #endregion
    }

    [Fact]
    public async Task CanRetrieveVersion()
    {
        var repository = new AggregateRepository(theStore);

        var invoice = CreateInvoice();
        invoice.Version.ShouldBe(3);
        await repository.StoreAsync(invoice);

        // Assert version was incremented properly
        var invoiceFromRepository = await repository.LoadAsync<Invoice>(invoice.Id);
        invoiceFromRepository.Version.ShouldBe(3);

        // Update aggregate
        invoiceFromRepository.AddLine(100, 23, "Some nice product with 23% VAT");
        await repository.StoreAsync(invoiceFromRepository);

        // Assert version was incremented properly
        invoiceFromRepository = await repository.LoadAsync<Invoice>(invoice.Id);
        invoiceFromRepository.Version.ShouldBe(4);
    }

    private static Invoice CreateInvoice()
    {
        #region sample_scenario-aggregate-createinvoice

        var invoice = new Invoice(42);

        invoice.AddLine(100, 24, "Joo Janta 200 Super-Chromatic Peril Sensitive Sunglasses");
        invoice.AddLine(200, 16, "Happy Vertical People Transporter");

        #endregion

        return invoice;
    }
}

#region sample_scenario-aggregate-invoice

public sealed class Invoice: AggregateBase
{
    public Invoice(int invoiceNumber)
    {
        if (invoiceNumber <= 0)
        {
            throw new ArgumentException("Invoice number needs to be positive", nameof(invoiceNumber));
        }

        // Instantiation creates our initial event, capturing the invoice number
        var @event = new InvoiceCreated(invoiceNumber);

        // Call Apply to mutate state of aggregate based on event
        Apply(@event);

        // Add the event to uncommitted events to use it while persisting the events to Marten events store
        AddUncommittedEvent(@event);
    }

    private Invoice()
    {
    }

    // Enforce any contracts on input, then raise event capturing the data
    public void AddLine(decimal price, decimal vat, string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            throw new ArgumentException("Description cannot be empty", nameof(description));
        }

        var @event = new LineItemAdded(price, vat, description);

        // Call Apply to mutate state of aggregate based on event
        Apply(@event);

        // Add the event to uncommitted events to use it while persisting the events to Marten events store
        AddUncommittedEvent(@event);
    }

    public override string ToString()
    {
        var lineItems = string.Join(Environment.NewLine, lines.Select(x => $"{x.Item1}: {x.Item2} ({x.Item3}% VAT)"));
        return $"{lineItems}{Environment.NewLine}Total: {Total}";
    }

    public decimal Total { get; private set; }

    private readonly List<Tuple<string, decimal, decimal>> lines = new List<Tuple<string, decimal, decimal>>();

    // Apply the deltas to mutate our state
    private void Apply(InvoiceCreated @event)
    {
        Id = @event.InvoiceNumber.ToString(CultureInfo.InvariantCulture);

        // Ensure to update version on every Apply method.
        Version++;
    }

    // Apply the deltas to mutate our state
    private void Apply(LineItemAdded @event)
    {
        var price = @event.Price * (1 + @event.Vat / 100);
        Total += price;
        lines.Add(Tuple.Create(@event.Description, price, @event.Vat));

        // Ensure to update version on every Apply method.
        Version++;
    }
}

#endregion

#region sample_scenario-aggregate-events

public sealed class InvoiceCreated
{
    public int InvoiceNumber { get; }

    public InvoiceCreated(int invoiceNumber)
    {
        InvoiceNumber = invoiceNumber;
    }
}

public sealed class LineItemAdded
{
    public decimal Price { get; }
    public decimal Vat { get; }
    public string Description { get; }

    public LineItemAdded(decimal price, decimal vat, string description)
    {
        Price = price;
        Vat = vat;
        Description = description;
    }
}

#endregion

#region sample_scenario-aggregate-base

// Infrastructure to capture modifications to state in events
public abstract class AggregateBase
{
    // For indexing our event streams
    public string Id { get; protected set; }

    // For protecting the state, i.e. conflict prevention
    // The setter is only public for setting up test conditions
    public long Version { get; set; }

    // JsonIgnore - for making sure that it won't be stored in inline projection
    [JsonIgnore] private readonly List<object> _uncommittedEvents = new List<object>();

    // Get the deltas, i.e. events that make up the state, not yet persisted
    public IEnumerable<object> GetUncommittedEvents()
    {
        return _uncommittedEvents;
    }

    // Mark the deltas as persisted.
    public void ClearUncommittedEvents()
    {
        _uncommittedEvents.Clear();
    }

    protected void AddUncommittedEvent(object @event)
    {
        // add the event to the uncommitted list
        _uncommittedEvents.Add(@event);
    }
}

#endregion

#region sample_scenario-aggregate-repository

public sealed class AggregateRepository
{
    private readonly IDocumentStore store;

    public AggregateRepository(IDocumentStore store)
    {
        this.store = store;
    }

    public async Task StoreAsync(AggregateBase aggregate, CancellationToken ct = default)
    {
        await using var session = await store.LightweightSessionAsync(token: ct);
        // Take non-persisted events, push them to the event stream, indexed by the aggregate ID
        var events = aggregate.GetUncommittedEvents().ToArray();
        session.Events.Append(aggregate.Id, aggregate.Version, events);
        await session.SaveChangesAsync(ct);
        // Once successfully persisted, clear events from list of uncommitted events
        aggregate.ClearUncommittedEvents();
    }

    public async Task<T> LoadAsync<T>(
        string id,
        int? version = null,
        CancellationToken ct = default
    ) where T : AggregateBase
    {
        await using var session = await store.LightweightSessionAsync(token: ct);
        var aggregate = await session.Events.AggregateStreamAsync<T>(id, version ?? 0, token: ct);
        return aggregate ?? throw new InvalidOperationException($"No aggregate by id {id}.");
    }
}

#endregion
