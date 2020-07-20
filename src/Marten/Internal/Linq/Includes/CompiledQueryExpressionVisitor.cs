using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Internal.CompiledQueries;
using Marten.Internal.Storage;
using Marten.Linq;

namespace Marten.Internal.Linq.Includes
{
    public class CompiledQueryExpressionVisitor<TSource> : ExpressionVisitor
    {
        private readonly IDocumentStorage<TSource> _source;
        private readonly IMartenSession _session;
        private readonly CompiledQueryPlan _plan;

        public CompiledQueryExpressionVisitor(IMartenSession session, CompiledQueryPlan plan)
        {
            _source = session.StorageFor<TSource>();
            _session = session;
            _plan = plan;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            switch (node.Method.Name)
            {
                case "Include":
                    var connectingMember = FindMembers.Determine(node.Arguments[0]).Single();
                    var connectingField = _source.Fields.FieldFor(connectingMember);
                    var includeType = node.Method.GetGenericArguments().First();

                    var planType = typeof(IncludePlan<>).MakeGenericType(includeType);

                    var includeStorage = _session.StorageFor(includeType);

                    var plan = (IIncludePlan)Activator.CreateInstance(planType,
                        new object[] {_plan.IncludePlans.Count, includeStorage, connectingField, null});

                    _plan.IncludePlans.Add(plan);

                    var member = FindMembers.Determine(node.Arguments.Last()).Last();
                    _plan.IncludeMembers.Add(member);


                    try
                    {
                        return base.VisitMethodCall(node);
                    }
                    catch (Exception)
                    {
                        // Probably don't care, just get out of here
                        return null;
                    }

                case "Where":
                case "First":
                case "FirstOrDefault":
                case "Single":
                case "SingleOrDefault":
                case "OrderBy":
                case "OrderByDescending":
                case "Count":
                case "Any":
                case "Select":
                case "SelectMany":
                case "ToJsonArray":
                    return Visit(node.Arguments[0]);

                default:
                    return base.VisitMethodCall(node);

            }



        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            try
            {
                return base.VisitUnary(node);
            }
            catch (Exception)
            {
                return null;
            }
        }


        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            return Visit(node.Body);
        }
    }
}
