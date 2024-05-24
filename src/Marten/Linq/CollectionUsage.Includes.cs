using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.Includes;
using Marten.Linq.Members;
using Marten.Linq.Parsing;

namespace Marten.Linq;

public partial class CollectionUsage
{
    public List<MethodCallExpression> IncludeExpressions { get; } = new();
    public List<IIncludePlan> Includes { get; } = new();

    private bool _hasParsedIncludes = false;

    public void ParseIncludes(IQueryableMemberCollection collection, IMartenSession session)
    {
        if (_hasParsedIncludes) return;

        foreach (var expression in IncludeExpressions)
        {
            var startingIndex = expression.Arguments[0] is UnaryExpression ? 0 : 1;

            var member = collection.MemberFor(expression.Arguments[startingIndex]);

            var receiver = expression.Arguments[startingIndex + 1].Value();

            var genericArguments = receiver.GetType().GetGenericArguments();
            if (receiver.GetType().Closes(typeof(IList<>)))
            {
                var includedType = genericArguments[0];
                var storage = session.StorageFor(includedType);

                var type = typeof(ListIncludePlan<>).MakeGenericType(includedType);
                var plan = (IIncludePlan)Activator.CreateInstance(type, storage, member, receiver);

                if (expression.Arguments.Count == 3)
                {
                    plan.Where = expression.Arguments[2];
                }

                Includes.Add(plan);
            }
            else if (receiver.GetType().Closes(typeof(Action<>)))
            {
                var includedType = genericArguments[0];
                var storage = session.StorageFor(includedType);

                var type = typeof(IncludePlan<>).MakeGenericType(includedType);
                var plan = (IIncludePlan)Activator.CreateInstance(type, storage, member, receiver);

                if (expression.Arguments.Count == 3)
                {
                    plan.Where = expression.Arguments[2];
                }

                Includes.Add(plan);
            }
            else
            {
                var idType = genericArguments[0];
                var includedType = genericArguments[1];
                var storage = session.StorageFor(includedType);

                var type = typeof(DictionaryIncludePlan<,>).MakeGenericType(includedType, idType);
                var plan = (IIncludePlan)Activator.CreateInstance(type, storage, member, receiver);

                if (expression.Arguments.Count == 3)
                {
                    plan.Where = expression.Arguments[2];
                }

                Includes.Add(plan);
            }
        }

        _hasParsedIncludes = true;
    }
}
