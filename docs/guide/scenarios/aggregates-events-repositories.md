# Aggregates, Events, Repositories

This use case demonstrates how to capture state changes in events and then replaying that state from the database. This is done by first introducing some supporting infrastructure, then implementing a model of invoice, together with invoice lines, on top of that.

## Scenario

To model, capture and replay the state of an object through events, some infrastructure is established to dispatch events to their respective handlers. This is demonstrated in the `AggregateBase` class below - it serves as the basis for objects whose state is to be modeled.

<!-- snippet: sample_scenario-aggregate-base -->
<a id='snippet-sample_scenario-aggregate-base'></a>
```cs
// Infrastructure to capture modifications to state in events
public abstract class AggregateBase
{
    // For indexing our event streams
    public string Id { get; protected set; }

    // For protecting the state, i.e. conflict prevention
    // The setter is only public for setting up test conditions
    public long Version { get; set; }

    // JsonIgnore - for making sure that it won't be stored in inline projection
    [JsonIgnore]
    private readonly List<object> _uncommittedEvents = new List<object>();

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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#L211-L245' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-aggregate-base' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With the first piece of infrastructure implemented, two events to capture state changes of an invoice are introduced. Namely, creation of an invoice, accompanied by an invoice number, and addition of lines to an invoice:

<!-- snippet: sample_scenario-aggregate-events -->
<a id='snippet-sample_scenario-aggregate-events'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#L184-L209' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-aggregate-events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With the events in place to present the deltas of an invoice, an aggregate is implemented, using the infrastructure presented above, to create and replay state from the described events.

<!-- snippet: sample_scenario-aggregate-invoice -->
<a id='snippet-sample_scenario-aggregate-invoice'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#L110-L182' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-aggregate-invoice' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The implemented invoice protects its state by not exposing mutable data, while enforcing its contracts through argument validation. Once an applicable state modification is introduced, either through the constructor (which numbers our invoice and captures that in an event) or the `Invoice.AddLine` method, a respective event capturing that data is recorded.

Lastly, to persist the deltas described above and to replay the state of an object from such persisted data, a repository is implemented. The said repository pushes the deltas of an object to event stream, indexed by the ID of the object.

<!-- snippet: sample_scenario-aggregate-repository -->
<a id='snippet-sample_scenario-aggregate-repository'></a>
```cs
public sealed class AggregateRepository
{
    private readonly IDocumentStore store;

    public AggregateRepository(IDocumentStore store)
    {
        this.store = store;
    }

    public void Store(AggregateBase aggregate)
    {
        using (var session = store.OpenSession())
        {
            // Take non-persisted events, push them to the event stream, indexed by the aggregate ID
            var events = aggregate.GetUncommittedEvents().ToArray();
            session.Events.Append(aggregate.Id, aggregate.Version, events);
            session.SaveChanges();
        }
        // Once successfully persisted, clear events from list of uncommitted events
        aggregate.ClearUncommittedEvents();
    }

    public T Load<T>(string id, int? version = null) where T : AggregateBase
    {
        using (var session = store.LightweightSession())
        {
            var aggregate = session.Events.AggregateStream<T>(id, version ?? 0);
            return aggregate ?? throw new InvalidOperationException($"No aggregate by id {id}.");
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#L247-L280' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-aggregate-repository' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With the last infrastructure component in place, versioned invoices can now be created, persisted and hydrated through Marten. For this purpose, first an invoice is created:

<!-- snippet: sample_scenario-aggregate-createinvoice -->
<a id='snippet-sample_scenario-aggregate-createinvoice'></a>
```cs
var invoice = new Invoice(42);

invoice.AddLine(100, 24, "Joo Janta 200 Super-Chromatic Peril Sensitive Sunglasses");
invoice.AddLine(200, 16, "Happy Vertical People Transporter");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#L99-L104' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-aggregate-createinvoice' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Then, with an instantiated & configured Document Store (in this case with string as event stream identity) a repository is bootstrapped. The newly created invoice is then passed to the repository, which pushes the deltas to the database and clears them from the to-be-committed list of changes. Once persisted, the invoice data is replayed from the database and verified to match the data of the original item.

<!-- snippet: sample_scenario-aggregate-storeandreadinvoice -->
<a id='snippet-sample_scenario-aggregate-storeandreadinvoice'></a>
```cs
var repository = new AggregateRepository(theStore);

repository.Store(invoice);

var invoiceFromRepository = repository.Load<Invoice>(invoice.Id);

Assert.Equal(invoice.ToString(), invoiceFromRepository.ToString());
Assert.Equal(invoice.Total, invoiceFromRepository.Total);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#L29-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-aggregate-storeandreadinvoice' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With this infrastructure in place and the ability to model change as events, it is also possible to replay back any previous state of the object. For example, it is possible to see what the invoice looked with only the first line added:

<!-- snippet: sample_scenario-aggregate-versionedload -->
<a id='snippet-sample_scenario-aggregate-versionedload'></a>
```cs
var invoiceFromRepository = repository.Load<Invoice>(invoice.Id, 2);

Assert.Equal(124, invoiceFromRepository.Total);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#L50-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-aggregate-versionedload' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, to prevent our invoice from getting into a conflited state, the version attribute of the item is used to assert that the state of the object has not changed between replaying its state and introducing new deltas:

<!-- snippet: sample_scenario-aggregate-conflict -->
<a id='snippet-sample_scenario-aggregate-conflict'></a>
```cs
var invoice = CreateInvoice();
var invoiceWithSameIdentity = CreateInvoice();

repository.Store(invoice);

Assert.Throws<EventStreamUnexpectedMaxEventIdException>(() =>
{
    repository.Store(invoiceWithSameIdentity);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#L62-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-aggregate-conflict' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
