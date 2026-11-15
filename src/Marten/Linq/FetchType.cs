#nullable enable
namespace Marten.Linq;

/// <summary>
///     In basic terms, how is the IQueryable going to be executed?
/// </summary>
public enum FetchType
{
    /// <summary>
    ///     First/FirstOrDefault/Single/SingleOrDefault. A preview with <c>ToCommand()</c> or <c>ExplainAsync()</c>
    ///     shows what First() and FirstOrDefault() send; Single() and SingleOrDefault() read up to two rows instead
    ///     of one, so that they can tell a second match apart.
    /// </summary>
    FetchOne,

    /// <summary>
    ///     Any execution that returns an IEnumerable (ToArray()/ToList()/etc.)
    /// </summary>
    FetchMany,

    /// <summary>
    ///     Using IQueryable.Count()
    /// </summary>
    Count,

    /// <summary>
    ///     Using IQueryable.Any()
    /// </summary>
    Any
}
