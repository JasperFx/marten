using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.Includes;
using Marten.Linq.Parsing;

namespace Marten.Internal.CompiledQueries;

internal class QueryCompiler
{
    internal static readonly IList<IParameterFinder> Finders = new List<IParameterFinder> { new EnumParameterFinder() };

    static QueryCompiler()
    {
        forType(count =>
        {
            var values = new string[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Guid.NewGuid().ToString();
            }

            return values;
        });

        forType(count =>
        {
            var values = new Guid[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Guid.NewGuid();
            }

            return values;
        });

        forType(count =>
        {
            var value = -100000;
            var values = new int[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = value--;
            }

            return values;
        });

        forType(count =>
        {
            var value = -200000L;
            var values = new long[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = value--;
            }

            return values;
        });

        forType(count =>
        {
            var value = -300000L;
            var values = new float[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = value--;
            }

            return values;
        });

        forType(count =>
        {
            var value = -300000L;
            var values = new decimal[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = value--;
            }

            return values;
        });

        forType(count =>
        {
            var value = new DateTime(1600, 1, 1);
            var values = new DateTime[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = value.AddDays(-1);
            }

            return values;
        });

        forType(count =>
        {
            var value = new DateTimeOffset(1600, 1, 1, 0, 0, 0, 0.Seconds());
            var values = new DateTimeOffset[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = value.AddDays(-1);
            }

            return values;
        });
    }

    private static void forType<T>(Func<int, T[]> uniqueValues)
    {
        var finder = new SimpleParameterFinder<T>(uniqueValues);
        Finders.Add(finder);
    }

    public static CompiledQueryPlan BuildQueryPlan(QuerySession session, Type queryType, StoreOptions storeOptions)
    {
        var querySignature = queryType.FindInterfaceThatCloses(typeof(ICompiledQuery<,>));
        if (querySignature == null)
        {
            throw new ArgumentOutOfRangeException(nameof(queryType),
                $"Cannot derive query type for {queryType.FullNameInCode()}");
        }

        var builder =
            typeof(CompiledQueryPlanBuilder<,>).CloseAndBuildAs<ICompiledQueryPlanBuilder>(
                querySignature.GetGenericArguments());

        return builder.BuildPlan(session, queryType, storeOptions);
    }

    public static CompiledQueryPlan BuildQueryPlan<TDoc, TOut>(QuerySession session, ICompiledQuery<TDoc, TOut> query)
    {
        eliminateStringNulls(query);

        var plan = new CompiledQueryPlan(query.GetType(), typeof(TOut)){TenantId = session.TenantId};

        assertValidityOfQueryType(plan, query.GetType(), session.Options);

        // This *could* throw
        var queryTemplate = plan.CreateQueryTemplate(query);

        var statistics = plan.GetStatisticsIfAny(query);
        var parser = BuildDatabaseCommand(session, queryTemplate, statistics, plan);

        plan.IncludePlans.AddRange(new List<IIncludePlan>());
        var handler = parser.BuildHandler<TOut>();
        if (handler is IIncludeQueryHandler<TOut> i)
        {
            handler = i.Inner;
        }

        plan.HandlerPrototype = handler;

        return plan;
    }

    internal static LinqQueryParser BuildDatabaseCommand<TDoc, TOut>(QuerySession session,
        ICompiledQuery<TDoc, TOut> queryTemplate,
        QueryStatistics statistics,
        CompiledQueryPlan queryPlan)
    {
        Expression expression = queryTemplate.QueryIs();
        if (expression is LambdaExpression lambda)
        {
            expression = lambda.Body;
        }

        var parser = new LinqQueryParser(
            new MartenLinqQueryProvider(session, typeof(TDoc)) { Statistics = statistics }, session,
            expression);

        var statements = parser.BuildStatements();
        var topStatement = statements.Top;
        topStatement.Apply(queryPlan);

        var filters = topStatement.AllFilters().OfType<ICompiledQueryAwareFilter>().ToArray();

        queryPlan.MatchParameters(session.Options, filters);

        return parser;
    }

    private static void eliminateStringNulls(object query)
    {
        var type = query.GetType();

        foreach (var propertyInfo in type.GetProperties().Where(x => x.CanWrite && x.PropertyType == typeof(string)))
        {
            var raw = propertyInfo.GetValue(query);
            if (raw == null)
            {
                propertyInfo.SetValue(query, string.Empty);
            }
        }

        foreach (var fieldInfo in type.GetFields().Where(x => x.FieldType == typeof(string)))
        {
            var raw = fieldInfo.GetValue(query);
            if (raw == null)
            {
                fieldInfo.SetValue(query, string.Empty);
            }
        }
    }


    private static void assertValidityOfQueryType(CompiledQueryPlan plan, Type type, StoreOptions options)
    {
        if (plan.InvalidMembers.Any())
        {
            // Remove any value types here!
            foreach (var member in plan.InvalidMembers.Where(x => !x.GetRawMemberType().IsNullable()).ToArray())
            {
                if (options.TryFindValueType(member.GetMemberType()) != null)
                {
                    plan.InvalidMembers.Remove(member);
                }
            }

            if (!plan.InvalidMembers.Any()) return;

            var members = plan.InvalidMembers.Select(x => $"{x.GetRawMemberType().NameInCode()} {x.Name}")
                .Join(", ");
            var message = $"Members {members} cannot be used as parameters to a compiled query";

            throw new InvalidCompiledQueryException(message);
        }
    }

    public class CompiledQueryPlanBuilder<TDoc, TOut>: ICompiledQueryPlanBuilder
    {
        public CompiledQueryPlan BuildPlan(QuerySession session, Type queryType, StoreOptions storeOptions)
        {
            object query;

            try
            {
                query = Activator.CreateInstance(queryType);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    $"Unable to create query type {queryType.FullNameInCode()}. If you receive this message, add a no-arg constructor that can be either public or non-public",
                    e);
            }

            return BuildQueryPlan(session, (ICompiledQuery<TDoc, TOut>)query);
        }
    }

    public interface ICompiledQueryPlanBuilder
    {
        CompiledQueryPlan BuildPlan(QuerySession session, Type queryType, StoreOptions storeOptions);
    }
}
