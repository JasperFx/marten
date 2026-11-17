#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Marten.Linq;

/// <summary>
///     #5861: builds the <see cref="NotSupportedException" /> that a Marten queryable throws when a LINQ operator
///     would execute it synchronously, naming the asynchronous operator to use instead.
/// </summary>
/// <remarks>
///     <para>
///         There are exactly two places a synchronous LINQ operator reaches Marten: an aggregating or
///         single-result operator (<c>First()</c>, <c>Count()</c>, <c>Any()</c>, <c>Sum()</c>...) calls
///         <see cref="IQueryProvider.Execute{TResult}" /> with a call to the matching <see cref="Queryable" />
///         method on top of the expression, and every operator that materializes the whole result
///         (<c>ToList()</c>, <c>ToArray()</c>, <c>ToDictionary()</c>, <c>foreach</c>...) calls
///         <see cref="IEnumerable{T}.GetEnumerator" />. The first carries the operator's name; the second
///         does not, so it names <c>ToListAsync()</c>, which is the replacement for every one of them.
///     </para>
///     <para>
///         Composing a query never reaches either, so building expressions -- sub-queries inside a predicate,
///         compiled queries, <c>ToCommand()</c> and <c>ToSql()</c> -- is unaffected.
///     </para>
/// </remarks>
internal static class SynchronousLinqRefusal
{
    internal const string Preamble = "Synchronous data access is not supported in Marten 10";

    internal const string EnumerationMessage =
        Preamble + ": enumerating a Marten query (foreach, ToList(), ToArray(), ToDictionary(), ToHashSet() and the " +
        "like) would query the database synchronously. Use await ToListAsync() instead, or " +
        "await foreach over ToAsyncEnumerable().";

    private static readonly Dictionary<string, string> _replacements = new()
    {
        [nameof(Queryable.First)] = "await FirstAsync()",
        [nameof(Queryable.FirstOrDefault)] = "await FirstOrDefaultAsync()",
        [nameof(Queryable.Single)] = "await SingleAsync()",
        [nameof(Queryable.SingleOrDefault)] = "await SingleOrDefaultAsync()",
        [nameof(Queryable.Count)] = "await CountAsync()",
        [nameof(Queryable.LongCount)] = "await LongCountAsync()",
        [nameof(Queryable.Any)] = "await AnyAsync()",
        [nameof(Queryable.Sum)] = "await SumAsync()",
        [nameof(Queryable.Min)] = "await MinAsync()",
        [nameof(Queryable.Max)] = "await MaxAsync()",
        [nameof(Queryable.Average)] = "await AverageAsync()",
        [nameof(Queryable.All)] = "await AnyAsync() with the predicate negated",
        [nameof(Queryable.Contains)] = "await AnyAsync() with an equality predicate",
        [nameof(Queryable.Last)] = "await FirstAsync() with the ordering reversed",
        [nameof(Queryable.LastOrDefault)] = "await FirstOrDefaultAsync() with the ordering reversed",
        [nameof(Queryable.ElementAt)] = "Skip(index) and then await FirstAsync()",
        [nameof(Queryable.ElementAtOrDefault)] = "Skip(index) and then await FirstOrDefaultAsync()"
    };

    /// <summary>
    ///     The exception for an operator that reached <see cref="IQueryProvider.Execute{TResult}" />.
    /// </summary>
    public static NotSupportedException ForExecution(Expression expression)
    {
        return new NotSupportedException(MessageForExecution(expression));
    }

    /// <summary>
    ///     The exception for synchronous enumeration of a Marten queryable.
    /// </summary>
    public static NotSupportedException ForEnumeration()
    {
        return new NotSupportedException(EnumerationMessage);
    }

    internal static string MessageForExecution(Expression expression)
    {
        var operatorName = expression is MethodCallExpression call ? call.Method.Name : null;

        if (operatorName == null)
        {
            return Preamble + ": this LINQ operator would query the database synchronously. " + GenericAdvice;
        }

        return _replacements.TryGetValue(operatorName, out var replacement)
            ? $"{Preamble}: {operatorName}() would query the database synchronously. Use {replacement} instead."
            : $"{Preamble}: {operatorName}() would query the database synchronously. {GenericAdvice}";
    }

    private const string GenericAdvice =
        "Use the asynchronous operator instead: ToListAsync(), FirstAsync(), FirstOrDefaultAsync(), SingleAsync(), " +
        "CountAsync(), AnyAsync(), SumAsync(), MinAsync(), MaxAsync() or AverageAsync().";
}
