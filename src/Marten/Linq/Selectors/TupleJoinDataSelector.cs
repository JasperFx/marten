#nullable enable
using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq.Selectors;

/// <summary>
///     #6137: reads a join row whose result is a <see cref="ValueTuple{T1,T2}" /> -- the .NET 11 tuple
///     <c>Join</c>, <c>LeftJoin</c> and <c>RightJoin</c> overloads, or a <c>(c, o) =&gt; (c, o)</c> result selector --
///     by building the tuple from its two sides, each read with the store's serializer as its own type.
/// </summary>
/// <remarks>
///     The row is the same <c>jsonb_build_object('item1', ..., 'item2', ...)</c> any other join projection renders.
///     Handing it to the serializer as a whole tuple works under Newtonsoft.Json, which reads fields, but
///     System.Text.Json ignores the <c>Item1</c> / <c>Item2</c> fields and hands back <c>default</c>. Reading each side
///     separately means a tuple never goes through the serializer, so how tuples inside documents serialize stays
///     the serializer's own business.
/// </remarks>
internal static class TupleJoinDataSelector
{
    public static bool Applies(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTuple<,>);

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Closes the selector over the ValueTuple<,> the query's own result type already is.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055",
        Justification = "Closes the selector over the ValueTuple<,> the query's own result type already is.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "The closed selector type has the one public constructor below.")]
    public static ISelector<T> For<T>(IStorageSerializer serializer)
    {
        var closed = typeof(TupleJoinDataSelector<,>).MakeGenericType(typeof(T).GetGenericArguments());
        return (ISelector<T>)Activator.CreateInstance(closed, serializer)!;
    }
}

internal sealed class TupleJoinDataSelector<T1, T2>: ISelector<ValueTuple<T1, T2>>
{
    private readonly IStorageSerializer _serializer;

    public TupleJoinDataSelector(IStorageSerializer serializer)
    {
        _serializer = serializer;
    }

    public ValueTuple<T1, T2> Resolve(DbDataReader reader)
    {
        return reader.IsDBNull(0) ? default : read(reader.GetFieldValue<string>(0));
    }

    public async Task<ValueTuple<T1, T2>> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            return default;
        }

        return read(await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false));
    }

    private ValueTuple<T1, T2> read(string json)
    {
        using var row = JsonDocument.Parse(json);
        return (item<T1>(row.RootElement, 1), item<T2>(row.RootElement, 2));
    }

    // The keys are the tuple constructor's parameter names, item1 and item2. A side that is SQL null -- the
    // unmatched side of a left or right join -- is null in the row, and default in the tuple.
    private TItem item<TItem>(JsonElement row, int position)
    {
        var name = "item" + position;
        foreach (var property in row.EnumerateObject())
        {
            if (!property.NameEquals(name) && !string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null) return default!;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(property.Value.GetRawText()));
            return _serializer.FromJson<TItem>(stream);
        }

        return default!;
    }
}
