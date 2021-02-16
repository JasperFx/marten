# Migration Guide

## Key changes in 3.0.0

Main goal of this release was to accommodate the **Npgsql 4.*** dependency.

Besides the usage of Npgsql 4, our biggest change was making the **default schema object creation mode** to `CreateOrUpdate`. Meaning that Marten even in its default mode will not drop any existing tables, even in development mode. You can still opt into the full "sure, I’ll blow away a table and start over if it’s incompatible" mode, but we felt like this option was safer after a few user problems were reported with the previous rules. See [schema migration and patches](/guide/schema/migrations) for more information.

We also aligned usage of `EnumStorage`. Previously, [Enum duplicated fields](/guide/documents/configuration/duplicated-fields) was always stored as `varchar`. Now it's using setting from `JsonSerializer` options - so by default it's `integer`. We felt that it's not consistent to have different default setting for Enums stored in json and in duplicated fields.

See full list of the fixed issues on [GitHub](https://github.com/JasperFx/marten/milestone/26?closed=1).

You can also read more in [Jeremy's blog post from](https://jeremydmiller.com/2018/09/27/marten-3-0-is-released-and-introducing-the-new-core-team/).

## Migration from 2.*

* To keep Marten fully rebuilding your schema (so to allow Marten drop tables) set store options to:

```csharp
AutoCreateSchemaObjects = AutoCreate.All
```

* To keep [enum fields](/guide/documents/configuration/duplicated-fields) being stored as `varchar` set store options to:

```csharp
DuplicatedFieldEnumStorage = EnumStorage.AsString;
```

* To keep [duplicated DateTime fields](/guide/documents/configuration/duplicated-fields) being stored as `timestamp with time zone` set store options to:

```csharp
DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime = false;
```
