using System.Collections.Generic;
using System.IO;
using System.Linq;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using Marten.Events.Schema;
using Marten.Storage;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Functions;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Archiving;

/// <summary>
///     <c>mt_archive_stream</c>, or its reverse <c>mt_unarchive_stream</c> (marten#5476). Both directions are
///     built here so the column lists, the tag-table statements and the tenant predicates cannot drift apart:
///     un-archiving is archiving with the two values of <c>is_archived</c> swapped, plus one refusal the
///     archive side never needs.
/// </summary>
internal class ArchiveStreamFunction: Function
{
    internal const string Name = "mt_archive_stream";

    internal const string UnArchiveName = "mt_unarchive_stream";

    /// <summary>
    ///     SQLSTATE raised by <c>mt_unarchive_stream</c> under <c>UseArchivedStreamPartitioning</c> when the
    ///     stream's identity has been reused by a live stream since it was archived. Translated by
    ///     <see cref="ArchiveStreamOperation" />.
    /// </summary>
    internal const string LiveStreamCollisionSqlState = "MT004";

    private readonly EventGraph _events;
    private readonly bool _unarchive;

    public ArchiveStreamFunction(EventGraph events): this(events, false)
    {
    }

    public ArchiveStreamFunction(EventGraph events, bool unarchive)
        : base(new PostgresqlObjectName(events.DatabaseSchemaName, unarchive ? UnArchiveName : Name))
    {
        _events = events;
        _unarchive = unarchive;
    }

    public override void WriteCreateStatement(Migrator rules, TextWriter writer)
    {
        var argList = _events.StreamIdentity == StreamIdentity.AsGuid
            ? "streamid uuid"
            : "streamid varchar";

        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            argList += ", tenantid varchar";
        }

        var tenantWhere = _events.TenancyStyle == TenancyStyle.Conjoined ? " and tenant_id = tenantid" : "";

        if (_events.UseArchivedStreamPartitioning)
        {
            if (_unarchive)
            {
                writeUnArchiveWithPartitioning(writer, argList, tenantWhere);
            }
            else
            {
                writeWithPartitioning(writer, argList, tenantWhere);
            }
        }
        else
        {
            writeSimple(writer, argList, tenantWhere);
        }
    }

    // #4619: emit an EXPLICIT column list on both the INSERT target AND the
    // SELECT source. The previous implementation relied on positional
    // ordering — the SELECT enumerated columns from the in-memory
    // EventsTable/StreamsTable model and the INSERT had no target column
    // list. That works on a freshly-created table where the model order
    // matches the physical order, but breaks the moment ALTER TABLE ADD
    // COLUMN appends a new column at the end of an existing table (PG
    // can't reposition columns), shifting the positional alignment by one.
    // The #4515 bdata column added at model position 3 reproduces this on
    // any upgrade: physical layout is `... data, type, timestamp, ...,
    // bdata` while the model + SELECT walks `... data, bdata, type,
    // timestamp, ...`, so the timestamptz `timestamp` column receives the
    // varchar `type` value → 42804. Naming the columns on both sides
    // makes the function robust to physical-order drift forever.
    private (string insertColumns, string selectColumns) columnLists(Table table)
    {
        var names = table.Columns
            .Where(x => x.Name != IsArchivedColumn.ColumnName)
            .Select(x => x.Name)
            .ToList();

        return (names.Append(IsArchivedColumn.ColumnName).Join(", "), names.Join(", "));
    }

    private void writeWithPartitioning(TextWriter writer, string argList, string tenantWhere)
    {
        var (eventInsertColumns, eventColumnList) = columnLists(new EventsTable(_events));
        var (streamInsertColumns, streamColumnList) = columnLists(new StreamsTable(_events));

        // Serialize archives of the same active stream before copying it. Filtering alone lets
        // concurrent calls select the same row and collide in the archived partition.
        //
        // #5675: the tag rows move with their events, and the ORDER of these statements is the whole fix.
        // Each mt_event_tag_* table is itself partitioned by is_archived and carries a composite foreign
        // key (seq_id, is_archived) to mt_events, which is NOT deferrable -- so every statement here has to
        // leave both sides of that key consistent on its own:
        //
        //   1. the archived event rows are inserted FIRST, so (seq_id, TRUE) exists to point at;
        //   2. then the archived tag rows, which reference it;
        //   3. then the original tag rows are deleted, BEFORE the events they still reference;
        //   4. and only then the original event rows.
        //
        // Before this, step 4 ran with the tag rows still pointing at (seq_id, FALSE), so archiving a
        // tagged stream came back as a foreign key violation on the delete.
        var archiveTags = tagCopies(fromArchived: false);
        var deleteTags = tagDeletes(archived: false);

        // marten#5476: the natural key lookup rows ride along too. Under this partitioning each
        // mt_natural_key_X table is partitioned by is_archived and carries an ON DELETE CASCADE foreign key
        // (stream, is_archived) to mt_streams, so deleting the live stream row below used to cascade the
        // lookup row away -- the key was destroyed rather than archived, and UnArchiveStream would have had
        // nothing to bring back. Copied into the archived partition first, it is deleted with the live
        // stream row as before and the archived copy survives for mt_unarchive_stream to move back.
        var archiveNaturalKeys = naturalKeyCopies(fromArchived: false);

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_events.DatabaseSchemaName}.{Name}({argList}) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
  perform 1 from {_events.DatabaseSchemaName}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere} for update;
  insert into {_events.DatabaseSchemaName}.mt_streams ({streamInsertColumns}) select {streamColumnList}, TRUE from {_events.DatabaseSchemaName}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};
  insert into {_events.DatabaseSchemaName}.mt_events ({eventInsertColumns}) select {eventColumnList}, TRUE from {_events.DatabaseSchemaName}.mt_events where stream_id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};{archiveNaturalKeys}{archiveTags}{deleteTags}
  delete from {_events.DatabaseSchemaName}.mt_events where stream_id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};
  delete from {_events.DatabaseSchemaName}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};
END;
$function$;
");
    }

    /// <summary>
    ///     marten#5476: the mirror image of <see cref="writeWithPartitioning" />. Every row moves from the
    ///     archived partition back to the default one, in the same foreign-key-safe order: the live stream and
    ///     event rows are inserted first, then the natural key and tag rows that point at them, then the
    ///     archived tag rows are deleted ahead of the archived events, and the archived stream row last (which
    ///     cascades its archived natural key rows away).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A stream with no archived row is left alone, which makes un-archiving a live stream, an
    ///         already un-archived stream or a stream that never existed a no-op, as it is on the
    ///         unpartitioned path.
    ///     </para>
    ///     <para>
    ///         Unlike the unpartitioned path, this layout lets a stream's identity be reused once it is
    ///         archived (see <c>start_stream_with_id_of_previously_archived_stream</c>). When that has
    ///         happened the archived stream cannot come back without two live streams sharing one identity
    ///         and colliding on every version number, so the function refuses with
    ///         <see cref="LiveStreamCollisionSqlState" /> before it writes anything.
    ///     </para>
    /// </remarks>
    private void writeUnArchiveWithPartitioning(TextWriter writer, string argList, string tenantWhere)
    {
        var (eventInsertColumns, eventColumnList) = columnLists(new EventsTable(_events));
        var (streamInsertColumns, streamColumnList) = columnLists(new StreamsTable(_events));

        var restoreNaturalKeys = naturalKeyCopies(fromArchived: true);
        var restoreTags = tagCopies(fromArchived: true);
        var deleteArchivedTags = tagDeletes(archived: true);

        var schema = _events.DatabaseSchemaName;

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {schema}.{UnArchiveName}({argList}) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
  perform 1 from {schema}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = TRUE {tenantWhere} for update;
  if not found then
    return;
  end if;
  if exists (select 1 from {schema}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere}) then
    raise exception 'Cannot un-archive stream ''%'' because a live stream with the same identity was started after it was archived.', streamid using errcode = '{LiveStreamCollisionSqlState}';
  end if;
  insert into {schema}.mt_streams ({streamInsertColumns}) select {streamColumnList}, FALSE from {schema}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = TRUE {tenantWhere};
  insert into {schema}.mt_events ({eventInsertColumns}) select {eventColumnList}, FALSE from {schema}.mt_events where stream_id = streamid and {IsArchivedColumn.ColumnName} = TRUE {tenantWhere};{restoreNaturalKeys}{restoreTags}{deleteArchivedTags}
  delete from {schema}.mt_events where stream_id = streamid and {IsArchivedColumn.ColumnName} = TRUE {tenantWhere};
  delete from {schema}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = TRUE {tenantWhere};
END;
$function$;
");
    }

    /// <summary>
    ///     The statements that copy each tag table's rows of this stream out of the partition
    ///     <paramref name="fromArchived" /> and into the other one. Empty when there are no tag tables to
    ///     move — no registered tag types, or HStore mode, where tags live inline on <c>mt_events.tags</c>
    ///     and travel with the row.
    /// </summary>
    /// <remarks>
    ///     The column list is explicit for the same reason #4619 made the event and stream inserts explicit:
    ///     an ALTER TABLE ADD COLUMN on an existing tag table appends the column physically, and a
    ///     positional insert would then silently load the wrong column.
    /// </remarks>
    private string tagCopies(bool fromArchived)
    {
        var isConjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var columns = isConjoined
            ? $"value, tenant_id, seq_id, {IsArchivedColumn.ColumnName}"
            : $"value, seq_id, {IsArchivedColumn.ColumnName}";
        var source = isConjoined ? "t.value, t.tenant_id, t.seq_id" : "t.value, t.seq_id";

        return tagStatements(fromArchived, (table, where) =>
            $"  insert into {table} ({columns}) select {source}, {literal(!fromArchived)} from {table} t where {where};");
    }

    /// <summary>
    ///     The statements that delete this stream's tag rows from the partition <paramref name="archived" />.
    /// </summary>
    private string tagDeletes(bool archived)
    {
        return tagStatements(archived, (table, where) => $"  delete from {table} t where {where};");
    }

    private string tagStatements(bool archived, System.Func<string, string, string> statement)
    {
        if (_events.DcbStorageMode == DcbStorageMode.HStore) return "";

        var schema = _events.DatabaseSchemaName;
        var isConjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var writer = new StringWriter();

        foreach (var registration in _events.TagTypes)
        {
            var table = $"{schema}.mt_event_tag_{registration.TableSuffix}";

            // Aliased because both the tag table and mt_events carry tenant_id under conjoined tenancy,
            // and the subquery would otherwise resolve the outer predicate's column to the inner table.
            var tagTenantWhere = isConjoined ? " and t.tenant_id = tenantid" : "";
            var ofThisStream =
                $"t.seq_id in (select e.seq_id from {schema}.mt_events e where e.stream_id = streamid and e.{IsArchivedColumn.ColumnName} = {literal(archived)}{(isConjoined ? " and e.tenant_id = tenantid" : "")})";

            writer.WriteLine();
            writer.Write(statement(table,
                $"{ofThisStream} and t.{IsArchivedColumn.ColumnName} = {literal(archived)}{tagTenantWhere}"));
        }

        return writer.ToString();
    }

    /// <summary>
    ///     The statements that copy this stream's natural key lookup rows out of the partition
    ///     <paramref name="fromArchived" /> and into the other one. The originals are not deleted here: they
    ///     go with the stream row they reference, through that foreign key's ON DELETE CASCADE.
    /// </summary>
    /// <remarks>
    ///     <c>on conflict do nothing</c> because a key value can already be taken on the other side. Going into
    ///     the archive, another archived stream may already hold the same value; coming out, a live stream may
    ///     have claimed the key while this one was archived, which the natural key projection allows for an
    ///     archived holder. Either way the row already there keeps the key, the same outcome as the
    ///     unpartitioned layout, where a new claimant repoints the single lookup row.
    /// </remarks>
    private string naturalKeyCopies(bool fromArchived)
    {
        var definitions = naturalKeyDefinitions().ToList();
        if (definitions.Count == 0) return "";

        var isConjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var streamColumn = _events.StreamIdentity == StreamIdentity.AsGuid ? "stream_id" : "stream_key";
        var columns = isConjoined
            ? $"natural_key_value, {streamColumn}, tenant_id"
            : $"natural_key_value, {streamColumn}";
        var tenantWhere = isConjoined ? " and tenant_id = tenantid" : "";

        var writer = new StringWriter();
        foreach (var definition in definitions)
        {
            var table = NaturalKeyTable.TableNameFor(_events, definition).QualifiedName;

            writer.WriteLine();
            writer.Write(
                $"  insert into {table} ({columns}, {IsArchivedColumn.ColumnName}) select {columns}, {literal(!fromArchived)} from {table} where {streamColumn} = streamid and {IsArchivedColumn.ColumnName} = {literal(fromArchived)}{tenantWhere} on conflict do nothing;");
        }

        return writer.ToString();
    }

    private IEnumerable<NaturalKeyDefinition> naturalKeyDefinitions()
    {
        // The same enumeration EventGraph.FeatureSchema uses to create the tables, deduplicated by table
        // name so two projections of one aggregate type do not emit the same copy twice.
        return _events.Options.Projections.All
            .OfType<IAggregateProjection>()
            .Where(x => x.NaturalKeyDefinition != null)
            .Select(x => x.NaturalKeyDefinition!)
            .GroupBy(x => NaturalKeyTable.TableNameFor(_events, x).QualifiedName)
            .Select(x => x.First());
    }

    private static string literal(bool value) => value ? "TRUE" : "FALSE";

    private void writeSimple(TextWriter writer, string argList, string tenantWhere)
    {
        if (_unarchive)
        {
            // marten#5476: only rows still flagged are touched, so un-archiving a live stream, or one that does
            // not exist, writes nothing. The natural key lookup rows need nothing here: in this layout they
            // are never moved or flagged by archiving, and FetchNaturalKeyPlan reads archival off mt_streams.
            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_events.DatabaseSchemaName}.{UnArchiveName}({argList}) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
  update {_events.DatabaseSchemaName}.mt_streams set {IsArchivedColumn.ColumnName} = FALSE where id = streamid and {IsArchivedColumn.ColumnName} = TRUE {tenantWhere};
  update {_events.DatabaseSchemaName}.mt_events set is_archived = FALSE where stream_id = streamid and {IsArchivedColumn.ColumnName} = TRUE {tenantWhere};
END;
$function$;
");
            return;
        }

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_events.DatabaseSchemaName}.{Name}({argList}) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
  update {_events.DatabaseSchemaName}.mt_streams set {IsArchivedColumn.ColumnName} = TRUE where id = streamid {tenantWhere};
  update {_events.DatabaseSchemaName}.mt_events set is_archived = TRUE where stream_id = streamid {tenantWhere};
END;
$function$;
");
    }
}
