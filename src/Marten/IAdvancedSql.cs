#nullable enable
using System.Collections.Generic;
using System.Threading;

namespace Marten;

public interface IAdvancedSql
{
    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns>An async enumerable iterating over the results</returns>
    IAsyncEnumerable<T> StreamAsync<T>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns>An async enumerable iterating over the list of result tuples</returns>
    IAsyncEnumerable<(T1, T2)> StreamAsync<T1, T2>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <typeparam name="T3"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns>An async enumerable iterating over the list of result tuples</returns>
    IAsyncEnumerable<(T1, T2, T3)> StreamAsync<T1, T2, T3>(string sql, CancellationToken token, params object[] parameters);
}
