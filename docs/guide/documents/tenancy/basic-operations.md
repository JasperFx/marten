# Multi-Tenancy with Basic Store Operations

Once configured for multi-tenancy, Marten exposes it via sessions (`IQuerySession`, `IDocumentSession`) scoped to specific tenants, as well as various overloads to saving operations that accept a tenant identifier.

## Scoping Sessions to Tenancy

The following sample demonstrates scoping a document session to tenancy idenfitied as *tenant1*. With multi-tenancy enabled, the persisted `User` objects are then associated with the tenancy of the session.

<!-- snippet: sample_tenancy-scoping-session-write -->
<a id='snippet-sample_tenancy-scoping-session-write'></a>
```cs
// Write some User documents to tenant "tenant1"
using (var session = store.OpenSession("tenant1"))
{
    session.Store(new User { UserName = "Bill" });
    session.Store(new User { UserName = "Lindsey" });
    session.SaveChanges();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L33-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-scoping-session-write' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As with storing, the load operations respect tenancy of the session.

<!-- snippet: sample_tenancy-scoping-session-read -->
<a id='snippet-sample_tenancy-scoping-session-read'></a>
```cs
// When you query for data from the "tenant1" tenant,
// you only get data for that tenant
using (var query = store.QuerySession("tenant1"))
{
    query.Query<User>()
        .Select(x => x.UserName)
        .ToList()
        .ShouldHaveTheSameElementsAs("Bill", "Lindsey");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L51-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-scoping-session-read' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, unlike reading operations, `IDocumentSession.Store` offers an overload to explicitly pass in a tenant identifier, bypassing any tenancy associated with the session. Similar overload for tenancy exists for `IDocumentStore.BulkInsert`.

## Default Tenancy

With multi-tenancy enabled, Marten associates each record with a tenancy. If no explicit tenancy is specified, either via policies, mappings, scoped sessions or overloads, Marten will default to `Tenancy.DefaultTenantId` with a constant value of `*DEFAULT*`.

The following sample demonstrates persisting documents as non-tenanted, under default tenant and other named tenants then querying them back in a session scoped to a specific named tenant and default tenant.

<!-- snippet: sample_tenancy-mixed-tenancy-non-tenancy-sample -->
<a id='snippet-sample_tenancy-mixed-tenancy-non-tenancy-sample'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.DatabaseSchemaName = "mixed_multi_tenants";
    opts.Connection(ConnectionSource.ConnectionString);
    opts.Schema.For<Target>().MultiTenanted(); // tenanted
    opts.Schema.For<User>(); // non-tenanted
    opts.Schema.For<Issue>().MultiTenanted(); // tenanted
});

store.Advanced.Clean.DeleteAllDocuments();

// Add documents to tenant Green
var greens = Target.GenerateRandomData(10).ToArray();
store.BulkInsert("Green", greens);

// Add documents to tenant Red
var reds = Target.GenerateRandomData(11).ToArray();
store.BulkInsert("Red", reds);

// Add non-tenanted documents
// User is non-tenanted in schema
var user1 = new User {UserName = "Frank"};
var user2 = new User {UserName = "Bill"};
store.BulkInsert(new[] {user1, user2});

// Add documents to default tenant
// Note that schema for Issue is multi-tenanted hence documents will get added
// to default tenant if tenant is not passed in the bulk insert operation
var issue1 = new Issue {Title = "Test issue1"};
var issue2 = new Issue {Title = "Test issue2"};
store.BulkInsert(new[] {issue1, issue2});

// Create a session with tenant Green
using (var session = store.QuerySession("Green"))
{
    // Query tenanted document as the tenant passed in session
    session.Query<Target>().Count().ShouldBe(10);

    // Query non-tenanted documents
    session.Query<User>().Count().ShouldBe(2);

    // Query documents in default tenant from a session using tenant Green
    session.Query<Issue>().Count(x => x.TenantIsOneOf(Tenancy.DefaultTenantId)).ShouldBe(2);

    // Query documents from tenant Red from a session using tenant Green
    session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
}

// create a session without passing any tenant, session will use default tenant
using (var session = store.QuerySession())
{
    // Query non-tenanted documents
    session.Query<User>().Count().ShouldBe(2);

    // Query documents in default tenant
    // Note that session is using default tenant
    session.Query<Issue>().Count().ShouldBe(2);

    // Query documents on tenant Green
    session.Query<Target>().Count(x => x.TenantIsOneOf("Green")).ShouldBe(10);

    // Query documents on tenant Red
    session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/multi_tenancy.cs#L264-L330' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-mixed-tenancy-non-tenancy-sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In some cases, You may want to disable using the default tenant for storing documents, set `StoreOptions.DefaultTenantUsageEnabled` to `false`. With this option disabled, Tenant (non-default tenant) should be passed via method argument or `SessionOptions` when creating a session using document store. Marten will throw an exception `DefaultTenantUsageDisabledException` if a session is created using default tenant.
