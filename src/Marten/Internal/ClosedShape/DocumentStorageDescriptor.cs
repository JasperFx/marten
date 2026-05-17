#nullable enable
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M7): which concurrency model the document mapping uses.
/// Drives WHERE-clause additions on UPDATE/UPSERT, RETURNING column
/// choice, and Postprocess exception type. Encoded once on the
/// descriptor so the operation classes can switch on it without
/// reading mapping state per call.
/// </summary>
public enum ConcurrencyMode
{
    /// <summary>No optimistic concurrency. Updates use plain WHERE id = ?.</summary>
    Off,

    /// <summary>
    /// Guid-based optimistic concurrency. Each write generates a fresh
    /// Guid version; UPDATE / UPSERT add <c>and mt_version = ?</c> to
    /// the predicate and RETURN the new version for postprocess
    /// validation. A miss (no row returned) raises
    /// <c>ConcurrencyException</c> instead of
    /// <c>NonExistentDocumentException</c>.
    /// </summary>
    Optimistic,

    /// <summary>
    /// Monotonic-bigint revisions (Marten's <c>UseNumericRevisions</c>).
    /// Each write either auto-increments (caller passes
    /// <c>Revision = 0</c>) or supplies an explicit revision that must
    /// be strictly greater than the current row's
    /// <c>mt_version</c>. Mirrors codegen
    /// <c>UpsertFunction</c> with <c>RevisionArgument</c>.
    /// </summary>
    Numeric
}

/// <summary>
/// W3 spike (M1): per-mapping descriptor that the closed-shape document
/// storage class composes with at construction. Holds the pre-built SQL
/// strings + the ordered metadata-binder arrays. Built once per
/// <see cref="DocumentMapping"/>; held as a <c>readonly</c> field on the
/// storage instance. Closed-shape equivalent of <c>RichEventStorageDescriptor</c>
/// from W4.
/// </summary>
public sealed class DocumentStorageDescriptor<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    internal DocumentStorageDescriptor(
        IIdentification<TDoc, TId> identification,
        IDocumentMetadataBinder<TDoc>[] clientSideWriteBinders,
        IDocumentMetadataBinder<TDoc>[] writeBinders,
        IDocumentMetadataBinder<TDoc>[] readBinders,
        string upsertSql,
        string insertSql,
        string updateSql,
        string overwriteSql,
        bool isConjoined,
        ConcurrencyMode concurrencyMode,
        DocumentVersionBinder<TDoc>? versionBinder,
        DocumentRevisionBinder<TDoc>? revisionBinder,
        int versionReadIndex,
        Marten.Schema.DocumentMapping? hierarchyMapping,
        int docTypeReadIndex,
        string tableName,
        IDocumentMetadataBinder<TDoc>[]? partitionPkBinders = null)
    {
        Identification = identification;
        ClientSideWriteBinders = clientSideWriteBinders;
        WriteBinders = writeBinders;
        ReadBinders = readBinders;
        TableName = tableName;
        UpsertSql = upsertSql;
        InsertSql = insertSql;
        UpdateSql = updateSql;
        OverwriteSql = overwriteSql;
        IsConjoined = isConjoined;
        ConcurrencyMode = concurrencyMode;
        VersionBinder = versionBinder;
        RevisionBinder = revisionBinder;
        VersionReadIndex = versionReadIndex;
        HierarchyMapping = hierarchyMapping;
        DocTypeReadIndex = docTypeReadIndex;
        PartitionPkBinders = partitionPkBinders ?? System.Array.Empty<IDocumentMetadataBinder<TDoc>>();
    }

    /// <summary>
    /// Writers that bind the partition PK columns. For Update/Upsert on
    /// partitioned tables whose PK includes the partition column (e.g.
    /// list-partitioned by mt_deleted, range-partitioned by a duplicated
    /// date field) the WHERE clause needs to filter on those columns too
    /// — otherwise UPDATE … WHERE id = ? targets every partition row
    /// with that id and produces a PK violation when the new value moves
    /// it back into another row's slot. Order matches the SQL emit.
    /// </summary>
    internal IDocumentMetadataBinder<TDoc>[] PartitionPkBinders { get; }

    public IIdentification<TDoc, TId> Identification { get; }

    /// <summary>
    /// Subset of the metadata binders that consume a <c>?</c> parameter
    /// slot in the VALUES list. Server-side binders (e.g.
    /// <c>transaction_timestamp()</c> for <c>mt_last_modified</c>) are
    /// excluded — their literal SQL is baked into <see cref="UpsertSql"/>.
    /// </summary>
    public IDocumentMetadataBinder<TDoc>[] ClientSideWriteBinders { get; }

    /// <summary>
    /// W3 spike (M16): all write binders — client-side <em>and</em>
    /// server-side. The COPY path uses this because the binary protocol
    /// can't run inline SQL literals (<c>transaction_timestamp()</c>),
    /// so each binder writes a client-computed value via
    /// <see cref="IDocumentMetadataBinder{TDoc}.WriteToBulkAsync"/> —
    /// including LastModified, which computes <c>UtcNow</c> instead of
    /// emitting <c>transaction_timestamp()</c>.
    /// </summary>
    internal IDocumentMetadataBinder<TDoc>[] WriteBinders { get; }

    /// <summary>
    /// Unqualified table name (without schema prefix) — used by the
    /// exception-transform path to detect when a Postgres
    /// unique-constraint violation belongs to this document type's
    /// table so it can be surfaced as
    /// <see cref="Marten.Exceptions.DocumentAlreadyExistsException"/>.
    /// </summary>
    internal string TableName { get; }

    /// <summary>
    /// All metadata binders applied on the read path, in column order
    /// starting at <see cref="DataColumnIndex"/> + 1. Includes both
    /// client-side and server-side binders — on read they're symmetric
    /// (each binder consumes one result column).
    /// </summary>
    public IDocumentMetadataBinder<TDoc>[] ReadBinders { get; }

    /// <summary>
    /// Full upsert SQL with <c>?</c> placeholders for client-side
    /// parameters and inline literals for server-side ones — e.g.
    /// <c>"insert into schema.mt_doc_foo (id, data, mt_version, mt_dotnet_type, mt_last_modified) values (?, ?, ?, ?, transaction_timestamp()) on conflict (id) do update set …"</c>.
    /// Fed to <c>ICommandBuilder.AppendWithParameters(sql, '?')</c> at
    /// write time; the returned <c>NpgsqlParameter[]</c> is filled by the
    /// operation in order: id, data, then each
    /// <see cref="ClientSideWriteBinders"/> entry.
    /// </summary>
    public string UpsertSql { get; }

    /// <summary>
    /// SQL for the Insert path —
    /// <c>"insert into … (id, data, …) values (?, ?, …) on conflict (id) do nothing returning id"</c>.
    /// The trailing RETURNING is consumed by
    /// <see cref="ClosedShapeInsertOperation{TDoc, TId}"/>.Postprocess so
    /// a missing row (conflict) raises <c>DocumentAlreadyExistsException</c>.
    /// Parameter order matches <see cref="UpsertSql"/>: id, data, then
    /// each client-side binder.
    /// </summary>
    public string InsertSql { get; }

    /// <summary>
    /// SQL for the Update path —
    /// <c>"update … set data = ?, mt_version = ?, … where id = ? returning id"</c>.
    /// Parameter order: data first, then each client-side binder, then id
    /// (the WHERE clause). Postprocess raises
    /// <c>NonExistentDocumentException</c> when no row comes back.
    /// </summary>
    public string UpdateSql { get; }

    /// <summary>
    /// SQL for the Overwrite path — identical to <see cref="UpsertSql"/>
    /// when <see cref="ConcurrencyMode"/> is <c>Off</c>; under optimistic
    /// concurrency the trailing WHERE filter on <c>mt_version</c> is
    /// stripped so the write always wins. Used by
    /// <c>session.Store(doc, ignoreConcurrencyCheck: true)</c>.
    /// </summary>
    public string OverwriteSql { get; }

    /// <summary>
    /// When <c>true</c>, the document table is conjoined-multi-tenanted:
    /// <c>tenant_id</c> is part of the primary key, INSERT carries it as
    /// the first parameter, UPDATE has <c>and tenant_id = ?</c> appended
    /// to the WHERE clause, and ON CONFLICT references <c>(tenant_id, id)</c>.
    /// The operations bind the tenant id directly from the
    /// <c>tenant</c> argument the storage class receives — same source
    /// the codegen path uses today.
    /// </summary>
    public bool IsConjoined { get; }

    /// <summary>
    /// W3 spike (M7): which concurrency model the mapping uses. <c>Off</c>
    /// retains pre-M7 behavior; <c>Optimistic</c> turns on Guid-version
    /// WHERE filters + version writeback. <see cref="UpsertSql"/> /
    /// <see cref="UpdateSql"/> are already baked for the selected mode;
    /// operation classes only read this property to decide their
    /// postprocess branch and what extra parameter to bind.
    /// </summary>
    public ConcurrencyMode ConcurrencyMode { get; }

    /// <summary>
    /// W3 spike (M7): the version binder, present whenever
    /// <see cref="ConcurrencyMode"/> is non-<c>Off</c> or the mapping has
    /// a <c>[Version]</c>-annotated member. Operations use it from
    /// <c>Postprocess</c> to write the new version back onto the document
    /// without needing to re-walk <see cref="ReadBinders"/>.
    /// </summary>
    internal DocumentVersionBinder<TDoc>? VersionBinder { get; }

    /// <summary>
    /// W3 spike (M8): the bigint revision binder, present when
    /// <see cref="ConcurrencyMode"/> is <see cref="ConcurrencyMode.Numeric"/>
    /// or the mapping has a <c>[Version]</c>-annotated long member.
    /// Operations use it from <c>Postprocess</c> to write the new
    /// revision back onto the document.
    /// </summary>
    internal DocumentRevisionBinder<TDoc>? RevisionBinder { get; }

    /// <summary>
    /// W3 spike (M7+M8): zero-based index of the <c>mt_version</c> binder
    /// inside <see cref="ReadBinders"/>, or <c>-1</c> when version isn't
    /// in the read set. Each selector adds its own first-metadata-column
    /// offset (1 for QueryOnly, 2 for the rest) to get the actual
    /// reader column ordinal.
    /// </summary>
    public int VersionReadIndex { get; }

    /// <summary>
    /// W3 spike (M11): the root <see cref="Marten.Schema.DocumentMapping"/>
    /// when the mapping is hierarchical, otherwise <c>null</c>. Selectors
    /// use it to resolve the <c>mt_doc_type</c> alias back to a .NET
    /// <see cref="Type"/> for polymorphic deserialization.
    /// </summary>
    internal Marten.Schema.DocumentMapping? HierarchyMapping { get; }

    /// <summary>
    /// W3 spike (M11): zero-based index of the <c>mt_doc_type</c> binder
    /// inside <see cref="ReadBinders"/>, or <c>-1</c> when the mapping
    /// isn't hierarchical. Selectors translate it to a column ordinal
    /// the same way as <see cref="VersionReadIndex"/>.
    /// </summary>
    public int DocTypeReadIndex { get; }
}
