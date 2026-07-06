using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Marten.Internal.Operations;
using Marten.Services;

namespace Marten.Internal.DirtyTracking;

public class ChangeTracker<T>: IChangeTracker where T : notnull
{
    private readonly T _document;
    private string _json;

    public ChangeTracker(IStorageSession session, T document)
    {
        _document = document;
        _json = session.Serializer.ToCleanJson(document);
    }

    public object Document => _document;

    public bool DetectChanges(IStorageSession session, [NotNullWhen(true)]out IStorageOperation? operation)
    {
        var newJson = session.Serializer.ToCleanJson(_document);

        // Fast path: if the JSON strings are identical, skip expensive parsing
        if (string.Equals(_json, newJson, StringComparison.Ordinal))
        {
            operation = null;
            return false;
        }

        // Slow path: parse and deep-compare to handle semantically equivalent JSON
        // (whitespace, property ordering). 9.0: this used to call
        // Newtonsoft.Json.Linq's JToken.DeepEquals — now uses STJ's JsonNode.DeepEquals
        // so Marten core no longer depends on Newtonsoft.Json.
        if (JsonNode.DeepEquals(JsonNode.Parse(_json), JsonNode.Parse(newJson)))
        {
            operation = null;
            return false;
        }

        // When the session opts out of concurrency checks (Concurrency=Disabled)
        // and the document uses optimistic versioning, route through Overwrite
        // so the SQL drops the WHERE mt_version = ? guard — otherwise a stale
        // version on the dirty-tracking copy turns into a no-op UPDATE and
        // SaveChanges raises ConcurrencyException despite the opt-out.
        var storage = session.Database.Providers.StorageFor<T>().DirtyTracking;
        operation = session.Concurrency == ConcurrencyChecks.Disabled
                    && (storage.UseOptimisticConcurrency || storage.UseNumericRevisions)
            ? storage.Overwrite(_document, session, session.TenantId)
            : storage.Upsert(_document, session, session.TenantId);

        return true;
    }

    public void Reset(IStorageSession session)
    {
        _json = session.Serializer.ToCleanJson(_document);
    }
}
