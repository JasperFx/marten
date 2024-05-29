#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class EnumerableContains: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var member = expression.Object ?? expression.Arguments[0];

        return expression.Method.Name == LinqConstants.CONTAINS && matches(member.Type);
    }

    private static bool matches(Type memberType)
    {
        if (memberType.Closes(typeof(HashSet<>))) return false;

        if (memberType.IsEnumerable()) return true;
        if (memberType.IsGenericEnumerable()) return true;
        if (memberType.Closes(typeof(IDictionary<,>))) return true;

        return false;
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        if ((expression.Object ?? expression.Arguments[0]).TryToParseConstant(out var constant))
        {
            // This is the value.Contains() pattern
            var collectionMember = memberCollection.MemberFor(expression.Arguments.Last());

            return new IsOneOfFilter(collectionMember, new CommandParameter(constant.Value));
        }

        // Not sure why it did this: memberCollection as ICollectionMember ??
        var collection = memberCollection.MemberFor(expression.Object ?? expression.Arguments[0]) as ICollectionMember;

        if (collection == null)
        {
            throw new BadLinqExpressionException(
                $"Marten is not (yet) able to parse '{expression}' as part of a Contains() query for this member");
        }

        return collection.ParseWhereForContains(expression, options);
    }
}

internal class HashSetEnumerableContains: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var member = expression.Object ?? expression.Arguments[0];
        return expression.Method.Name == LinqConstants.CONTAINS && matches(member.Type);
    }

    private static bool matches(Type memberType)
    {
        if (!memberType.Closes(typeof(HashSet<>))) return false;

        if (memberType.IsEnumerable()) return true;
        if (memberType.IsGenericEnumerable()) return true;
        if (memberType.Closes(typeof(IDictionary<,>))) return true;

        return false;
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        if ((expression.Object ?? expression.Arguments[0]).TryToParseConstant(out var constant))
        {
            // This is the value.Contains() pattern
            var collectionMember = memberCollection.MemberFor(expression.Arguments.Last());

            return new WhereFragment($"{collectionMember.TypedLocator} = ANY(?)", correctToArray(constant.Value!));
        }

        var collection = memberCollection as ICollectionMember ??
                         memberCollection.MemberFor(expression.Object ?? expression.Arguments[0]) as ICollectionMember;

        if (collection == null)
        {
            throw new BadLinqExpressionException(
                $"Marten is not (yet) able to parse '{expression}' as part of a Contains() query for this member");
        }

        return collection.ParseWhereForContains(expression, options);
    }

    private object correctToArray(object constantValue)
    {
        var valueType = constantValue.GetType().GetGenericArguments()[0];
        var corrector = typeof(Corrector<>).CloseAndBuildAs<ICorrector>(valueType);
        return corrector.Correct(constantValue);
    }

    internal interface ICorrector
    {
        object Correct(object hashSet);
    }

    internal class Corrector<T>: ICorrector
    {
        public object Correct(object hashSet)
        {
            return hashSet.As<HashSet<T>>().ToArray();
        }
    }
}
