#nullable enable
using System.Collections.Generic;

namespace Marten.Events.Archiving.ObjectStorage;

/// <summary>
///     A column of <c>mt_events</c>, as the object storage provider has to reproduce it.
/// </summary>
public sealed record ArchivedEventColumn(string Name, string PostgresType);

/// <summary>
///     Where archived events go once they leave <c>mt_events</c>, and how PostgreSQL reads them back.
///     Marten only ever reads them through the view this provider defines, and only for a query that
///     asks for archived events; every other event query keeps reading <c>mt_events</c> directly.
/// </summary>
public interface IArchivedEventObjectStorage
{
    /// <summary>
    ///     Statements run before the view is created, e.g. writing an empty file so the view has
    ///     something to read before the first move.
    /// </summary>
    IEnumerable<string> InitializationSql(IReadOnlyList<ArchivedEventColumn> columns);

    /// <summary>
    ///     The body of a view with every column of <c>mt_events</c>: the rows still in
    ///     <paramref name="eventsTable" /> plus the rows already moved to object storage.
    /// </summary>
    string ArchiveInclusiveViewSql(string eventsTable, IReadOnlyList<ArchivedEventColumn> columns);

    /// <summary>
    ///     A statement that writes the rows of <paramref name="selectSql" /> to a new object named after
    ///     <paramref name="batchKey" />. Runs inside the transaction that then deletes those rows.
    /// </summary>
    string ExportSql(string selectSql, string batchKey);
}
