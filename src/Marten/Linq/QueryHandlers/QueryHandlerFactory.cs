using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Remotion.Linq;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.QueryHandlers
{
    public interface IQueryHandlerFactory
    {
        IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model);
    }

    public class QueryHandlerFactory : IQueryHandlerFactory
    {
        private readonly IDocumentSchema _schema;

        public QueryHandlerFactory(IDocumentSchema schema)
        {
            _schema = schema;
        }

        public IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model)
        {
            _schema.EnsureStorageExists(model.SourceType());

            if (model.HasOperator<CountResultOperator>() || model.HasOperator<LongCountResultOperator>())
            {
                return new CountQueryHandler<T>(model, _schema);
            }

            if (model.HasOperator<SumResultOperator>())
            {
                return AggregateQueryHandler<T>.Sum(_schema, model);
            }

            if (model.HasOperator<AnyResultOperator>())
            {
                return new AnyQueryHandler(model, _schema).As<IQueryHandler<T>>();
            }

            throw new NotSupportedException("Not yet supporting these results: " + model.AllResultOperators().Select(x => x.GetType().Name).Join(", "));
        } 
    }
}