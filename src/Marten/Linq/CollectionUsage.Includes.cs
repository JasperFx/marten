#nullable enable
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
            // 9.0 (#4308): GenericFactoryCache caches the closed type +
            // delegate-compiled ctor, so per-query Include parsing skips
            // both MakeGenericType and Activator.CreateInstance after
            // the first occurrence of each (includedType, idType) shape.
            if (receiver.GetType().Closes(typeof(IList<>)))
            {
                var includedType = genericArguments[0];
                var storage = session.StorageFor(includedType);

                var plan = GenericFactoryCache.Build<IIncludePlan>(
                    typeof(ListIncludePlan<>), includedType, storage, member, receiver);

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

                var plan = GenericFactoryCache.Build<IIncludePlan>(
                    typeof(IncludePlan<>), includedType, storage, member, receiver);

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

                var plan = GenericFactoryCache.Build<IIncludePlan>(
                    typeof(DictionaryIncludePlan<,>), includedType, idType, storage, member, receiver);

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
