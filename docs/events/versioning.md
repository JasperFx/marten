# Events Versioning

Events, by their nature, represent facts that happened in the past. They should be immutable even if they had wrong values (as we can only roughly guess what should be the missing property value). Postgres allows us to do SQL migration even for the JSON data. Yet, those changes will only be reflected in the specific module. They won't be propagated further to other modules. In the distributed world we're living, that's a no-go. 

**The best strategy is not to change the past data but compensate our mishaps.** In Event Sourcing, that means appending the new event with correction. That's also how business work in general. If you issued the wrong invoice, you do not modify it; you send a new one with updated data.

Events versioning is presented as something scary, as you cannot "just update data" as in the traditional systems. Running migrations or finding broken data is challenging even in the classical way. **In Event Sourcing, you're at least getting tools to run a proper investigation.** By checking the history of events, you may find where was the place your data was broken (and, e.g. correlate it with the new deployment or system-wide failure).

**Business processes usually don't change so rapidly. Our understanding of how they work may change often.** Still, that typically means an issue in the requirements discovery or modelling. Typically you should not get a lot of schema versions of the same event. If you do, try to get back to the whiteboard and work on modelling, as there may be some design or process smell.

**It's also worth thinking about data in the context of the usage type.** It may be:
- _hot_ - accessed daily for our transactions/operations needs. That type of data represents active business processes. This is data that we're using actively in our business logic (write model),
- _warm_ - data used sporadically or read-only. They usually represent data we're accessing for our UI (read model) and data we typically won't change.
- _cold_ - data not used in our application or used by other modules (for instance, reporting). We may want to keep also for the legal obligations.

Once we realise that, we may discover that we might not separate the storage for each type. We also might not need to keep all data in the same database. If we also apply the temporal modelling practices to our model, then instead of keeping, e.g. all transactions for the cash register, we may just keep data for the current cashier shift. It will make our event streams shorter and more manageable. We may also decide to just keep read model <a href="TODO">documents</a> and <a href="TODO">archive</a> events from the inactive cashier shift, as effectively we won't be accessing them.

**Applying explained above modelling, and archiving techniques will keep our streams short-living. It may reduce the need to keep all event schemas.** When we need to introduce the new schema, we can do it with backward compatibility and support both old and new schema during the next deployment. Based on our business process lifetime, we can define the graceful period. For instance, helpdesk tickets live typically for 1-3 days. We can assume that, after two weeks from deployment, active tickets will be using only the new event schema. Of course, we should verify that, and events with the old schema will still be in the database. Yet, we can archive the inactive tickets, as they won't be needed for operational purposes (they will be either _warm_ or _cold_ data). By doing that, we can make the old event schema obsolete and don't need to maintain it.

Nevertheless, life is not only in black and white colours. We cannot predict everything and always be correct. **In practice, it's unavoidable in the living system not to have event schema migrations.** Even during the graceful period of making old schema obsolete. They might come from:

- bug - e.g. typo in the property name, missing event data,
- new business requirements - e.g. besides storing the user email, we'd like to be also storing its full name,
- refactorings - e.g. renaming event class, moving to a different namespace or assembly,
- etc.

Depending on the particular business case, we may use a different technique for handling such event migrations.

## Event type name mapping

Marten stores, by default, both CLR event class qualified assembly name and mapped event type name. It enables handling migrations of the CLR types, e.g. namespace or class name change. The Qualified assembly name is stored in the `mt_dotnet_type` column, and the event type name is stored in the `type` column of the `mt_events` table. Read more in [events schema documentation](/events/storage).

Marten will try to do automatic matching based on the qualified assembly name unless you specify the custom mapping. You can define it by:
- either registering events with store options using `Events.AddEventType` or `Events.AddEventTypes` methods,
- or by defining custom mapping with the `Events.MapEventType` method.

The default mapping changes the _CamelCase_ CLR class name into the lowered _snake\_case_. For instance, the mapped event type name for the `ECommerce.Orders.OrderStatusChanged` class will be `order_status_changed`.

## Namespace Migration

If you changed the namespace of your event class, it's enough to use the `AddEventTypes` method as it generates mapping based on the CLR event class name. As an example, change the `OrderStatusChanged` event from:

<!-- snippet: sample_old_event_namespace -->
<a id='snippet-sample_old_event_namespace'></a>
```cs
namespace OldEventNamespace
{
    public class OrderStatusChanged
    {
        public Guid OrderId { get; }
        public int Status { get; }

        public OrderStatusChanged(Guid orderId, int status)
        {
            OrderId = orderId;
            Status = status;
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/NamespaceChange.cs#L16-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_old_event_namespace' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

to:

<!-- snippet: sample_new_event_namespace -->
<a id='snippet-sample_new_event_namespace'></a>
```cs
namespace NewEventNamespace
{
    public class OrderStatusChanged
    {
        public Guid OrderId { get; }
        public int Status { get; }

        public OrderStatusChanged(Guid orderId, int status)
        {
            OrderId = orderId;
            Status = status;
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/NamespaceChange.cs#L33-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_new_event_namespace' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It's enough to register a new event type as follows:

<!-- snippet: sample_event_namespace_migration_options -->
<a id='snippet-sample_event_namespace_migration_options'></a>
```cs
var options = new StoreOptions();

options.Events.AddEventType<NewEventNamespace.OrderStatusChanged>();

var store = new DocumentStore(options);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/NamespaceChange.cs#L72-L78' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event_namespace_migration_options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

After that, Marten can do automatic mapping based on the class name (as it didn't change).

## Event Type Name Migration

When the event class type name has changed, Marten does not perform automatic mapping but allows to define a custom one.

To do that you need to use `Events.MapEventType` method to define the type name for the new event.

Eg. for migrating `OrderStatusChanged` event into `ConfirmedOrderStatusChanged`

<!-- snippet: sample_new_event_type_name -->
<a id='snippet-sample_new_event_type_name'></a>
```cs
namespace NewEventNamespace
{
    public class ConfirmedOrderStatusChanged
    {
        public Guid OrderId { get; }
        public int Status { get; }

        public ConfirmedOrderStatusChanged(Guid orderId, int status)
        {
            OrderId = orderId;
            Status = status;
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/NamespaceChange.cs#L51-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_new_event_type_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

it's needed to register mapping using old event type name (`order_status_changed`) as follows:

<!-- snippet: sample_event_type_name_migration_options -->
<a id='snippet-sample_event_type_name_migration_options'></a>
```cs
var options = new StoreOptions();

options.EventGraph
    .MapEventType<NewEventNamespace.ConfirmedOrderStatusChanged>("order_status_changed");

var store = new DocumentStore(options);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/NamespaceChange.cs#L83-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event_type_name_migration_options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
In this case, both old `OrderStatusChanged` and new `ConfirmedOrderStatusChanged` event type names will get published with the same `order_status_changed` event type.
:::
