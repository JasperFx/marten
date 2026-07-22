#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;

namespace Marten.Linq.Selectors;

/// <summary>
/// Fallback selector for Select() projections that are not "simple" flat member
/// accesses (GH-5011) -- e.g. they contain method calls, arithmetic, casts, or
/// conditional expressions. The full source document is deserialized from the raw
/// "data" column and the original Select() lambda (compiled once here) is then applied
/// against it on the client, exactly as Marten did before the jsonb_build_object
/// optimization existed for simple projections. Because the transform runs in .NET,
/// results produced through this selector cannot be streamed as raw JSON.
/// </summary>
internal class LambdaTransformSelector<TSource, TResult>: ISelector<TResult> where TSource : notnull
{
    private readonly IStorageSerializer _serializer;
    private readonly Func<TSource, TResult> _transform;

    public LambdaTransformSelector(IStorageSerializer serializer, Func<TSource, TResult> transform)
    {
        _serializer = serializer;
        _transform = transform;
    }

    public TResult Resolve(DbDataReader reader)
    {
        var source = _serializer.FromJson<TSource>(reader, 0);
        return _transform(source);
    }

    public async Task<TResult> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var source = await _serializer.FromJsonAsync<TSource>(reader, 0, token).ConfigureAwait(false);
        return _transform(source);
    }
}
