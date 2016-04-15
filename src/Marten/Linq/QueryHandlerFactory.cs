using System;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Remotion.Linq;

namespace Marten.Linq
{
    public static class QueryHandlerFactory
    {

        //private static void strategy<T>()

        public static IQueryHandler<T> BuildHandler<T>(this IDocumentSchema schema, QueryModel query)
        {
            throw new NotImplementedException();
        }         
    }
}