# PostgreSQL Read Replicas

<Badge type="tip" text="7.0" />

Marten leverages Npgsql's `NpgsqlDataSource` capabilities to support PostgreSQL read replicas,
allowing applications to route read-only queries to standby database instances for improved
scalability and availability.

## Configuration

To use read replicas, configure a multi-host `NpgsqlDataSource` and tell Marten to prefer
standby connections for read sessions:

```cs
// Register Npgsql's multi-host data source with primary + replica connection strings
services.AddMultiHostNpgsqlDataSource(
    "Host=primary,replica1,replica2;Database=mydb;Username=app;Password=secret");

services.AddMarten(opts =>
{
    // Tell Marten to prefer read replicas for query sessions
    opts.Advanced.MultiHostSettings.ReadSessionPreference =
        TargetSessionAttributes.PreferStandby;
})
.UseLightweightSessions()
.UseNpgsqlDataSource();
```

With this configuration, `IQuerySession` operations (LINQ queries, loading documents by ID)
will be routed to available read replicas when possible. Write operations through
`IDocumentSession` continue to target the primary.

## How It Works

Npgsql's multi-host data source automatically manages connections across multiple PostgreSQL
hosts. When Marten opens a query session with `PreferStandby`, Npgsql will:

1. Attempt to connect to a standby (replica) host
2. Fall back to the primary if no standby is available
3. Handle connection failover transparently

## Benefits

* **Horizontal read scaling** -- distribute read load across multiple replicas
* **Reduced primary contention** -- offload query-heavy workloads from the primary
* **High availability** -- automatic failover if a replica becomes unavailable

## Considerations

* Read replicas have **replication lag** -- data may be slightly behind the primary
* Write operations (Store, SaveChanges) always target the primary
* The async projection daemon always connects to the primary
* For CQRS command handlers using `FetchForWriting`, the session connects to the primary
  to ensure strong consistency

For more background, see the blog post
[Scaling Marten with PostgreSQL Read Replicas](https://jeremydmiller.com/2024/05/08/scaling-marten-with-postgresql-read-replicas/).
