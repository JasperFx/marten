using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Marten.Linq
{
    /// <summary>
    /// Used to express a query expression that when used will be cached by class type implementing this interface
    /// </summary>
    /// <typeparam name="TDoc">The document</typeparam>
    /// <typeparam name="TOut">The result type for a query</typeparam>
    // SAMPLE: ICompiledQuery
    public interface ICompiledQuery<TDoc, TOut>
    {
        Expression<Func<IMartenQueryable<TDoc>, TOut>> QueryIs();
    }

    // ENDSAMPLE

    /// <summary>
    /// A *temporary* marker interface that for now is necessary to express enumerable result sets
    /// Once the concept of a result transformer is introduced we can remove the need for this extra interface
    /// </summary>
    /// <typeparam name="TDoc">The document</typeparam>
    // SAMPLE: ICompiledListQuery-with-no-select
    public interface ICompiledListQuery<TDoc>: ICompiledListQuery<TDoc, TDoc>
    {
    }

    // ENDSAMPLE

    /// <summary>
    /// A temporary marker interface that for now is necessary to express enumerable result sets
    /// </summary>
    /// <typeparam name="TDoc">The document</typeparam>
    /// <typeparam name="TOut">The output type</typeparam>
    // SAMPLE: ICompiledListQuery-with-select
    public interface ICompiledListQuery<TDoc, TOut>: ICompiledQuery<TDoc, IEnumerable<TOut>>
    {
    }

    // ENDSAMPLE

    /// <summary>
    /// Used to express a query expression that when used will be cached by class type implementing this interface
    /// </summary>
    /// <typeparam name="TDoc">The document</typeparam>
    // SAMPLE: ICompiledQuery-for-single-doc
    public interface ICompiledQuery<TDoc>: ICompiledQuery<TDoc, TDoc>
    {
    }

    // ENDSAMPLE
}
