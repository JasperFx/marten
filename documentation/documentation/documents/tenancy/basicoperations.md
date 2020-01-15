<!--Title: Multi-Tenancy with Basic Store Operations-->

Once configured for multi-tenancy, Marten exposes it via sessions (`IQuerySession`, `IDocumentSession`) scoped to specific tenants, as well as various overloads to saving operations that accept a tenant identifier.

## Scoping Sessions to Tenancy

The following sample demonstrates scoping a document session to tenancy idenfitied as *tenant1*. With multi-tenancy enabled, the persisted `User` objects are then associated with the tenancy of the session.  

<[sample:tenancy-scoping-session-write]>

As with storing, the load operations respect tenancy of the session.

<[sample:tenancy-scoping-session-read]>

Lastly, unlike reading operations, `IDocumentSession.Store` offers an overload to explicitly pass in a tenant identifier, bypassing any tenancy associated with the session. Similar overload for tenancy exists for `IDocumentStore.BulkInsert`.

## Default Tenancy

With multi-tenancy enabled, Marten associates each record with a tenancy. If no explicit tenancy is specified, either via policies, mappings, scoped sessions or overloads, Marten will default to `Tenancy.DefaultTenantId` with a constant value of *\*DEFAULT\**.

The following sample demonstrates persisting documents as non-tenanted, under default tenant and other named tenants then querying them back in a session scoped to a specific named tenant and default tenant.

<[sample:tenancy-mixed-tenancy-non-tenancy-sample]> 
