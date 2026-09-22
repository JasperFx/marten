namespace Marten.Linq;

/// <summary>
///     #5481. Marten's generic LINQ refusals used to stop at "not supported", leaving no hint that
///     there is anything to do about it. Every generic refusal now closes with the same sentence,
///     naming the reference list of what IS supported and the three escape hatches, so the reader
///     does not have to already know they exist.
/// </summary>
internal static class LinqRefusals
{
    /// <summary>
    ///     Appended to the refusals that cannot name a specific alternative — an unknown LINQ
    ///     operator, or a method call with no registered parser.
    /// </summary>
    internal const string EscapeHatches =
        " Supported operators are documented at https://martendb.io/documents/querying/linq/operators.html; " +
        "for anything else use MatchesSql(), session.QueryAsync<T>(sql), or materialize with ToListAsync() and filter in memory.";
}
