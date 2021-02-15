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
    #region sample_ICompiledQuery
    public interface ICompiledQuery<TDoc, TOut>
    {
        Expression<Func<IMartenQueryable<TDoc>, TOut>> QueryIs();
    }

    #endregion sample_ICompiledQuery

    /// <summary>
    /// A *temporary* marker interface that for now is necessary to express enumerable result sets
    /// Once the concept of a result transformer is introduced we can remove the need for this extra interface
    /// </summary>
    /// <typeparam name="TDoc">The document</typeparam>
    #region sample_ICompiledListQuery-with-no-select
    public interface ICompiledListQuery<TDoc>: ICompiledListQuery<TDoc, TDoc>
    {
    }

    #endregion sample_ICompiledListQuery-with-no-select

    /// <summary>
    /// A temporary marker interface that for now is necessary to express enumerable result sets
    /// </summary>
    /// <typeparam name="TDoc">The document</typeparam>
    /// <typeparam name="TOut">The output type</typeparam>
    #region sample_ICompiledListQuery-with-select
    public interface ICompiledListQuery<TDoc, TOut>: ICompiledQuery<TDoc, IEnumerable<TOut>>
    {
    }

    #endregion sample_ICompiledListQuery-with-select

    /// <summary>
    /// Used to express a query expression that when used will be cached by class type implementing this interface
    /// </summary>
    /// <typeparam name="TDoc">The document</typeparam>
    #region sample_ICompiledQuery-for-single-doc
    public interface ICompiledQuery<TDoc>: ICompiledQuery<TDoc, TDoc>
    {
    }

    #endregion sample_ICompiledQuery-for-single-doc
}
