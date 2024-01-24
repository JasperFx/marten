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
    /// <summary>
    /// A list of <see cref="IParameterFinder"/>
    /// </summary>
    internal static readonly IList<IParameterFinder> Finders = new List<IParameterFinder> { new EnumParameterFinder() };

    /// <summary>
    /// Query compiler static constructor that sets up unique value sources for
    /// </summary>
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

    /// <summary>
    /// Creates a new <see cref="SimpleParameterFinder{T}"/> using <paramref name="uniqueValueGenerator"/>
    /// as a unique value source and adds it to <see cref="Finders"/>
    /// </summary>
    /// <param name="uniqueValueGenerator">Function that generates unique values</param>
    /// <typeparam name="T">The type for the new <see cref="SimpleParameterFinder{T}"/> </typeparam>
    private static void forType<T>(Func<int, T[]> uniqueValueGenerator)
    {
        var finder = new SimpleParameterFinder<T>(uniqueValueGenerator);
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

    /// <summary>
    /// Try to build a <see cref="CompiledQueryPlan"/> for the <paramref name="query"/>
    /// </summary>
    /// <param name="session">The <see cref="QuerySession"/> to build the command for</param>
    /// <param name="query">The actual <see cref="ICompiledQuery{TDoc,TOut}"/> instance</param>
    /// <typeparam name="TDoc">The input type of <see cref="ICompiledQuery{TDoc,TOut}"/></typeparam>
    /// <typeparam name="TOut">The output type of <see cref="ICompiledQuery{TDoc,TOut}"/></typeparam>
    /// <returns>A compiled query plan</returns>
    /// <exception cref="InvalidCompiledQueryException">Thrown if the <paramref name="query"/> passed is invalid somehow</exception>
    /// TODO: Document exceptions
    public static CompiledQueryPlan BuildQueryPlan<TDoc, TOut>(QuerySession session, ICompiledQuery<TDoc, TOut> query)
    {
        eliminateStringNulls(query);

        var plan = new CompiledQueryPlan(query.GetType(), typeof(TOut)){TenantId = session.TenantId};

        assertValidityOfQueryType(plan, query.GetType());

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

    /// <summary>
    /// Uses the <see cref="ICompiledQuery{TDoc,TOut}"/> template <paramref name="queryTemplate"/> to build a template
    /// database command. Also matches parameters against a provided <see cref="CompiledQueryPlan"/>.
    /// </summary>
    /// <param name="session">The session this command should be built for</param>
    /// <param name="queryTemplate">The template to build the command from</param>
    /// <param name="statistics">Where if any QueryStatistics should be saved</param>
    /// <param name="queryPlan">The query plan to match parameters against</param>
    /// <typeparam name="TDoc">The input type of the <see cref="ICompiledQuery{TDoc,TOut}"/></typeparam>
    /// <typeparam name="TOut">The output type of the <see cref="ICompiledQuery{TDoc,TOut}"/></typeparam>
    /// <returns>A <see cref="LinqQueryParser"/> built from the template containing the template command</returns>
    internal static LinqQueryParser BuildDatabaseCommand<TDoc, TOut>(QuerySession session,
        ICompiledQuery<TDoc, TOut> queryTemplate,
        QueryStatistics? statistics,
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

    /// <summary>
    /// Sets all writable public string properties and fields that are <see langword="null"/> to <see cref="string.Empty"/>
    /// </summary>
    /// <remarks>This also sets all public static string properties and fields</remarks>
    /// <param name="query">The query instance</param>
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


    /// <summary>
    /// Throws if <paramref name="plan"/> has any invalid members
    /// </summary>
    /// <param name="plan">The plan to check</param>
    /// <param name="type">Unused</param>
    /// <exception cref="InvalidCompiledQueryException">Thrown if any members on the <see cref="CompiledQueryPlan"/> are invalid </exception>
    /// TODO: Should this actually throw if the members are unused during the actual query?
    private static void assertValidityOfQueryType(CompiledQueryPlan plan, Type type)
    {
        if (plan.InvalidMembers.Any())
        {
            var members = plan.InvalidMembers.Select(x => $"{x.GetRawMemberType()?.NameInCode()} {x.Name}")
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
