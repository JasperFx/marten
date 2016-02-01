using System;
using System.Linq.Expressions;

namespace Marten.Linq
{
    public static class CompiledQueryExtensions
    {
        public static ICompileQueryBody<TDoc, T1> CompileQuery<TDoc, T1>(this IDocumentStore store)
        {
            throw new NotSupportedException();
        }

        public static ICompileQueryBody<TDoc, T1, T2> CompileQuery<TDoc, T1, T2>(this IDocumentStore store)
        {
            throw new NotSupportedException();
        }

        public static ICompileQueryBody<TDoc, T1, T2, T3> CompileQuery<TDoc, T1, T2, T3>(this IDocumentStore store)
        {
            throw new NotSupportedException();
        }
    }


    public interface ICompileQueryBody<TDoc, T1>
    {
        ICompiledQuery<T1, TOut> For<TOut>(Expression<Func<IMartenQueryable<TDoc>, T1, TOut>>  expression);
    }

    public interface ICompileQueryBody<TDoc, T1, T2>
    {
        ICompiledQuery<T1, T2, TOut> For<TOut>(Expression<Func<IMartenQueryable<TDoc>, T1, T2, TOut>> expression);
    }

    public interface ICompileQueryBody<TDoc, T1, T2, T3>
    {
        ICompiledQuery<T1, T2, T3, TOut> For<TOut>(Expression<Func<IMartenQueryable<TDoc>, T1, T2, T3, TOut>> expression);
    }

    public interface ICompiledQuery<T1, TOut>
    {
        TOut Query(IQuerySession session, T1 input1);
    }

    public interface ICompiledQuery<T1, T2, TOut>
    {
        TOut Query(IQuerySession session, T1 input1, T2 input2);
    }

    public interface ICompiledQuery<T1, T2, T3, TOut>
    {
        TOut Query(IQuerySession session, T1 input1, T2 input2, T3 input3);
    }
}