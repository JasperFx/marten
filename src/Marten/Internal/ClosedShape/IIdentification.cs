#nullable enable
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Spike: shared per-document-type identity-strategy contract for the W3
/// closed-shape document-storage hierarchy ([#4404](https://github.com/JasperFx/marten/issues/4404)).
///
/// <para>
/// Replaces today's <c>IIdGeneration</c> (codegen-driven — emits identity
/// reads + assignments into a per-document storage class at boot time) with
/// a runtime contract that closed-shape <c>DocumentStorage&lt;TDoc, TId&gt;</c>
/// subclasses compose with. One implementation per identity strategy
/// (sequential GUID, Hilo int, Hilo long, identity-key, strong-typed IDs)
/// times one storage subclass per <c>(StorageStyle × Concurrency × Hierarchical)</c>
/// tuple — additive rather than combinatorial.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Per-call cost is one virtual call into the strategy plus whatever the
/// strategy's body does (a getter delegate read for the no-op path; a
/// sequence-or-CombGuid call for the generate path). No allocations on the
/// "already has an id" hot path.
/// </para>
/// <para>
/// In the source-generator-output world ([JasperFx/jasperfx#276](https://github.com/JasperFx/jasperfx/issues/276) / W5),
/// the getter + setter delegates each strategy carries are emitted by the
/// generator into a sibling <c>IDocumentAccessor&lt;TDoc, TId&gt;</c> type.
/// For the spike, callers supply them at construction.
/// </para>
/// </remarks>
public interface IIdentification<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    /// <summary>
    /// Read the current identity from a document instance. Pure — no
    /// side effects, no database access. Returns the default <typeparamref name="TId"/>
    /// value if the document hasn't been assigned an id yet (callers use that
    /// to decide whether to generate one).
    /// </summary>
    TId Identity(TDoc document);

    /// <summary>
    /// Idempotent identity assignment. If the document already has a
    /// non-default id, return that unchanged (no allocations, no
    /// database round-trip). Otherwise generate a new id by the strategy's
    /// rules (CombGuid / Hilo / identity-key / ...), write it onto the
    /// document via the strategy's setter, and return the new value.
    /// </summary>
    /// <param name="document">The document to inspect and potentially mutate.</param>
    /// <param name="database">
    /// The active <see cref="IMartenDatabase"/> — strategies that need a
    /// database sequence (Hilo, identity-key) read it here. Strategies that
    /// don't (sequential GUID, externally-assigned string keys) ignore it.
    /// </param>
    TId AssignIfMissing(TDoc document, IMartenDatabase database);

    /// <summary>
    /// Convert an id to the value Postgres should bind — for primitive
    /// id types this is the id itself; strong-typed wrappers return the
    /// inner primitive (matching <c>DocumentStorage.RawIdentityValue</c>
    /// emit in the codegen path). Default implementation just boxes
    /// <paramref name="id"/> through <see cref="object"/>.
    /// </summary>
    object ToRawSqlValue(TId id) => id!;

    /// <summary>
    /// The Postgres parameter type matching <see cref="ToRawSqlValue"/>.
    /// For primitives this is the same as <typeparamref name="TId"/>;
    /// strong-typed wrappers return the inner primitive type. Operations
    /// use this to set <see cref="Npgsql.NpgsqlParameter.NpgsqlDbType"/>
    /// instead of looking it up from <c>typeof(TId)</c>.
    /// </summary>
    System.Type RawSqlType => typeof(TId);
}
