using System.Security.Cryptography;
using System.Text;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Npgsql;
using Pgvector;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.PgVector.Projection;

/// <summary>
/// Base class for projections that maintain a vector embedding table.
/// Events are mapped to text content, which is embedded via an IEmbeddingProvider
/// and stored alongside content hashes to skip re-embedding unchanged content.
///
/// Register with: opts.Projections.Add(new MyVectorProjection(provider), ProjectionLifecycle.Async);
/// Create the schema table via: opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(schemaName));
/// </summary>
public abstract class VectorProjection : IProjection
{
    private readonly IEmbeddingProvider _provider;
    private readonly string _tableName;
    private readonly List<IVectorEventMapping> _mappings = new();
    private readonly List<Type> _deleteTypes = new();

    protected VectorProjection(string tableName, IEmbeddingProvider provider)
    {
        _tableName = tableName;
        _provider = provider;

        Configure(new VectorProjectionMapping(this));
    }

    /// <summary>
    /// Override to define how events map to text content for embedding.
    /// </summary>
    protected abstract void Configure(VectorProjectionMapping map);

    /// <summary>
    /// Build the Weasel Table object for this projection's embedding storage.
    /// Register via opts.Storage.ExtendedSchemaObjects.Add(table).
    /// </summary>
    public Table BuildTable(string schemaName)
    {
        var table = new Table(new PostgresqlObjectName(schemaName, _tableName));
        table.AddColumn<Guid>("id").AsPrimaryKey();
        table.AddColumn("embedding", $"vector({_provider.Dimensions})").NotNull();
        table.AddColumn<string>("content_text");
        table.AddColumn<string>("content_hash").NotNull();
        table.AddColumn("metadata", "jsonb");
        table.AddColumn("last_updated", "timestamptz").NotNull().DefaultValueByExpression("now()");
        return table;
    }

    /// <summary>
    /// The qualified table name for use in queries.
    /// </summary>
    public string QualifiedTableName(string schemaName) => $"{schemaName}.{_tableName}";

    public string TableName => _tableName;

    #region IProjection

    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        throw new NotSupportedException("VectorProjection requires async execution");
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation)
    {
        var allEvents = streams.SelectMany(s => s.Events).ToList();
        return ApplyEventsAsync(operations, allEvents, cancellation);
    }

    // IJasperFxProjection<IDocumentOperations>
    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        return ApplyEventsAsync(operations, events, cancellation);
    }

    private async Task ApplyEventsAsync(IDocumentOperations operations, IReadOnlyList<IEvent> allEvents,
        CancellationToken cancellation)
    {
        if (allEvents.Count == 0) return;

        var store = (DocumentStore)operations.DocumentStore;
        var schemaName = store.Options.Events.DatabaseSchemaName;
        var qualifiedTable = QualifiedTableName(schemaName);

        // Collect content extractions and deletes
        var extractions = new List<(Guid Id, string Content)>();
        var deletions = new List<Guid>();

        foreach (var @event in allEvents)
        {
            if (_deleteTypes.Contains(@event.EventType))
            {
                deletions.Add(@event.StreamId);
                continue;
            }

            foreach (var mapping in _mappings)
            {
                if (mapping.EventType == @event.EventType)
                {
                    var id = mapping.ExtractId(@event);
                    var content = mapping.ExtractContent(@event);
                    if (content != null)
                    {
                        extractions.Add((id, content));
                    }
                    break;
                }
            }
        }

        if (extractions.Count == 0 && deletions.Count == 0) return;

        var database = store.Storage.Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(cancellation).ConfigureAwait(false);

        // Process deletions
        foreach (var id in deletions)
        {
            var delCmd = conn.CreateCommand();
            delCmd.CommandText = $"DELETE FROM {qualifiedTable} WHERE id = $1";
            delCmd.Parameters.Add(new NpgsqlParameter { Value = id });
            await delCmd.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
        }

        if (extractions.Count == 0) return;

        // Fetch existing content hashes
        var ids = extractions.Select(e => e.Id).Distinct().ToArray();
        var existingHashes = new Dictionary<Guid, string>();

        var hashCmd = conn.CreateCommand();
        hashCmd.CommandText = $"SELECT id, content_hash FROM {qualifiedTable} WHERE id = ANY($1)";
        hashCmd.Parameters.Add(new NpgsqlParameter { Value = ids });

        await using (var reader = await hashCmd.ExecuteReaderAsync(cancellation).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                existingHashes[reader.GetGuid(0)] = reader.GetString(1);
            }
        }

        // Filter to only items needing new embeddings (content changed)
        var needsEmbedding = new List<(Guid Id, string Content, string Hash)>();
        foreach (var (id, content) in extractions)
        {
            var hash = ComputeHash(content);
            if (existingHashes.TryGetValue(id, out var existing) && existing == hash)
                continue;

            needsEmbedding.Add((id, content, hash));
        }

        if (needsEmbedding.Count == 0) return;

        // Batch-generate embeddings
        var texts = needsEmbedding.Select(e => e.Content).ToArray();
        var embeddings = await _provider.GenerateEmbeddingsAsync(texts, cancellation).ConfigureAwait(false);

        // Upsert rows
        for (int i = 0; i < needsEmbedding.Count; i++)
        {
            var (id, content, hash) = needsEmbedding[i];
            var embedding = embeddings[i];

            // See PgVectorExtensions.VectorSearchAsync — bind the embedding as
            // its text form and cast to vector(N) server-side. The
            // NpgsqlDataSource type-info cache is unreliable across schema
            // migrations that create the "vector" extension.
            var dimensions = _provider.Dimensions;
            var upsertCmd = conn.CreateCommand();
            upsertCmd.CommandText =
                $"INSERT INTO {qualifiedTable} (id, embedding, content_text, content_hash, last_updated) " +
                $"VALUES ($1, $2::vector({dimensions}), $3, $4, now()) " +
                $"ON CONFLICT (id) DO UPDATE SET embedding = $2::vector({dimensions}), content_text = $3, content_hash = $4, last_updated = now()";

            upsertCmd.Parameters.Add(new NpgsqlParameter { Value = id });
            upsertCmd.Parameters.Add(new NpgsqlParameter { Value = embedding.ToString(), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
            upsertCmd.Parameters.Add(new NpgsqlParameter { Value = content });
            upsertCmd.Parameters.Add(new NpgsqlParameter { Value = hash });

            await upsertCmd.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
        }
    }

    #endregion

    #region Internals

    internal void AddMapping(IVectorEventMapping mapping)
    {
        _mappings.Add(mapping);
    }

    internal void AddDeleteType(Type eventType)
    {
        _deleteTypes.Add(eventType);
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    #endregion
}

/// <summary>
/// Fluent API for configuring event-to-content mappings in a VectorProjection.
/// </summary>
public class VectorProjectionMapping
{
    private readonly VectorProjection _projection;

    internal VectorProjectionMapping(VectorProjection projection)
    {
        _projection = projection;
    }

    /// <summary>
    /// Map an event type to text content for embedding.
    /// </summary>
    public VectorProjectionMapping Map<TEvent>(
        Func<TEvent, string> contentSelector,
        Func<TEvent, Guid>? idSelector = null)
    {
        _projection.AddMapping(new VectorEventMapping<TEvent>(contentSelector, idSelector));
        return this;
    }

    /// <summary>
    /// Register an event type that causes the embedding row to be deleted.
    /// </summary>
    public VectorProjectionMapping Delete<TEvent>()
    {
        _projection.AddDeleteType(typeof(TEvent));
        return this;
    }
}

internal interface IVectorEventMapping
{
    Type EventType { get; }
    Guid ExtractId(IEvent @event);
    string? ExtractContent(IEvent @event);
}

internal class VectorEventMapping<TEvent> : IVectorEventMapping
{
    private readonly Func<TEvent, string> _contentSelector;
    private readonly Func<TEvent, Guid>? _idSelector;

    public VectorEventMapping(Func<TEvent, string> contentSelector, Func<TEvent, Guid>? idSelector)
    {
        _contentSelector = contentSelector;
        _idSelector = idSelector;
    }

    public Type EventType => typeof(TEvent);

    public Guid ExtractId(IEvent @event)
    {
        if (_idSelector != null)
            return _idSelector((TEvent)@event.Data);
        return @event.StreamId;
    }

    public string? ExtractContent(IEvent @event)
    {
        try { return _contentSelector((TEvent)@event.Data); }
        catch { return null; }
    }
}
