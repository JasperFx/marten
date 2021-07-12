# Events Versioning

Events by their nature represent facts that happened in the past. They should be immutable even if they had wrong values (as we can only roughly guess what should be the missing property value).

However, in practice, it's unavoidable in the living system to not have the event schema migrations. They might come from:

- bug - eg. typo in the property name, missing event data,
- new business requirements - eg. besides storing the user email we'd like to be also storing its full name,
- refactorings - eg. renaming event class, moving to different namespace or assembly,
- etc.

Depending on the concrete business case we may use a different technique for handling such event migrations.

## Namespace migration

Marten by default tries to find the event class based on the fully qualified assembly name (it's stored in `mt_dotnet_type` column of `mt_events` table, read more in <[linkto:documentation/events/schema;title=events schema documentation]>).
When it is not able to find event type with the same assembly, namespace and type name then it tries to make a lookup for mapping on the event type name (stored in `type` column of `mt_events` table).

Such mapping needs to be defined manually:

- either by registering events with store options `Events.AddEventTypes` method,
- or by defining custom mapping with `Events.EventMappingFor` method.

For the case of namespace migration, it's enough to use `AddEventTypes` method as it's generating mapping based on the event type. As an example, change `OrderStatusChanged` event from:

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/SchemaChange/NamespaceChange.cs#L14-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_old_event_namespace' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/SchemaChange/NamespaceChange.cs#L31-L46' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_new_event_namespace' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It's enough to register new event type as follows:

<!-- snippet: sample_event_namespace_migration_options -->
<a id='snippet-sample_event_namespace_migration_options'></a>
```cs
var options = new StoreOptions();

options.Events.AddEventTypes(new[] {typeof(NewEventNamespace.OrderStatusChanged)});

var store = new DocumentStore(options);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/SchemaChange/NamespaceChange.cs#L70-L76' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event_namespace_migration_options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

After that Marten will automatically perform a matching based on the type name (that didn't change) - `order_status_changed`.

## Event type name migration

When the event class type name has changed, Marten does not perform automatic mapping but allows to define a custom one.

To do that you need to use `Events.EventMappingFor` method to define the type name for the new event.

Eg. for migrating `OrderStatusChanged` event into `ConfirmedOrderStatusChanged`

<!-- snippet: sample_new_event_type_name -->
<a id='snippet-sample_new_event_type_name'></a>
```cs
namespace OldEventNamespace
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/SchemaChange/NamespaceChange.cs#L49-L64' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_new_event_type_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

it's needed to register mapping using old event type name (`order_status_changed`) as follows:

<!-- snippet: sample_event_type_name_migration_options -->
<a id='snippet-sample_event_type_name_migration_options'></a>
```cs
var options = new StoreOptions();

var orderStatusChangedMapping = options.EventGraph.EventMappingFor<OldEventNamespace.ConfirmedOrderStatusChanged>();
orderStatusChangedMapping.EventTypeName = "order_status_changed";

var store = new DocumentStore(options);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/SchemaChange/NamespaceChange.cs#L81-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event_type_name_migration_options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
In this case, both old `OrderStatusChanged` and new `ConfirmedOrderStatusChanged` event type names will get published with the same `order_status_changed` event type.
:::
