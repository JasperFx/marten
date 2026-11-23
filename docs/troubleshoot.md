# FAQ & Troubleshooting

## Types of document sessions

| From `IDocumentStore` | Characteristics                                                                                                                                   | Use                                                                                                                                                                                                                                                                                                                                                                                            |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `OpenSession`         | Requires a `SessionOptions` argument; the default `SessionOptions.Tracking` is `DocumentTracking.None`, with isolation level of *Read Committed*. | Reading & writing data. Pass `SessionOptions` with a different `Tracking` value to enable identity-map or dirty-tracking behavior. Without any tracking, incurs the lowest overhead of the writable sessions.                                                                                                                                                                                  |
| `LightweightSession`  | No change tracking, `DocumentTracking.None`, with the default isolation level of *Read Committed*.                                                | Reading & writing data. No caching of objects is done within a session, e.g. repeated loads using the same document identity yield separate objects, each hydrated from the database. In case of updates to such objects, the last object to be stored will overwrite any pending changes from previously stored objects of the same identity. Can incur lower overhead than tracked sessions. |
| `IdentitySession`     | Identity map, `DocumentTracking.IdentityOnly`, with the default isolation level of *Read Committed*.                                              | Reading & writing data. Repeated loads of the same document identity within a session return the same object instance, but changes are not detected: call `IDocumentSession.Store` or `IDocumentSession.Update` for anything you change.                                                                                                                                                       |
| `DirtyTrackedSession` | Track all changes to objects, `DocumentTracking.DirtyTracking`, with the default isolation level of *Read Committed*.                             | Reading & writing data. Tracks all changes to objects loaded through a session. Upon save (`IDocumentSession.SaveChangesAsync()`), Marten updates the changed objects without requiring explicit calls to `IDocumentSession.Update` or `IDocumentSession.Store`. Incurs the largest overhead of tracked sessions.                                                                              |
| `QuerySession`        | No identity mapping with the default isolation level of *Read Committed*.                                                                         | Reading data, i.e. no insert or update operations are exposed.                                                                                                                                                                                                                                                                                                                                 |

## Query throws `NotSupportedException` exception

The most common cause in Marten 10 is a **synchronous LINQ operator**. Marten 10 removed all synchronous data access, but
operators such as `ToList()`, `ToArray()`, `First()`, `Single()`, `Count()`, `Any()` and `foreach` still compile, because
they come from `System.Linq`. They throw a `NotSupportedException` naming the asynchronous operator to use instead:
`ToListAsync()`, `FirstAsync()`, `CountAsync()` and so on. See
[Synchronous data access is gone](/migration-guide#synchronous-data-access-is-gone-in-10-0-0) for the full list of
replacements.

Otherwise, Marten needs to translate LINQ queries to SQL in order to execute them against the database, and this
translation requires explicit support for every query operator used. An operator or method Marten has no translation
for throws a `NotSupportedException` or a `BadLinqExpressionException`. The
[Not Supported](/documents/querying/linq/#not-supported) table lists the common cases and what to do instead. As a
mitigation, consider [mixing raw SQL into the query](/documents/querying/linq/sql) with `MatchesSql()`, or
[filing a feature request](https://github.com/JasperFx/marten/issues/new).

## More diagnostics data outside of Marten

If you cannot obtain the desired diagnostic data through Marten's [diagnostics](/diagnostics), Npgsql's own logging and
metrics can help:

* Set `opts.DisableNpgsqlLogging = false` to forward Npgsql's logging to your application's `ILoggerFactory`. It is off by
  default because it is very noisy. See [Disabling Npgsql Logging](/diagnostics#disabling-npgsql-logging-).
* Npgsql publishes [OpenTelemetry tracing and metrics](https://www.npgsql.org/doc/diagnostics/overview.html) of its own,
  alongside [Marten's](/otel).

Lastly, if you feel that exposing the data should be the responsibility of Marten, consider [filing a feature request](https://github.com/JasperFx/marten/issues/new).

## Full text search error `function to_tsvector(unknown, jsonb) does not exist`

Full text search over `jsonb` needs PostgreSQL 10 or later. Every PostgreSQL version Marten 10 supports has it, so this
error usually means the application is connected to an older server than you expect. Check with `select version();`.
