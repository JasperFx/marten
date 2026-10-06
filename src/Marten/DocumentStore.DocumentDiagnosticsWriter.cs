#nullable enable
using System;
using System.IO;
using System.Linq;
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
/// The write half of the diagnostics contract (jasperfx#870 §6, #5543): a monitoring console edits or
/// deletes one document by type name and raw JSON, without referencing Marten.
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
    private static readonly MethodInfo _updateExpectedVersion =
        typeof(IDocumentOperations).RequireMethod(nameof(IDocumentOperations.UpdateExpectedVersion),
            BindingFlags.Public | BindingFlags.Instance);

    async Task<DocumentWriteResult> IDocumentStoreDiagnosticsWriter.SaveDocumentJsonAsync(
        DocumentWriteRequest request, CancellationToken token)
    {
        var target = resolveDiagnosticsTargetForWrite(request.DocumentTypeName);
        var tenantId = DocumentQueryOptions.NormalizeTenantId(request.TenantId);

        var document = deserialize(target.RequestedType, request.Json);

        if (!idsAgree(target.Mapping, document, request.Id))
        {
            throw new ArgumentException(
                $"The id in the JSON does not match the requested id '{request.Id}'. A diagnostic save will not guess which one was meant.",
                nameof(request));
        }

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
                    _updateExpectedVersion
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
