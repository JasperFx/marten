<!--Title:Events Versioning-->
<!--Url:versioning-->

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

For the case of namespace migration, it's enough to use `AddEventTypes` method as it's generating mapping based on the event type. So eg. having `OrderStatusChanged` event changed from:

<[sample:old_event_namespace]> 

to:

<[sample:new_event_namespace]> 

It's enough to register new event type as follows:

<[sample:event_namespace_migration_options]>

After that Marten will automatically perform a matching based on the type name (that didn't change) - so. `order_status_changed`.

## Event type name migration

For the case when event class type name changed Marten is unable to perform the automatic mapping. However, it allows to define a custom one.

To do that you need to use `Events.EventMappingFor` method to define the type name for the new event.

Eg. for migrating `OrderStatusChanged` event into `ConfirmedOrderStatusChanged` 

<[sample:new_event_type_name]>

it's needed to register mapping using old event type name (`order_status_changed`) as follows:

<[sample:event_type_name_migration_options]>

<br />
<div class="alert alert-warning">
<b><u>Warning:</u></b>
<br />
In this case old event type name will be also used when publishing events of the new type name - so both `OrderStatusChanged` and `ConfirmedOrderStatusChanged` will be published with the same `order_status_changed` event type.
</div>
