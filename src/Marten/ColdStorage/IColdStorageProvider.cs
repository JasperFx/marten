#nullable enable
using System.Collections.Generic;

namespace Marten.ColdStorage;

/// <summary>
///     A column of a Marten table, as the cold storage provider has to reproduce it.
/// </summary>
public sealed record ColdColumn(string Name, string PostgresType);

/// <summary>
///     Where rows go once they leave a Marten table (<c>mt_events</c> or a document table), and how
///     PostgreSQL reads them back. Marten reads cold rows only through views over these statements,
///     and only for a read that asks for them; every other read keeps its current SQL.
/// </summary>
public interface IColdStorageProvider
{
    /// <summary>
    ///     Statements run before the views are created, e.g. writing an empty file so the cold relation
    ///     can be read before the first move. <paramref name="dataset" /> names one Marten table.
    /// </summary>
    IEnumerable<string> InitializationSql(string dataset, IReadOnlyList<ColdColumn> columns);

    /// <summary>
    ///     A SELECT of every row moved out of <paramref name="dataset" />, with one output column per
    ///     entry of <paramref name="columns" />, typed as <see cref="ColdColumnType" />.
    /// </summary>
    string ColdRelationSql(string dataset, IReadOnlyList<ColdColumn> columns);

    /// <summary>
    ///     The type a column has on the cold side. The hot side is cast to it wherever the two are combined.
    /// </summary>
    string ColdColumnType(string postgresType);

    /// <summary>
    ///     A statement that writes the rows of <paramref name="selectSql" /> to a new object of
    ///     <paramref name="dataset" /> named after <paramref name="batchKey" />. Runs inside the
    ///     transaction that then deletes those rows from the hot table.
    /// </summary>
    string ExportSql(string dataset, string selectSql, string batchKey);
}
