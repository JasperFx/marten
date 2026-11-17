#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Documents;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using JasperFx;

namespace Marten;

public partial class DocumentStore
{
    // #5619 house style: resolved by name with RequireMethod, behind a Lazy.
    private static readonly Lazy<MethodInfo> _aggregateDocumentJsonOfT = new(() =>
        typeof(DocumentStore).RequireMethod(nameof(aggregateDocumentJsonOfTAsync),
            BindingFlags.NonPublic | BindingFlags.Instance));

    /// <summary>
    /// jasperfx#1082 (marten#5768): the document of the named type with this id, rebuilt now by live-aggregating
    /// its stream through the single-stream projection registered for the type, and serialized by this store's
    /// own serializer -- what an inline or async run of that projection would store, without storing anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The projection is looked up among the top-level registrations only, and only one whose scope is
    /// <see cref="AggregationScope.SingleStream" /> and whose lifecycle stores documents (not Live). That is
    /// deliberately narrower than <c>ProjectionOptions.AggregatorFor&lt;T&gt;()</c>, which also picks a composite's
    /// child or the first aggregator of any scope -- a multi-stream projection has no one source stream per
    /// document, so "rebuild this one" has no answer for it.
    /// </para>
    /// <para>
    /// The aggregation is the live-aggregation path (<c>AggregateStreamAsync</c>'s): the stream's events through
    /// the projection's own apply methods, then the identity set from the stream id. The session is opened for
    /// the database <c>findDiagnosticsDatabaseAsync</c> resolves -- never provisioned -- and the normalized
    /// tenant, so a conjoined event store reads that tenant's stream.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "jasperfx#1082: closes aggregateDocumentJsonOfTAsync<T> over a projected document type, which is preserved at the StoreOptions registration boundary.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "jasperfx#1082: MakeGenericMethod over a mapped document type, which is preserved at the StoreOptions registration boundary.")]
    async Task<string?> IDocumentStoreDiagnostics.AggregateDocumentJsonAsync(
        string documentTypeName, string id, string? tenantId, CancellationToken token)
    {
        var target = resolveDiagnosticsTarget(documentTypeName)
                     ?? throw new ArgumentException(
                         $"'{documentTypeName}' is not a document type this store knows.", nameof(documentTypeName));

        var projection = SingleStreamProjectionFor(target.RequestedType)
                         ?? throw new InvalidOperationException(
                             $"Documents of type {target.RequestedType.FullNameInCode()} are not produced by a single-stream projection registered on this store, so there is no one stream to rebuild a document from. Only a type with DocumentTypeRef.SingleStreamProjection set can be rebuilt.");

        // The id text is the stream's id or key, converted as a load converts it: text that is not a value of
        // the stream identity names no stream.
        object streamId;
        if (Options.EventGraph.StreamIdentity == StreamIdentity.AsGuid)
        {
            if (!Guid.TryParse(id, out var guid))
            {
                return null;
            }

            streamId = guid;
        }
        else
        {
            streamId = id;
        }

        var database = await findDiagnosticsDatabaseAsync(tenantId).ConfigureAwait(false);
        if (database == null)
        {
            return null;
        }

        var tenant = DocumentQueryOptions.NormalizeTenantId(tenantId) ?? StorageConstants.DefaultTenantId;
        await using var session = (QuerySession)QuerySession(SessionOptions.ForDatabase(tenant, database));

        // An async method: everything it throws arrives in the task, never as a TargetInvocationException.
        return await ((Task<string?>)_aggregateDocumentJsonOfT.Value
                .MakeGenericMethod(((IAggregateProjection)projection).AggregateType)
                .Invoke(this, [projection, session, streamId, token])!)
            .ConfigureAwait(false);
    }

    private async Task<string?> aggregateDocumentJsonOfTAsync<T>(
        IProjectionSource<IDocumentOperations, IQuerySession> projection, QuerySession session, object streamId,
        CancellationToken token) where T : class
    {
        var events = streamId is Guid guid
            ? await session.Events.FetchStreamAsync(guid, token: token).ConfigureAwait(false)
            : await session.Events.FetchStreamAsync((string)streamId, token: token).ConfigureAwait(false);

        if (events.Count == 0)
        {
            return null;
        }

        var aggregator = projection as IAggregator<T, IQuerySession>
                         ?? ((IAggregatorSource<IQuerySession>)projection).Build<T>();

        var aggregate = await aggregator.BuildAsync(events, session, null, token).ConfigureAwait(false);
        if (aggregate == null)
        {
            return null;
        }

        if (session.TryGetStorageForLiveAggregation<T>(out var storage))
        {
            if (streamId is Guid g)
            {
                storage!.SetIdentityFromGuid(aggregate, g);
            }
            else
            {
                storage!.SetIdentityFromString(aggregate, (string)streamId);
            }
        }

        return session.Serializer.ToJson(aggregate);
    }

    /// <summary>
    /// jasperfx#1082: the top-level single-stream projection that stores documents of exactly this type, or null.
    /// What <c>DocumentTypeRef.SingleStreamProjection</c> names, and what <c>AggregateDocumentJsonAsync</c> rebuilds
    /// through.
    /// </summary>
    internal IProjectionSource<IDocumentOperations, IQuerySession>? SingleStreamProjectionFor(Type documentType)
        => Options.Projections.All.FirstOrDefault(x =>
            x is IAggregateProjection { Scope: AggregationScope.SingleStream } aggregate
            && aggregate.AggregateType == documentType
            && x.Lifecycle != ProjectionLifecycle.Live);
}
