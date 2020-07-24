using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using LamarCodeGeneration;
using LamarCodeGeneration.Util;
using Marten.Internal;
using Marten.Internal.Linq.Includes;
using Marten.Internal.Storage;
using Marten.Linq.Fields;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Linq
{
    public class IncludeResultOperator
        : SequenceTypePreservingResultOperatorBase
    {
        public Expression ConnectingField { get; }
        public ConstantExpression IncludeExpression { get; }

        public IncludeResultOperator(Expression connectingField, ConstantExpression includeExpression)
        {
            ConnectingField = connectingField;
            IncludeExpression = includeExpression;
        }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new IncludeResultOperator(ConnectingField, IncludeExpression);
        }

        public override void TransformExpressions(
            Func<Expression, Expression> transformation)
        {
            throw new NotImplementedException();
        }

        public override StreamedSequence ExecuteInMemory<T>(StreamedSequence input)
        {
            return input;
        }

        public IIncludePlan BuildInclude(IMartenSession session, IFieldMapping sourceFields)
        {
            var value = IncludeExpression.Value;
            var connectingField = sourceFields.FieldFor(ConnectingField);

            var valueType = value.GetType();
            if (valueType.Closes(typeof(IDictionary<,>)))
            {
                // It's funky, but the generic arguments need to be reversed between the dictionary and the
                // builder here
                var builder = typeof(DictionaryIncludeBuilder<,>)
                    .CloseAndBuildAs<IIncludeBuilder>(valueType.GetGenericArguments().Reverse().ToArray());

                return builder.Build(session, connectingField, value);
            }
            else if (valueType.Closes(typeof(Action<>)) || valueType.Closes(typeof(IList<>)))
            {
                var builder = typeof(ActionIncludeBuilder<>)
                    .CloseAndBuildAs<IIncludeBuilder>(valueType.GetGenericArguments()[0]);

                return builder.Build(session, connectingField, value);
            }

            throw new ArgumentOutOfRangeException(nameof(valueType));
        }

        internal interface IIncludeBuilder
        {
            IIncludePlan Build(IMartenSession session, IField connectingField, object value);
        }

        internal class DictionaryIncludeBuilder<T, TId>: IIncludeBuilder
        {
            public IIncludePlan Build(IMartenSession session, IField connectingField, object value)
            {
                var storage = session.StorageFor<T>();
                if (storage is IDocumentStorage<T, TId> typed)
                {
                    var dict = value as Dictionary<TId, T>;
                    if (dict == null)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }

                    return new IncludePlan<T>(storage, connectingField, doc => dict[typed.Identity(doc)] = doc);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Id/Document type mismatch. The id type for the included document type {typeof(T).FullNameInCode()} is {storage.IdType.FullNameInCode()}");
                }
            }
        }

        internal class ActionIncludeBuilder<T>: IIncludeBuilder
        {
            public IIncludePlan Build(IMartenSession session, IField connectingField, object value)
            {
                var storage = session.StorageFor<T>();
                if (value is IList<T> list)
                {
                    return new IncludePlan<T>(storage, connectingField, list.Add);
                }
                else if (value is Action<T> action)
                {
                    return new IncludePlan<T>(storage, connectingField, action);
                }

                throw new InvalidOperationException();
            }
        }
    }
}
