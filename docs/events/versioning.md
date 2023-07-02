# Events Versioning

## Overview

Events, by their nature, represent facts that happened in the past. They should be immutable even if they had wrong or missing values (as we can only roughly guess what should be the correct value). Postgres allows us to do SQL migration even for the JSON data. Yet, those changes will only be reflected in the specific module. They won't be propagated further to other modules. In the distributed world we're living, that's a no-go. 

**The best strategy is not to change the past data but compensate our mishaps.** In Event Sourcing, that means appending the new event with correction. That's also how business work in general. If you issued the wrong invoice, you do not modify it; you send a new one with updated data.

Events versioning is presented as something scary, as you cannot "just update data" as in the traditional systems. Running migrations or finding broken data is challenging even in the classical way. **In Event Sourcing, you're at least getting tools to run a proper investigation.** By checking the history of events, you may find where was the place your data was broken (and, e.g. correlate it with the new deployment or system-wide failure).

**Business processes usually don't change so rapidly. Our understanding of how they work may change often.** Still, that typically means an issue in the requirements discovery or modeling. Typically you should not get a lot of schema versions of the same event. If you do, try to get back to the whiteboard and work on modeling, as there may be some design or process smell.

**It's also worth thinking about data in the context of the usage type.** It may be:

- _hot_ - accessed daily for our transactions/operations needs. That type of data represents active business processes. This is data that we're using actively in our business logic (write model),
- _warm_ - data used sporadically or read-only. They usually represent data we're accessing for our UI (read model) and data we typically won't change.
- _cold_ - data not used in our application or used by other modules (for instance, reporting). We may want to keep also for the legal obligations.

Once we realize that, we may discover that we could separate the storage for each type. We also might not need to keep all data in the same database. If we also apply the temporal modeling practices to our model, then instead of keeping, e.g. all transactions for the cash register, we may just keep data for the current cashier shift. It will make our event streams shorter and more manageable. We may also decide to just keep read model [documents](/documents/) and [archive](/events/archiving) events from the inactive cashier shift, as effectively we won't be accessing them.

**Applying explained above modeling, and archiving techniques will keep our streams short-living. It may reduce the need to keep all event schemas.** When we need to introduce the new schema, we can do it with backward compatibility and support both old and new schema during the next deployment. Based on our business process lifetime, we can define the graceful period. For instance, helpdesk tickets live typically for 1-3 days. We can assume that, after two weeks from deployment, active tickets will be using only the new event schema. Of course, we should verify that, and events with the old schema will still be in the database. Yet, we can archive the inactive tickets, as they won't be needed for operational purposes (they will be either _warm_ or _cold_ data). By doing that, we can make the old event schema obsolete and don't need to maintain it.

Nevertheless, life is not only in black and white colors. We cannot predict everything and always be correct. **In practice, it's unavoidable in the living system not to have event schema migrations.** Even during the graceful period of making old schema obsolete. They might come from:

- bug - e.g. typo in the property name, missing event data,
- new business requirements - e.g. besides storing the user email, we'd like to be also storing its full name,
- refactorings - e.g. renaming event class, moving to a different namespace or assembly,
- etc.

Depending on the particular business case, we may use a different technique for handling such event migrations.

Read also more in:

- [Oskar Dudycz - How to (not) do the events versioning?](https://event-driven.io/en/how_to_do_event_versioning/),
- [Oskar Dudycz - Simple patterns for events schema versioning](https://event-driven.io/en/how_to_do_event_versioning/),
- [Greg Young - Versioning in an Event Sourced System](https://leanpub.com/esversioning/read).

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

If you change the event type class name, Marten cannot do mapping by convention. You need to define the custom one.

To do that, you need to use the `Events.MapEventType` method. By calling it, you're telling that you'd like to use a selected CLR event class for the specific event type (e.g. `order_status_changed`). For instance to migrate `OrderStatusChanged` event into `ConfirmedOrderStatusChanged`.

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

You need to map the previous event type name (`order_status_changed`) into the renamed class as follows:

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
In this case, old `OrderStatusChanged` and new `ConfirmedOrderStatusChanged` event type names will be stored with the same `order_status_changed` event type.
:::

## Event Schema Migration

Schema changes are always tricky. Once you find out that you have to do them, it's worth making the thought process to understand the origins of that. You can ask yourself the following questions:

- what caused the change?
- what are the possible solutions?
- is the change breaking?
- what to do with old data?

Those questions are not specific to Event Sourcing changes; they're the same for all types of migrations. The solutions are also similar. The best advice is to avoid breaking changes. As explained [above](#events-versioning), you can make each change in a non-breaking manner.

## Simple schema mapping

Many schema changes don't require sophisticated logic. See the examples below to learn how to do them using the basic serializer capabilities. 

### New not required property

Having event defined as such:

<!-- snippet: sample_schema_migration_default_event -->
<a id='snippet-sample_schema_migration_default_event'></a>
```cs
public record ShoppingCartOpened(
    Guid ShoppingCartId,
    Guid ClientId
);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/SimpleMappings.cs#L10-L17' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_schema_migration_default_event' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you want to add a new not-required column, you may add it with the nullable type. By that, old events won't have it, and the new ones will have the value set. For instance, adding the optional date telling when the shopping cart was opened will look like this:

<!-- snippet: sample_schema_migration_not_required_property -->
<a id='snippet-sample_schema_migration_not_required_property'></a>
```cs
public record ShoppingCartOpened(
    Guid ShoppingCartId,
    Guid ClientId,
    // Adding new not required property as nullable
    DateTime? OpenedAt
);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/SimpleMappings.cs#L22-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_schema_migration_not_required_property' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### New required property

When introducing new property, we should always ensure the impact on our business logic. You may want the value to be always present (e.g. when you unintentionally forgot to add it or a new requirement came up). Like in the traditional approach, you should consider the default value of the newly added required column. It may be either calculated based on the other event data or some arbitrary value.

For our shopping cart open event, we may decide that we need to send; also status (to, e.g. allow fraud detection or enable a one-click "buy now" feature). Previously, we assumed that opened shopping cart would always put the shopping cart into "opened" status.

<!-- snippet: sample_schema_migration_required_property -->
<a id='snippet-sample_schema_migration_required_property'></a>
```cs
public enum ShoppingCartStatus
{
    UnderFraudDetection = 1,
    Opened = 2,
    Confirmed = 3,
    Cancelled = 4
}

public record ShoppingCartOpened(
    Guid ShoppingCartId,
    Guid ClientId,
    // Adding new required property with default value
    ShoppingCartStatus Status = ShoppingCartStatus.Opened
);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/SimpleMappings.cs#L36-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_schema_migration_required_property' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Of course, in that case, we should also consider if it wouldn't be better to add an explicit event type instead.

### Renamed property

Rename is also a form of breaking change. Humans can spot the intention, but for computers (and, in this case, serializers), it's the removal of the old property and the introduction of the new one. We should avoid such changes, but we'd also like to avoid embarrassing typos in our codebase. Most of the serializers allow property name mapping. Let's say we'd like to shorten the property name from `ShoppingCartId` to `CartId`. Both Newtonsoft Json.NET and System.Text.Json allow doing the mapping using property attributes.

With Json.NET, you should use [JsonProperty attribute](https://www.newtonsoft.com/json/help/html/jsonpropertyname.htm):

<!-- snippet: sample_schema_migration_renamed_property_jsonnet -->
<a id='snippet-sample_schema_migration_renamed_property_jsonnet'></a>
```cs
public class ShoppingCartOpened
{
    [JsonProperty("ShoppingCartId")]
    public Guid CartId { get; }
    public Guid ClientId { get; }

    public ShoppingCartOpened(
        Guid cartId,
        Guid clientId
    )
    {
        CartId = cartId;
        ClientId = clientId;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/SimpleMappings.cs#L83-L101' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_schema_migration_renamed_property_jsonnet' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With System.Text.Json, you should use [JsonPropertyName attribute](https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-customize-properties):

<!-- snippet: sample_schema_migration_renamed_property_stj -->
<a id='snippet-sample_schema_migration_renamed_property_stj'></a>
```cs
public class ShoppingCartOpened
{
    [JsonPropertyName("ShoppingCartId")]
    public Guid CartId { get; }
    public Guid ClientId { get; }

    public ShoppingCartOpened(
        Guid cartId,
        Guid clientId
    )
    {
        CartId = cartId;
        ClientId = clientId;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/SimpleMappings.cs#L60-L78' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_schema_migration_renamed_property_stj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
Remember that if you use this attribute, new events will still produce the old (mapped) property name.

One of the consequences is that you won't be able to [query event data](/events/querying) by this property. Marten while performing database query using a direct mapping from CLR expressions. The query will then use the new name, while you'll find the old one in the payload. As we're querying JSON, it won't throw an exception, but just not find the respectful event data returning no results.
:::

## Upcasting - advanced payload transformations

Sometimes with more extensive schema changes, you'd like more flexibility in payload transformations. Upcasting is a process of transforming the old JSON schema into the new one. It's performed on the fly each time the event is read. You can think of it as a pluggable middleware between the deserialization and application logic. Having that, we can either grab raw JSON or a deserialized object of the old CLR type and transform them into the new schema. Thanks to that, we can keep only the last version of the event schema in our stream aggregation or projection handling.

There are two main ways of upcasting the old schema into the new one:

- **CLR types transformation** - if we're okay with keeping the old CLR class in the codebase, we could define a function that takes the instance of the old type and returns the new one. Internally it will use default deserialization and event type mapping for the old CLR type and calls the upcasting function.
- **Raw JSON transformation** - if we don't want to keep the old CLR class or want to get the best performance by reducing the number of allocations, we can do raw JSON transformations. Most of the serializers have classes enabling that. [Newtonsoft Json.NET has  JObject](https://www.newtonsoft.com/json/help/html/queryinglinqtojson.htm) and [System.Text.Json has JsonDocument](https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-use-dom-utf8jsonreader-utf8jsonwriter#use-jsondocument). This gives the best flexibility, but logic may be more cryptic and _stringly-typed_.

Let's say that we'd like to transform the event type known from previous examples:

<!-- snippet: sample_upcasters_old_event_type -->
<a id='snippet-sample_upcasters_old_event_type'></a>
```cs
public record ShoppingCartOpened(
    Guid ShoppingCartId,
    Guid ClientId
);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L21-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcasters_old_event_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

We want to enrich it with shopping cart status and client name. To have a more straightforward structure, we'd like to group the client id and name into a nested object.

<!-- snippet: sample_upcasters_new_event_type -->
<a id='snippet-sample_upcasters_new_event_type'></a>
```cs
public record ShoppingCartOpenedWithStatus(
    Guid ShoppingCartId,
    Client Client,
    ShoppingCartStatus Status
);

public record Client(
    Guid Id,
    string Name = "Unknown"
);

public enum ShoppingCartStatus
{
    Pending = 1,
    Opened = 2,
    Confirmed = 3,
    Cancelled = 4
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L30-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcasters_new_event_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten provides extended capabilities around that and enables different styles for handling the upcasting transformations.

### Upcasting with functions

The simplest way to define transformations is to do that using functions. As upcasting is a process that takes the old event payload and returns the new one, we could think of them as pure functions without side effects. That makes them also easy to test with unit or contract tests. 

We can define them with store options customization code or place them as static functions inside the class and register them. The former is simpler, the latter more maintainable and testable.

#### Transformation with CLR types will look like this:

<!-- snippet: sample_upcast_event_lambda_with_clr_types -->
<a id='snippet-sample_upcast_event_lambda_with_clr_types'></a>
```cs
options.Events
    .Upcast<ShoppingCartOpened, ShoppingCartOpenedWithStatus>(
        oldEvent =>
            new ShoppingCartOpenedWithStatus(
                oldEvent.ShoppingCartId,
                new Client(oldEvent.ClientId),
                ShoppingCartStatus.Opened
            )
    );
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L183-L195' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_event_lambda_with_clr_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It will default take the event type name based on the old CLR type. You can also define it explicitly. It can be helpful if you changed the event schema more than once, and the old CLR class doesn't represent the initial event type name. You can do that with:

<!-- snippet: sample_upcast_event_lambda_with_clr_types_and_explicit_type_name -->
<a id='snippet-sample_upcast_event_lambda_with_clr_types_and_explicit_type_name'></a>
```cs
options.Events
    .Upcast<ShoppingCartOpened, ShoppingCartOpenedWithStatus>(
        "shopping_cart_opened",
        oldEvent =>
            new ShoppingCartOpenedWithStatus(
                oldEvent.ShoppingCartId,
                new Client(oldEvent.ClientId),
                ShoppingCartStatus.Opened
            )
    );
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L225-L238' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_event_lambda_with_clr_types_and_explicit_type_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Raw JSON transformation with Json .NET:

<!-- snippet: sample_upcast_event_lambda_with_jsonnet_jobject -->
<a id='snippet-sample_upcast_event_lambda_with_jsonnet_jobject'></a>
```cs
options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

options.Events
    .Upcast<ShoppingCartOpenedWithStatus>(
        "shopping_cart_opened",
        Upcast(oldEvent =>
            new ShoppingCartOpenedWithStatus(
                (Guid)oldEvent["ShoppingCartId"]!,
                new Client(
                    (Guid)oldEvent["ClientId"]!
                ),
                ShoppingCartStatus.Opened
            )
        )
    );
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L585-L603' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_event_lambda_with_jsonnet_jobject' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Add also static import of helper classes to get a concise syntax as above:

<!-- snippet: sample_upcast_json_net_static_using -->
<a id='snippet-sample_upcast_json_net_static_using'></a>
```cs
using static Marten.Services.Json.Transformations.JsonNet.JsonTransformations;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L524-L528' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_json_net_static_using' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Raw JSON transformation with System.Text.Json:

<!-- snippet: sample_upcast_event_lambda_with_systemtextjson_json_document -->
<a id='snippet-sample_upcast_event_lambda_with_systemtextjson_json_document'></a>
```cs
options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

options.Events
    .Upcast<ShoppingCartOpenedWithStatus>(
        "shopping_cart_opened",
        Upcast(oldEventJson =>
        {
            var oldEvent = oldEventJson.RootElement;

            return new ShoppingCartOpenedWithStatus(
                oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                new Client(
                    oldEvent.GetProperty("ClientId").GetGuid()
                ),
                ShoppingCartStatus.Opened
            );
        })
    );
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L384-L405' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_event_lambda_with_systemtextjson_json_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Add also static import of helper classes to get a concise syntax as above:

<!-- snippet: sample_upcast_system_text_json_static_using -->
<a id='snippet-sample_upcast_system_text_json_static_using'></a>
```cs
using static Marten.Services.Json.Transformations.SystemTextJson.JsonTransformations;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L315-L319' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_system_text_json_static_using' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Upcasting with classes

Some people prefer to use classes instead of pure functions. It may help encapsulation, especially if you're using external dependencies for the transformation logic. It may also help in structuring the schema migrations code. You get the same set of capabilities as with functions registration.

#### Transformation with CLR types will look like this:

<!-- snippet: sample_upcaster_with_clr_types_and_event_type_name_from_old_type -->
<a id='snippet-sample_upcaster_with_clr_types_and_event_type_name_from_old_type'></a>
```cs
public class ShoppingCartOpenedUpcaster:
    EventUpcaster<ShoppingCartOpened, ShoppingCartOpenedWithStatus>
{
    protected override ShoppingCartOpenedWithStatus Upcast(ShoppingCartOpened oldEvent) =>
        new ShoppingCartOpenedWithStatus(
            oldEvent.ShoppingCartId,
            new Client(oldEvent.ClientId),
            ShoppingCartStatus.Opened
        );
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L76-L89' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcaster_with_clr_types_and_event_type_name_from_old_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Just like with functions, by default, it takes the event type name based on the old CLR type. You can also define it explicitly. It can be helpful if you changed the event schema more than once, and the old CLR class doesn't represent the initial event type name. You can do that with:

<!-- snippet: sample_upcaster_with_clr_types_and_explicit_event_type_name -->
<a id='snippet-sample_upcaster_with_clr_types_and_explicit_event_type_name'></a>
```cs
public class ShoppingCartOpenedUpcaster:
    EventUpcaster<ShoppingCartOpened, ShoppingCartOpenedWithStatus>
{
    // Explicit event type name mapping may be useful if you used other than default event type name
    // for old event type.
    public override string EventTypeName => "shopping_cart_opened";

    protected override ShoppingCartOpenedWithStatus Upcast(ShoppingCartOpened oldEvent) =>
        new ShoppingCartOpenedWithStatus(
            oldEvent.ShoppingCartId,
            new Client(oldEvent.ClientId),
            ShoppingCartStatus.Opened
        );
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L124-L141' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcaster_with_clr_types_and_explicit_event_type_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Raw JSON transformation with Json .NET:

<!-- snippet: sample_upcaster_with_clr_types_and_event_type_name_from_old_type_with_jsonnet_jobject -->
<a id='snippet-sample_upcaster_with_clr_types_and_event_type_name_from_old_type_with_jsonnet_jobject'></a>
```cs
public class ShoppingCartOpenedUpcaster:
    EventUpcaster<ShoppingCartOpenedWithStatus>
{
    public override string EventTypeName => "shopping_cart_opened";

    protected override ShoppingCartOpenedWithStatus Upcast(JObject oldEvent) =>
        new ShoppingCartOpenedWithStatus(
            (Guid)oldEvent["ShoppingCartId"]!,
            new Client(
                (Guid)oldEvent["ClientId"]!
            ),
            ShoppingCartStatus.Opened
        );
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L530-L547' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcaster_with_clr_types_and_event_type_name_from_old_type_with_jsonnet_jobject' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To use it, add the following using:

<!-- snippet: sample_upcast_json_net_class_using -->
<a id='snippet-sample_upcast_json_net_class_using'></a>
```cs
using Marten.Services.Json.Transformations.JsonNet;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L518-L522' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_json_net_class_using' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Raw JSON transformation with System.Text.Json:

<!-- snippet: sample_upcaster_with_clr_types_and_event_type_name_from_old_type_with_systemtextjson_json_document -->
<a id='snippet-sample_upcaster_with_clr_types_and_event_type_name_from_old_type_with_systemtextjson_json_document'></a>
```cs
public class ShoppingCartOpenedUpcaster:
    EventUpcaster<ShoppingCartOpenedWithStatus>
{
    public override string EventTypeName => "shopping_cart_opened";

    protected override ShoppingCartOpenedWithStatus Upcast(JsonDocument oldEventJson)
    {
        var oldEvent = oldEventJson.RootElement;

        return new ShoppingCartOpenedWithStatus(
            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
            new Client(
                oldEvent.GetProperty("ClientId").GetGuid()
            ),
            ShoppingCartStatus.Opened
        );
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L321-L342' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcaster_with_clr_types_and_event_type_name_from_old_type_with_systemtextjson_json_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To use it, add the following using:

<!-- snippet: sample_upcast_system_text_json_class_using -->
<a id='snippet-sample_upcast_system_text_json_class_using'></a>
```cs
using Marten.Services.Json.Transformations.SystemTextJson;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L309-L313' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_system_text_json_class_using' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Registering upcaster class

<!-- snippet: sample_upcast_event_class_with_clr_types -->
<a id='snippet-sample_upcast_event_class_with_clr_types'></a>
```cs
options.Events.Upcast<ShoppingCartOpenedUpcaster>();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L270-L274' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_event_class_with_clr_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Async Only Upcasters

The techniques presented above should be enough for the majority of cases. Yet, sometimes we need to do more than that. E.g. load the JSON schema to validate event payload and do different mapping in case of validation failure. We may also want to load some additional data or use the library with the async-only API. We got you also covered in this case. You can also define upcasting transformations using .NET async code.

::: warning
We recommend ensuring that you know what you're doing, as:

1. **Upcasting code is run each time the event is deserialized.** That means that if you read a lot of events and you're trying to call external resources (especially if that involves network calls or IO operations), then you may end up with poor performance and the [N+1 problem](https://stackoverflow.com/questions/97197/what-is-the-n1-selects-problem-in-orm-object-relational-mapping). If you need to do more exhausting call, make sure that you're caching results or getting the results upfront and reusing Task. Read also [Understanding the Whys, Whats, and Whens of ValueTask](https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/).
2. Marten supports both synchronous and asynchronous calls. **If you define async upcaster, the exception will be thrown if you read events with sync code.**
:::

Let's assume that you're aware of the async code consequences explained above and that you'd like to read additional client data while upcasting using the following interface:

<!-- snippet: sample_async_upcaster_dependency -->
<a id='snippet-sample_async_upcaster_dependency'></a>
```cs
public interface IClientRepository
{
    Task<string> GetClientName(Guid clientId, CancellationToken ct);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L54-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_upcaster_dependency' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can use it in all the ways presented above.

#### Function with CLR types

<!-- snippet: sample_async_upcast_event_lambda_with_clr_types -->
<a id='snippet-sample_async_upcast_event_lambda_with_clr_types'></a>
```cs
options.Events
    .Upcast<ShoppingCartOpened, ShoppingCartOpenedWithStatus>(
        async (oldEvent, ct) =>
        {
            // WARNING: UpcastAsync method is called each time old event
            // is read from database and deserialized.
            // We discourage to run resource consuming methods here.
            // It might end up with N+1 problem.
            var clientName = await clientRepository.GetClientName(oldEvent.ClientId, ct);

            return new ShoppingCartOpenedWithStatus(
                oldEvent.ShoppingCartId,
                new Client(oldEvent.ClientId, clientName),
                ShoppingCartStatus.Opened
            );
        }
    );
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L200-L220' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_upcast_event_lambda_with_clr_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Function with CLR types and explicit event type name

<!-- snippet: sample_async_upcast_event_lambda_with_clr_types_and_explicit_type_name -->
<a id='snippet-sample_async_upcast_event_lambda_with_clr_types_and_explicit_type_name'></a>
```cs
options.Events
    .Upcast<ShoppingCartOpened, ShoppingCartOpenedWithStatus>(
        "shopping_cart_opened",
        async (oldEvent, ct) =>
        {
            // WARNING: UpcastAsync method is called each time old event
            // is read from database and deserialized.
            // We discourage to run resource consuming methods here.
            // It might end up with N+1 problem.
            var clientName = await clientRepository.GetClientName(oldEvent.ClientId, ct);

            return new ShoppingCartOpenedWithStatus(
                oldEvent.ShoppingCartId,
                new Client(oldEvent.ClientId, clientName),
                ShoppingCartStatus.Opened
            );
        }
    );
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L244-L265' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_upcast_event_lambda_with_clr_types_and_explicit_type_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Function with raw JSON transformation with Json .NET:

<!-- snippet: sample_async_upcast_event_lambda_with_jsonnet_jobject -->
<a id='snippet-sample_async_upcast_event_lambda_with_jsonnet_jobject'></a>
```cs
options.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);

options.Events
    .Upcast<ShoppingCartOpenedWithStatus>(
        "shopping_cart_opened",
        AsyncOnlyUpcast(async (oldEvent, ct) =>
            {
                var clientId = (Guid)oldEvent["ClientId"]!;
                // WARNING: UpcastAsync method is called each time old event
                // is read from database and deserialized.
                // We discourage to run resource consuming methods here.
                // It might end up with N+1 problem.
                var clientName = await clientRepository.GetClientName(clientId, ct);

                return new ShoppingCartOpenedWithStatus(
                    (Guid)oldEvent["ShoppingCartId"]!,
                    new Client(clientId, clientName),
                    ShoppingCartStatus.Opened
                );
            }
        )
    );
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L608-L633' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_upcast_event_lambda_with_jsonnet_jobject' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Add also static import of helper classes to get a concise syntax as above:

<!-- snippet: sample_upcast_json_net_static_using -->
<a id='snippet-sample_upcast_json_net_static_using'></a>
```cs
using static Marten.Services.Json.Transformations.JsonNet.JsonTransformations;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L524-L528' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_json_net_static_using' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Function with raw JSON transformation with System.Text.Json:

<!-- snippet: sample_async_upcast_event_lambda_with_systemtextjson_json_document -->
<a id='snippet-sample_async_upcast_event_lambda_with_systemtextjson_json_document'></a>
```cs
options.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

options.Events
    .Upcast<ShoppingCartOpenedWithStatus>(
        "shopping_cart_opened",
        AsyncOnlyUpcast(async (oldEventJson, ct) =>
        {
            var oldEvent = oldEventJson.RootElement;

            var clientId = oldEvent.GetProperty("ClientId").GetGuid();

            // WARNING: UpcastAsync method is called each time
            // old event is read from database and deserialized.
            // We discourage to run resource consuming methods here.
            // It might end up with N+1 problem.
            var clientName = await clientRepository.GetClientName(clientId, ct);

            return new ShoppingCartOpenedWithStatus(
                oldEvent.GetProperty("ShoppingCartId").GetGuid(),
                new Client(clientId, clientName),
                ShoppingCartStatus.Opened
            );
        })
    );
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L410-L437' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_upcast_event_lambda_with_systemtextjson_json_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Add also static import of helper classes to get a concise syntax as above:

<!-- snippet: sample_upcast_system_text_json_static_using -->
<a id='snippet-sample_upcast_system_text_json_static_using'></a>
```cs
using static Marten.Services.Json.Transformations.SystemTextJson.JsonTransformations;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L315-L319' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_system_text_json_static_using' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Class with CLR types

<!-- snippet: sample_async_only_upcaster_with_clr_types_and_event_type_name_from_old_type -->
<a id='snippet-sample_async_only_upcaster_with_clr_types_and_event_type_name_from_old_type'></a>
```cs
public class ShoppingCartOpenedAsyncOnlyUpcaster:
    AsyncOnlyEventUpcaster<ShoppingCartOpened, ShoppingCartOpenedWithStatus>
{
    private readonly IClientRepository _clientRepository;

    public ShoppingCartOpenedAsyncOnlyUpcaster(IClientRepository clientRepository) =>
        _clientRepository = clientRepository;

    protected override async Task<ShoppingCartOpenedWithStatus> UpcastAsync(
        ShoppingCartOpened oldEvent,
        CancellationToken ct
    )
    {
        // WARNING: UpcastAsync method is called each time old event
        // is read from database and deserialized.
        // We discourage to run resource consuming methods here.
        // It might end up with N+1 problem.
        var clientName = await _clientRepository.GetClientName(oldEvent.ClientId, ct);

        return new ShoppingCartOpenedWithStatus(
            oldEvent.ShoppingCartId,
            new Client(oldEvent.ClientId, clientName),
            ShoppingCartStatus.Opened
        );
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L91-L120' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_only_upcaster_with_clr_types_and_event_type_name_from_old_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Class with CLR types and explicit event type name

<!-- snippet: sample_async_only_upcaster_with_clr_types_and_explicit_event_type_name -->
<a id='snippet-sample_async_only_upcaster_with_clr_types_and_explicit_event_type_name'></a>
```cs
public class ShoppingCartOpenedAsyncOnlyUpcaster:
    AsyncOnlyEventUpcaster<ShoppingCartOpened, ShoppingCartOpenedWithStatus>
{
    // Explicit event type name mapping may be useful if you used other than default event type name
    // for old event type.
    public override string EventTypeName => "shopping_cart_opened";

    private readonly IClientRepository _clientRepository;

    public ShoppingCartOpenedAsyncOnlyUpcaster(IClientRepository clientRepository) =>
        _clientRepository = clientRepository;

    protected override async Task<ShoppingCartOpenedWithStatus> UpcastAsync(
        ShoppingCartOpened oldEvent,
        CancellationToken ct
    )
    {
        // WARNING: UpcastAsync method is called each time old event
        // is read from database and deserialized.
        // We discourage to run resource consuming methods here.
        // It might end up with N+1 problem.
        var clientName = await _clientRepository.GetClientName(oldEvent.ClientId, ct);

        return new ShoppingCartOpenedWithStatus(
            oldEvent.ShoppingCartId,
            new Client(oldEvent.ClientId, clientName),
            ShoppingCartStatus.Opened
        );
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L143-L176' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_only_upcaster_with_clr_types_and_explicit_event_type_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Class with raw JSON transformation with Json .NET:

<!-- snippet: sample_async_upcaster_with_jsonnet_jobject -->
<a id='snippet-sample_async_upcaster_with_jsonnet_jobject'></a>
```cs
public class ShoppingCartOpenedAsyncOnlyUpcaster:
    AsyncOnlyEventUpcaster<ShoppingCartOpenedWithStatus>
{
    private readonly IClientRepository _clientRepository;

    public ShoppingCartOpenedAsyncOnlyUpcaster(IClientRepository clientRepository) =>
        _clientRepository = clientRepository;

    public override string EventTypeName => "shopping_cart_opened";

    protected override async Task<ShoppingCartOpenedWithStatus> UpcastAsync(
        JObject oldEvent,
        CancellationToken ct
    )
    {
        var clientId = (Guid)oldEvent["ClientId"]!;
        // WARNING: UpcastAsync method is called each time old event
        // is read from database and deserialized.
        // We discourage to run resource consuming methods here.
        // It might end up with N+1 problem.
        var clientName = await _clientRepository.GetClientName(clientId, ct);

        return new ShoppingCartOpenedWithStatus(
            (Guid)oldEvent["ShoppingCartId"]!,
            new Client(clientId, clientName),
            ShoppingCartStatus.Opened
        );
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L549-L579' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_upcaster_with_jsonnet_jobject' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To use it, add the following using:

<!-- snippet: sample_upcast_json_net_class_using -->
<a id='snippet-sample_upcast_json_net_class_using'></a>
```cs
using Marten.Services.Json.Transformations.JsonNet;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L518-L522' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_json_net_class_using' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Class with raw JSON transformation with System.Text.Json:

<!-- snippet: sample_async_upcaster_with_systemtextjson_json_document -->
<a id='snippet-sample_async_upcaster_with_systemtextjson_json_document'></a>
```cs
public class ShoppingCartOpenedAsyncOnlyUpcaster:
    AsyncOnlyEventUpcaster<ShoppingCartOpenedWithStatus>
{
    private readonly IClientRepository _clientRepository;

    public ShoppingCartOpenedAsyncOnlyUpcaster(IClientRepository clientRepository) =>
        _clientRepository = clientRepository;

    public override string EventTypeName => "shopping_cart_opened";

    protected override async Task<ShoppingCartOpenedWithStatus> UpcastAsync(
        JsonDocument oldEventJson, CancellationToken ct
    )
    {
        var oldEvent = oldEventJson.RootElement;

        var clientId = oldEvent.GetProperty("ClientId").GetGuid();

        // WARNING: UpcastAsync method is called each time old event
        // is read from database and deserialized.
        // We discourage to run resource consuming methods here.
        // It might end up with N+1 problem.
        var clientName = await _clientRepository.GetClientName(clientId, ct);

        return new ShoppingCartOpenedWithStatus(
            oldEvent.GetProperty("ShoppingCartId").GetGuid(),
            new Client(clientId, clientName),
            ShoppingCartStatus.Opened
        );
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L344-L378' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_upcaster_with_systemtextjson_json_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To use it, add the following using:

<!-- snippet: sample_upcast_system_text_json_class_using -->
<a id='snippet-sample_upcast_system_text_json_class_using'></a>
```cs
using Marten.Services.Json.Transformations.SystemTextJson;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L309-L313' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_upcast_system_text_json_class_using' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Registering Upcaster class

<!-- snippet: sample_async_upcast_event_class_with_clr_types -->
<a id='snippet-sample_async_upcast_event_class_with_clr_types'></a>
```cs
options.Events.Upcast(new ShoppingCartOpenedAsyncOnlyUpcaster(clientRepository));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/SchemaChange/Upcasters.cs#L279-L283' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_upcast_event_class_with_clr_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Working with multiple Event type versions
