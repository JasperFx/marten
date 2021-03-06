# Implementation Details

At the moment, Marten implements two modes of tenancy, namely single tenancy and conjoined multi-tenancy (see [tenancy](/guide/documents/tenancy/)).

## Conjoined Tenancy

The conjoined (`TenancyStyle.Conjoined`) multi-tenancy in Marten is implemented by associating each record with a tenant identifier. As such, Marten does not guarantee or enforce data isolation via database access privileges.

**Effects On Schema**

Once enabled, `TenancyStyle.Conjoined` introduces a `tenant_id` column to Marten tables. This column, of type `varchar` with the default value of `*DEFAULT*` (default tenancy), holds the tenant identifier associated with the record. Furthermore, Marten creates an index on this column by default.

A unique index may optionally be scoped per tenant (see [unique indexes](/guide/documents/configuration/unique)).
