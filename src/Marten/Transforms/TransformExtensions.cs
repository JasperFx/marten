using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Baseline;
using Marten.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;
using Remotion.Linq.Parsing.Structure.IntermediateModel;

namespace Marten.Transforms
{
    public static class TransformExtensions
    {
        /// <summary>
        /// Placeholder for Linq expressions
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

    /*
    public class TransformExpressionNode : ResultOperatorExpressionNodeBase
    {
        public static MethodInfo[] SupportedMethods =
            typeof(TransformExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static).ToArray();

        public TransformExpressionNode(MethodCallExpressionParseInfo parseInfo, LambdaExpression optionalPredicate, LambdaExpression optionalSelector) : base(parseInfo, optionalPredicate, optionalSelector)
        {
            Debug.WriteLine("Foo");
        }

        protected override ResultOperatorBase CreateResultOperator(ClauseGenerationContext clauseGenerationContext)
        {
            throw new System.NotImplementedException();
        }

        public override Expression Resolve(ParameterExpression inputParameter, Expression expressionToBeResolved,
            ClauseGenerationContext clauseGenerationContext)
        {
            throw new System.NotImplementedException();
        }
    }

    public class TransformToJsonResultOperator : SequenceTypePreservingResultOperatorBase
    {
        public Expression Parameter { get; set; }

        public TransformToJsonResultOperator(Expression parameter)
        {
            Parameter = parameter;
        }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new TransformToJsonResultOperator(Parameter);
        }

        public override void TransformExpressions(Func<Expression, Expression> transformation)
        {
            Parameter = transformation(Parameter);
        }

        public override StreamedSequence ExecuteInMemory<T>(StreamedSequence input)
        {
            return input;
        }
    }
    */
}