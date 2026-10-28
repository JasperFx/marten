#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Documents;
using Marten.Schema;
using Marten.Util;

namespace Marten;

/// <summary>
/// The write half of the diagnostics contract (jasperfx#870 §6, #5543): a monitoring console edits,
/// previews an edit (jasperfx#1071), deletes or restores (jasperfx#1059) one document by type name and raw
/// JSON, without referencing Marten.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything goes through a session.</b> Not a raw <c>update ... set data = @json</c>, which is the
/// obvious implementation and the wrong one: it would leave every duplicated-field column stale, skip the
/// version stamp, skip the commit listeners, and turn a soft-deleting type's delete into a hard one. A save
/// here has to be indistinguishable from one the application made, so the JSON is deserialized with the
/// store's own serializer and written through <c>LightweightSession</c>.
/// </para>
/// <para>
/// A separate interface from <see cref="IDocumentStoreDiagnostics"/> on purpose, so a host can register the
/// reader alone and offer browsing without editing. Both land on <c>DocumentStore</c> because both need the
/// store's serializer and sessions, and both answer with the same <see cref="Subject"/>.
/// </para>
/// </remarks>
public partial class DocumentStore : IDocumentStoreDiagnosticsWriter
{
    private static readonly Lazy<MethodInfo> _updateExpectedVersion = new(() =>
        typeof(IDocumentOperations).RequireMethod(nameof(IDocumentOperations.UpdateExpectedVersion),
            BindingFlags.Public | BindingFlags.Instance));

    async Task<DocumentWriteResult> IDocumentStoreDiagnosticsWriter.SaveDocumentJsonAsync(
        DocumentWriteRequest request, CancellationToken token)
    {
        var (target, document) = deserializeForWrite(request);
        var tenantId = DocumentQueryOptions.NormalizeTenantId(request.TenantId);

        if (request.ExpectedVersion != null)
        {
            // The strong form, where the mapping's own configuration gives us one: for a type that opted into
            // Guid optimistic concurrency, UpdateExpectedVersion puts the check in the UPDATE's own WHERE
            // clause, so the compare and the write share a single statement and cannot race.
            //
            // Gated on UseOptimisticConcurrency and NOT merely on the mt_version column existing, which was
            // the first thing tried and is wrong: every mapping has an mt_version column, but
            // IDocumentStorage.Upsert only emits the mt_expected_version guard when the mapping opted in, so
            // on an ordinary type UpdateExpectedVersion silently writes unconditionally. That failed
            // a_stale_expected_version_is_refused_with_the_current_document by reporting Saved -- which is
            // exactly the "guarded write that isn't" the contract is trying to stamp out.
            if (target.Mapping.UseOptimisticConcurrency && Guid.TryParse(request.ExpectedVersion, out var expected))
            {
                try
                {
                    await using var guarded = openDiagnosticsSession(tenantId);
                    _updateExpectedVersion.Value
                        .MakeGenericMethod(document.GetType())
                        .Invoke(guarded, [document, expected]);

                    await guarded.SaveChangesAsync(token).ConfigureAwait(false);
                }
                catch (TargetInvocationException e) when (e.InnerException != null)
                {
                    throw e.InnerException;
                }
                catch (ConcurrencyException)
                {
                    return await conflictAsync(request.DocumentTypeName, request.Id, tenantId, token)
                        .ConfigureAwait(false);
                }

                return await savedAsync(request.DocumentTypeName, request.Id, tenantId, token).ConfigureAwait(false);
            }

            // Every other mapping -- which is most of them, since optimistic concurrency is opt-in: compare
            // the token we handed out against the current one. Not atomic with the write below, and
            // deliberately left that way rather than papered over with a lock. The window is between two
            // statements on one connection, it only matters when two consoles edit the SAME document inside
            // it, and the loser's edit is still recorded in the version history; serializing every diagnostic
            // write to close it would be a poor trade. The reference implementation in JasperFx makes the
            // same call, in the same words.
            var current = await loadStoredDocumentAsync(request.DocumentTypeName, request.Id, tenantId, token)
                .ConfigureAwait(false);

            if (current == null || current.IsDeleted || current.Version != request.ExpectedVersion)
            {
                return new DocumentWriteResult(DocumentWriteStatus.ConcurrencyConflict,
                    current is { IsDeleted: false } ? current : null);
            }
        }

        await using (var session = openDiagnosticsSession(tenantId))
        {
            // StoreObjects rather than a reflected Store<T>: Marten resolves storage from each document's
            // runtime type, which is what makes a sub-class in a hierarchy land in its root's table with the
            // right mt_doc_type.
            session.StoreObjects([document]);
            await session.SaveChangesAsync(token).ConfigureAwait(false);
        }

        return await savedAsync(request.DocumentTypeName, request.Id, tenantId, token).ConfigureAwait(false);
    }

    /// <summary>
    /// jasperfx#1071: a dry run of <see cref="IDocumentStoreDiagnosticsWriter.SaveDocumentJsonAsync" />. The
    /// same deserialize step and the same refusals as the save (<see cref="deserializeForWrite" />), then the
    /// document re-serialized with the store's own serializer, which is what the save's upsert binds to the
    /// <c>data</c> column. No session is opened, so nothing is read or written and
    /// <see cref="DocumentWriteRequest.ExpectedVersion" /> plays no part.
    /// </summary>
    Task<string> IDocumentStoreDiagnosticsWriter.PreviewDocumentJsonAsync(
        DocumentWriteRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var (_, document) = deserializeForWrite(request);

        return Task.FromResult(Serializer.ToJson(document));
    }

    async Task<DocumentWriteResult> IDocumentStoreDiagnosticsWriter.DeleteDocumentAsync(
        DocumentDeleteRequest request, CancellationToken token)
    {
        var target = resolveDiagnosticsTargetForWrite(request.DocumentTypeName);
        var tenantId = DocumentQueryOptions.NormalizeTenantId(request.TenantId);

        var current = await loadStoredDocumentAsync(request.DocumentTypeName, request.Id, tenantId, token)
            .ConfigureAwait(false);

        // NotFound is decided BEFORE the version check, and an already soft-deleted row counts as not found:
        // there is no live document to delete, and answering ConcurrencyConflict for a row that is already
        // gone would tell a console to go and look at a change that never happened.
        if (current == null || current.IsDeleted)
        {
            return new DocumentWriteResult(DocumentWriteStatus.NotFound);
        }

        if (request.ExpectedVersion != null && current.Version != request.ExpectedVersion)
        {
            return new DocumentWriteResult(DocumentWriteStatus.ConcurrencyConflict, current);
        }

        // Deserialized from the row we just read rather than constructed from the id, so the delete runs
        // through the same DeleteObjects path an application uses -- which is what makes a soft-deleting type
        // soft-delete here instead of vanishing.
        var document = deserialize(typeFor(target, current), current.Json);

        await using (var session = openDiagnosticsSession(tenantId))
        {
            session.DeleteObjects([document]);
            await session.SaveChangesAsync(token).ConfigureAwait(false);
        }

        return new DocumentWriteResult(DocumentWriteStatus.Deleted);
    }

    private static readonly Lazy<MethodInfo> _queueUndelete = new(() =>
        typeof(DocumentStore).RequireMethod(nameof(queueUndelete), BindingFlags.NonPublic | BindingFlags.Static));

    /// <summary>
    /// jasperfx#1059 (#5740): restore one soft-deleted document — <c>UndoDeleteWhere</c> by type name and id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>UndoDeleteWhere</c> alone is not enough</b>, because it only clears <c>mt_deleted</c> and
    /// <c>mt_deleted_at</c>: the row would come back live with the version it was deleted at. A console that
    /// read the deleted row and then saw a restore and a second delete behind its back would hold a version
    /// token that still matched, so a stale undelete would go through. The document is therefore also stored
    /// again, in the same session and so the same transaction, which is what stamps a new version and
    /// last-modified time, refreshes the duplicated fields and fires the commit listeners — an undelete that
    /// is indistinguishable from one the application made, for the reason the class remarks give for saves.
    /// <c>UndoDeleteWhere</c> itself is left as it is: changing what it writes would change the public API's
    /// behaviour for every application that calls it.
    /// </para>
    /// <para>
    /// <c>ExpectedVersion</c> is compared rather than put in the statement, with the same reasoning and the
    /// same window as <see cref="IDocumentStoreDiagnosticsWriter.DeleteDocumentAsync" />.
    /// </para>
    /// </remarks>
    async Task<DocumentWriteResult> IDocumentStoreDiagnosticsWriter.UndeleteDocumentAsync(
        DocumentUndeleteRequest request, CancellationToken token)
    {
        var target = resolveDiagnosticsTargetForWrite(request.DocumentTypeName);

        // A type that hard-deletes has nothing to restore. Answered rather than thrown, so a console can offer
        // the action per type without guarding the call -- and before any read, so the answer does not depend
        // on whether a row happens to exist.
        if (!target.IsSoftDeleted)
        {
            return new DocumentWriteResult(DocumentWriteStatus.NotSupported);
        }

        var tenantId = DocumentQueryOptions.NormalizeTenantId(request.TenantId);

        var current = await loadStoredDocumentAsync(request.DocumentTypeName, request.Id, tenantId, token)
            .ConfigureAwait(false);

        // A live row is NotFound too: an undelete that did nothing must not look like one that succeeded.
        if (current is not { IsDeleted: true })
        {
            return new DocumentWriteResult(DocumentWriteStatus.NotFound);
        }

        if (request.ExpectedVersion != null && current.Version != request.ExpectedVersion)
        {
            return new DocumentWriteResult(DocumentWriteStatus.ConcurrencyConflict, current);
        }

        var documentType = typeFor(target, current);
        var document = deserialize(documentType, current.Json);
        var id = valueOf(target.Mapping.IdMember, document)
                 ?? throw new InvalidOperationException(
                     $"The stored '{request.DocumentTypeName}' document '{request.Id}' deserialized without an id.");

        // A numeric revision has to be advanced explicitly: a plain Store() of a revisioned type takes the
        // revision the document carries as the expected one and is refused. current + 1 is also a guard in the
        // statement itself, so a concurrent write to the same row loses with a ConcurrencyException rather than
        // being overwritten.
        //
        // Not for a projection document (#5773). Its revision is the version of the stream it reflects, and
        // current + 1 would put it ahead of the stream, so the async fetch's delta query would skip the next
        // event. A plain Store() is accepted there, and takes the revision from the stream again.
        long? nextRevision = target.Mapping.UseNumericRevisions && !target.Mapping.UseVersionFromMatchingStream &&
                             long.TryParse(current.Version, out var revision)
            ? revision + 1
            : null;

        try
        {
            await using var session = openDiagnosticsSession(tenantId);
            _queueUndelete.Value.MakeGenericMethod(documentType)
                .Invoke(null, [session, target.Mapping.IdMember, document, id, nextRevision]);

            await session.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            throw e.InnerException;
        }
        catch (ConcurrencyException)
        {
            var latest = await loadStoredDocumentAsync(request.DocumentTypeName, request.Id, tenantId, token)
                .ConfigureAwait(false);
            return new DocumentWriteResult(DocumentWriteStatus.ConcurrencyConflict, latest);
        }

        return new DocumentWriteResult(DocumentWriteStatus.Undeleted,
            await loadStoredDocumentAsync(request.DocumentTypeName, request.Id, tenantId, token).ConfigureAwait(false));
    }

    /// <summary>
    /// <c>UndoDeleteWhere&lt;T&gt;(x =&gt; x.Id == id)</c> and the re-store, for a document type known only at
    /// runtime, so the soft-delete exclusion, sub-class narrowing and tenant scoping are the ones Marten applies
    /// to every other <c>UndoDeleteWhere</c>.
    /// </summary>
    private static void queueUndelete<T>(IDocumentSession session, MemberInfo idMember, T document, object id,
        long? nextRevision) where T : notnull
    {
        // The upsert is queued FIRST, and that order is load-bearing. Queued after the UndoDeleteWhere statement,
        // a numeric-revision upsert in the same batch reports a ConcurrencyException that did not happen, even
        // though the row's revision is below the one requested.
        if (nextRevision.HasValue)
        {
            session.UpdateRevision(document, nextRevision.Value);
        }
        else
        {
            session.Store(document);
        }

        // Marten's upsert already writes mt_deleted from the document, so for most types the row is live after
        // the line above. UndoDeleteWhere is what says so explicitly, and what clears mt_deleted_at whatever the
        // document carries.
        var x = Expression.Parameter(typeof(T), "x");
        var member = Expression.MakeMemberAccess(x, idMember);
        var body = Expression.Equal(member, Expression.Constant(id, member.Type));

        session.UndoDeleteWhere(Expression.Lambda<Func<T, bool>>(body, x));
    }

    private IDocumentSession openDiagnosticsSession(string? normalizedTenantId)
        => normalizedTenantId == null ? LightweightSession() : LightweightSession(normalizedTenantId);

    private Task<StoredDocument?> loadStoredDocumentAsync(
        string documentTypeName, string id, string? tenantId, CancellationToken token)
        => ((IDocumentStoreDiagnostics)this).LoadDocumentAsync(documentTypeName, id, tenantId, token);

    private async Task<DocumentWriteResult> savedAsync(
        string documentTypeName, string id, string? tenantId, CancellationToken token)
        => new(DocumentWriteStatus.Saved,
            await loadStoredDocumentAsync(documentTypeName, id, tenantId, token).ConfigureAwait(false));

    private async Task<DocumentWriteResult> conflictAsync(
        string documentTypeName, string id, string? tenantId, CancellationToken token)
    {
        var current = await loadStoredDocumentAsync(documentTypeName, id, tenantId, token).ConfigureAwait(false);
        return new DocumentWriteResult(DocumentWriteStatus.ConcurrencyConflict,
            current is { IsDeleted: false } ? current : null);
    }

    /// <summary>
    /// An unknown type name is an <see cref="ArgumentException"/> on the write side, where a read answers
    /// with an empty page: a write that did nothing must not look like one that succeeded.
    /// </summary>
    private DiagnosticsTarget resolveDiagnosticsTargetForWrite(string documentTypeName)
        => resolveDiagnosticsTarget(documentTypeName)
           ?? throw new ArgumentException(
               $"'{documentTypeName}' is not a document type this store maps. Read IDocumentStoreDiagnostics.DocumentTypesAsync() for the names it accepts.",
               nameof(documentTypeName));

    /// <summary>
    /// The row's own type where a hierarchy makes that different from the type that was named — a delete
    /// against the root name still has to deserialize a bus as a bus.
    /// </summary>
    private Type typeFor(DiagnosticsTarget target, StoredDocument document)
    {
        if (document.DocumentType == null)
        {
            return target.RequestedType;
        }

        if (target.RequestedType.FullNameInCodeEquals(document.DocumentType))
        {
            return target.RequestedType;
        }

        var subClass = target.Mapping.SubClasses
            .FirstOrDefault(x => x.DocumentType.FullNameInCodeEquals(document.DocumentType));

        return subClass?.DocumentType ?? target.RequestedType;
    }

    /// <summary>
    /// The half of a save that decides what would be stored, shared by
    /// <see cref="IDocumentStoreDiagnosticsWriter.SaveDocumentJsonAsync" /> and
    /// <see cref="IDocumentStoreDiagnosticsWriter.PreviewDocumentJsonAsync" /> so a preview can never accept
    /// what a save refuses, or the other way round: resolve the type name (unknown is an
    /// <see cref="ArgumentException"/>), deserialize with the store's serializer, and refuse an id in the JSON
    /// that disagrees with the one the request names.
    /// </summary>
    private (DiagnosticsTarget Target, object Document) deserializeForWrite(DocumentWriteRequest request)
    {
        var target = resolveDiagnosticsTargetForWrite(request.DocumentTypeName);

        var document = deserialize(target.RequestedType, request.Json);

        if (!idsAgree(target.Mapping, document, request.Id))
        {
            throw new ArgumentException(
                $"The id in the JSON does not match the requested id '{request.Id}'. A diagnostic save will not guess which one was meant.",
                nameof(request));
        }

        return (target, document);
    }

    private object deserialize(Type documentType, string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return Serializer.FromJson(documentType, stream)
               ?? throw new ArgumentException($"The JSON deserialized to null for '{documentType.FullName}'.",
                   nameof(json));
    }

    /// <summary>
    /// Does the id carried inside the JSON agree with the one the request names?
    /// </summary>
    /// <remarks>
    /// Compared as values rather than as text, because the text forms legitimately differ: PostgreSQL renders
    /// a Guid lower-case and a console may well echo back the upper-case form it was given.
    /// </remarks>
    private static bool idsAgree(DocumentMapping mapping, object document, string requestedId)
    {
        var raw = valueOf(mapping.IdMember, document);

        return raw switch
        {
            null => false,
            Guid guid => Guid.TryParse(requestedId, out var parsed) && guid == parsed,
            int i => int.TryParse(requestedId, out var parsed) && i == parsed,
            long l => long.TryParse(requestedId, out var parsed) && l == parsed,
            string s => string.Equals(s, requestedId, StringComparison.Ordinal),
            // A strong-typed identifier wrapper: its ToString() is the inner value's, which is what the
            // column holds and therefore what a read handed the caller.
            _ => string.Equals(raw.ToString(), requestedId, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static object? valueOf(MemberInfo member, object document)
        => member switch
        {
            PropertyInfo property => property.GetValue(document),
            FieldInfo field => field.GetValue(document),
            _ => null
        };
}

internal static class DiagnosticsTypeNameExtensions
{
    public static bool FullNameInCodeEquals(this Type type, string name)
        => string.Equals(type.FullNameInCode(), name, StringComparison.OrdinalIgnoreCase);
}
