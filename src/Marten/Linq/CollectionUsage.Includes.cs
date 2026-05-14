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
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq;

[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Class-level: parameter receives a DAM-annotated Type from a reflective lookup whose source type is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2072",
    Justification = "Class-level: assigns the result of a reflective Type/MethodInfo lookup into a DAM-annotated target. Source types are preserved at the registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
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
            // 9.0 (#4308): replace per-call MakeGenericType + Activator.CreateInstance
            // with delegate-cached factories via GenericFactoryCache. The first call
            // for a given (open type, type args) pair builds + caches an
            // Activator-backed factory; subsequent calls reuse the cached delegate
            // and skip the MakeGenericType cost.
            if (receiver.GetType().Closes(typeof(IList<>)))
            {
                var includedType = genericArguments[0];
                var storage = session.StorageFor(includedType);

                var plan = (IIncludePlan)GenericFactoryCache.BuildAs<object>(
                    typeof(ListIncludePlan<>),
                    includedType,
                    storage,
                    member,
                    receiver,
                    static closed => (a, b, c) => Activator.CreateInstance(closed, a, b, c)!);

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

                var plan = (IIncludePlan)GenericFactoryCache.BuildAs<object>(
                    typeof(IncludePlan<>),
                    includedType,
                    storage,
                    member,
                    receiver,
                    static closed => (a, b, c) => Activator.CreateInstance(closed, a, b, c)!);

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

                var plan = (IIncludePlan)GenericFactoryCache.BuildAs<object>(
                    typeof(DictionaryIncludePlan<,>),
                    includedType,
                    idType,
                    storage,
                    member,
                    receiver,
                    static closed => (a, b, c) => Activator.CreateInstance(closed, a, b, c)!);

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
