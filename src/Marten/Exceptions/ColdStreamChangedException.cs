#nullable enable
using System.Collections.Generic;

namespace Marten.Exceptions;

/// <summary>
///     A unit of work appended to a stream whose cold version (<c>mt_streams.cold_version</c>) changed after
///     this session observed it, through <c>FetchForWriting</c>, <c>FetchLatest</c> or
///     <c>ColdVersionOfAsync</c>. Nothing was written; retry the unit of work from the fetch.
/// </summary>
public class ColdStreamChangedException: MartenException
{
    internal ColdStreamChangedException(string tenantId, IReadOnlyList<string> streams): base(
        $"The cold part of stream(s) {string.Join(", ", streams)} for tenant '{tenantId}' changed after this session read it. Retry the unit of work.")
    {
        Streams = streams;
    }

    public IReadOnlyList<string> Streams { get; }
}
