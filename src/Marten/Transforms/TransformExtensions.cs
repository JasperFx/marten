using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Transforms
{
    public static class TransformExtensions
    {
        /// <summary>
        ///     Placeholder for Linq expressions
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="doc"></param>
        /// <param name="transformName"></param>
        /// <returns></returns>
        public static string TransformToJson<T>(this T doc, string transformName)
        {
            return "";
        }


        public static IQueryable<string> TransformToJson<T>(this IQueryable<T> queryable, string transformName)
        {
            return queryable.Select(x => x.TransformToJson(transformName));
        }

        public static IQueryable<T> TransformTo<T>(this IQueryable queryable, string transformName)
        {
            return queryable.As<IMartenQueryable>().TransformTo<T>(transformName);
        }

        public static TDoc TransformTo<T, TDoc>(this T doc, string transformName)
        {
            return default(TDoc);
        }
    }

    
    public class TransformToJsonNode : ResultOperatorExpressionNodeBase
    {
        public static MethodInfo[] SupportedMethods =
            typeof(TransformExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(x => x.Name == nameof(TransformExtensions.TransformToJson)).ToArray();

        private readonly TransformToJsonResultOperator _operator;


        public TransformToJsonNode(MethodCallExpressionParseInfo parseInfo, Expression transform, Expression optionalSelector) : base(parseInfo, transform as LambdaExpression, optionalSelector as LambdaExpression)
        {
            var name = transform.As<ConstantExpression>().Value.As<string>();
            _operator = new TransformToJsonResultOperator(name);
        }

        protected override ResultOperatorBase CreateResultOperator(ClauseGenerationContext clauseGenerationContext)
        {
            return _operator;
        }

        public override Expression Resolve(ParameterExpression inputParameter, Expression expressionToBeResolved,
            ClauseGenerationContext clauseGenerationContext)
        {
            return Source.Resolve(
                inputParameter,
                expressionToBeResolved,
                clauseGenerationContext);
        }
    }

    public class TransformToOtherTypeNode : ResultOperatorExpressionNodeBase
    {
        public static MethodInfo[] SupportedMethods =
            typeof(TransformExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(x => x.Name == nameof(TransformExtensions.TransformTo)).ToArray();

        private readonly TransformToOtherTypeOperator _operator;


        public TransformToOtherTypeNode(MethodCallExpressionParseInfo parseInfo, Expression transform, Expression optionalSelector) : base(parseInfo, transform as LambdaExpression, optionalSelector as LambdaExpression)
        {
            var name = transform.As<ConstantExpression>().Value.As<string>();
            _operator = new TransformToOtherTypeOperator(name);
        }

        protected override ResultOperatorBase CreateResultOperator(ClauseGenerationContext clauseGenerationContext)
        {
            return _operator;
        }

        public override Expression Resolve(ParameterExpression inputParameter, Expression expressionToBeResolved,
            ClauseGenerationContext clauseGenerationContext)
        {
            return Source.Resolve(
                inputParameter,
                expressionToBeResolved,
                clauseGenerationContext);
        }
    }

    public interface ISelectableOperator
    {
        ISelector<T> BuildSelector<T>(IDocumentSchema schema, IQueryableDocument document);
    }

    public class TransformToJsonResultOperator : SequenceTypePreservingResultOperatorBase, ISelectableOperator
    {
        private readonly string _transformName;

        public TransformToJsonResultOperator(string transformName)
        {
            _transformName = transformName;
        }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new TransformToJsonResultOperator(_transformName);
        }

        public override void TransformExpressions(Func<Expression, Expression> transformation)
        {
            // no-op;
        }

        public override StreamedSequence ExecuteInMemory<T>(StreamedSequence input)
        {
            return input;
        }

        public ISelector<T> BuildSelector<T>(IDocumentSchema schema, IQueryableDocument document)
        {
            var transform = schema.TransformFor(_transformName);
            return new TransformToJsonSelector(transform, document).As<ISelector<T>>();
        }
    }

    public class TransformToOtherTypeOperator : SequenceTypePreservingResultOperatorBase, ISelectableOperator
    {
        private readonly string _transformName;

        public TransformToOtherTypeOperator(string transformName)
        {
            _transformName = transformName;
        }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new TransformToJsonResultOperator(_transformName);
        }

        public override void TransformExpressions(Func<Expression, Expression> transformation)
        {
            // no-op;
        }

        public override StreamedSequence ExecuteInMemory<T>(StreamedSequence input)
        {
            return input;
        }

        public ISelector<T> BuildSelector<T>(IDocumentSchema schema, IQueryableDocument document)
        {
            var transform = schema.TransformFor(_transformName);

            return new TransformToTypeSelector<T>(transform, document);
        }
    }

}