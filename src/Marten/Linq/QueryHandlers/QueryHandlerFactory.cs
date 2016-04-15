using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services.Includes;
using Remotion.Linq;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.QueryHandlers
{
    public interface IQueryHandlerFactory
    {
        IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model);
        IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, bool returnDefaultWhenEmpty);
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

        public IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, bool returnDefaultWhenEmpty)
        {
            _schema.EnsureStorageExists(model.SourceType());

            if (model.HasOperator<FirstResultOperator>())
            {
                return returnDefaultWhenEmpty
                    ? OneResultHandler<T>.FirstOrDefault(_schema, model, joins)
                    : OneResultHandler<T>.First(_schema, model, joins);
            }

            if (model.HasOperator<SingleResultOperator>())
            {
                return returnDefaultWhenEmpty 
                    ? OneResultHandler<T>.SingleOrDefault(_schema, model, joins)
                    : OneResultHandler<T>.Single(_schema, model, joins);
            }

            if (model.HasOperator<MinResultOperator>())
            {
                return AggregateQueryHandler<T>.Min(_schema, model);
            }

            if (model.HasOperator<MaxResultOperator>())
            {
                return AggregateQueryHandler<T>.Max(_schema, model);
            }

            if (model.HasOperator<LastResultOperator>())
            {
                throw new InvalidOperationException(
                    "Marten does not support Last()/LastOrDefault(). Use reverse ordering and First()/FirstOrDefault() instead");
            }

            throw new NotSupportedException("Not yet supporting these results: " + model.AllResultOperators().Select(x => x.GetType().Name).Join(", "));
        } 
    }
}