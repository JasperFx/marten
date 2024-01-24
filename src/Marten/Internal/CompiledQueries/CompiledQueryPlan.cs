using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core.Reflection;
using Marten.Events.CodeGeneration;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Linq.Includes;
using Marten.Linq.QueryHandlers;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Internal.CompiledQueries;

public class CompiledQueryPlan : ICommandBuilder
{
    /// <summary>
    /// Placeholder in the compiled query for parameters
    /// </summary>
    public const string ParameterPlaceholder = "^";

    /// <summary>
    /// The type for which this <see cref="CompiledQueryPlan"/> is constructed
    /// </summary>
    public Type QueryType { get; }

    /// <summary>
    /// The output type that should be produced by this <see cref="CompiledQueryPlan"/>
    /// </summary>
    public Type OutputType { get; }

    /// <summary>
    /// Member of the <see cref="QueryType"/> that holds <see cref="QueryStatistics"/> if any
    /// </summary>
    public MemberInfo? StatisticsMember { get; set; }

    /// <summary>
    /// Members of the <see cref="QueryType"/> that can be used to include additional data
    /// </summary>
    public List<MemberInfo> IncludeMembers { get; } = new();

    /// <summary>
    /// Members of the <see cref="QueryType"/> that are invalid as parameters
    /// </summary>
    public List<MemberInfo> InvalidMembers { get; } = new();

    /// <summary>
    /// Members of the <see cref="QueryType"/> that are usable as parameters
    /// </summary>
    public List<IQueryMember> QueryMembers { get; } = new();
    internal List<IIncludePlan> IncludePlans { get; } = new();

    private readonly List<CommandPlan> _commands = new();
    public IQueryHandler HandlerPrototype { get; set; }


    /// <summary>
    /// Create a new <see cref="CompiledQueryPlan"/> for the given <paramref name="queryType"/> which produces <paramref name="outputType"/> as output.
    /// </summary>
    /// <param name="queryType">The type of the query</param>
    /// <param name="outputType">The produced type</param>
    public CompiledQueryPlan(Type queryType, Type outputType)
    {
        QueryType = queryType;
        OutputType = outputType;

        sortMembers();
    }

    #region finding members on query type

    /// <summary>
    /// This function's purpose is to sort all the members on the <see cref="QueryType"/> into categories
    /// </summary>
    /// <remarks>
    /// The possible categories are:
    /// <list type="number">
    /// <item> Statistic member, only one </item>
    /// <item> Include members, which can be filled by fetching additional data during the same operation </item>
    /// <item> Invalid members, which are at this point in time all nullable fields as well as all types for which no
    ///     <see cref="QueryCompiler.Finders"/> instance exists </item>
    /// <item> Query members, which are the ones that can actually be used by the compiled query as parameters </item>
    /// </list>
    /// </remarks>
    /// <seealso cref="StatisticsMember"/>
    /// <seealso cref="IncludeMembers"/>
    /// <seealso cref="InvalidMembers"/>
    /// <seealso cref="QueryMembers"/>
    /// TODO: Possibly throw on duplicate QueryStatistics?
    private void sortMembers()
    {
        foreach (var member in findMembers())
        {
            var memberType = member.GetRawMemberType();
            if (memberType == typeof(QueryStatistics))
            {
                StatisticsMember = member;
            }

            else if (memberType.Closes(typeof(IDictionary<,>)))
            {
                IncludeMembers.Add(member);
            }
            else if (memberType.Closes(typeof(Action<>)))
            {
                IncludeMembers.Add(member);
            }
            else if (memberType.Closes(typeof(IList<>)))
            {
                IncludeMembers.Add(member);
            }
            else if (memberType.IsNullable())
            {
                InvalidMembers.Add(member);
            }
            else if (QueryCompiler.Finders.All(x => !x.Matches(memberType)))
            {
                InvalidMembers.Add(member);
            }
            else if (member is PropertyInfo)
            {
                var queryMember = typeof(PropertyQueryMember<>).CloseAndBuildAs<IQueryMember>(member, memberType);
                QueryMembers.Add(queryMember);
            }
            else if (member is FieldInfo)
            {
                var queryMember = typeof(FieldQueryMember<>).CloseAndBuildAs<IQueryMember>(member, memberType);
                QueryMembers.Add(queryMember);
            }
        }
    }

    /// <summary>
    /// Iterates over all <see langword="public"/> fields and properties on <see cref="QueryType"/>
    /// </summary>
    /// <returns>An enumerable that iterates over all fields and properties</returns>
    private IEnumerable<MemberInfo> findMembers()
    {
        foreach (var field in QueryType.GetFields(BindingFlags.Instance | BindingFlags.Public)
                     .Where(x => !x.HasAttribute<MartenIgnoreAttribute>())) yield return field;

        foreach (var property in QueryType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(x => !x.HasAttribute<MartenIgnoreAttribute>())) yield return property;
    }

    #endregion

    public string TenantId { get; set; }

    #region ICommandBuilder implementation

    private CommandPlan _current;

    string ICommandBuilder.LastParameterName => _current.Parameters.LastOrDefault()?.Parameter.ParameterName;

    private CommandPlan appendCommand()
    {
        var plan = new CommandPlan();
        _commands.Add(plan);

        return plan;
    }

    void ICommandBuilder.Append(string sql)
    {
        _current ??= appendCommand();

        _current.CommandText += sql;
    }

    void ICommandBuilder.Append(char character)
    {
        _current ??= appendCommand();
        _current.CommandText += character;
    }

    private int _parameterIndex = 0;

    NpgsqlParameter ICommandBuilder.AppendParameter(object value)
    {
        _current ??= appendCommand();
        var name = "p" + _parameterIndex;
        _parameterIndex++;
        var usage = new ParameterUsage(_current.Parameters.Count, name, value);
        _current.Parameters.Add(usage);

        _current.CommandText += ParameterPlaceholder;

        return usage.Parameter;
    }

    NpgsqlParameter ICommandBuilder.AppendParameter(object value, NpgsqlDbType? dbType)
    {
        return appendParameter(value, dbType);
    }

    private NpgsqlParameter appendParameter(object value, NpgsqlDbType? dbType)
    {
        _current ??= appendCommand();
        var name = "p" + _parameterIndex;
        _parameterIndex++;
        var usage = new ParameterUsage(_current.Parameters.Count, name, value, dbType);
        _current.Parameters.Add(usage);

        _current.CommandText += ParameterPlaceholder;

        return usage.Parameter;
    }

    void ICommandBuilder.AppendParameters(params object[] parameters)
    {
        _current ??= appendCommand();
        throw new NotSupportedException();
    }

    NpgsqlParameter[] ICommandBuilder.AppendWithParameters(string text)
    {
        _current ??= appendCommand();
        var split = text.Split('?');
        var parameters = new NpgsqlParameter[split.Length - 1];

        _current.CommandText += split[0];
        for (var i = 0; i < parameters.Length; i++)
        {
            // Just need a placeholder parameter type and value
            var parameter = appendParameter(DBNull.Value, NpgsqlDbType.Text);
            parameters[i] = parameter;
            _current.CommandText += split[i + 1];
        }

        return parameters;
    }

    NpgsqlParameter[] ICommandBuilder.AppendWithParameters(string text, char placeholder)
    {
        _current ??= appendCommand();
        var split = text.Split(placeholder);
        var parameters = new NpgsqlParameter[split.Length - 1];

        _current.CommandText += split[0];
        for (var i = 0; i < parameters.Length; i++)
        {
            // Just need a placeholder parameter type and value
            var parameter = appendParameter(DBNull.Value, NpgsqlDbType.Text);
            parameters[i] = parameter;
            _current.CommandText += split[i + 1];
        }

        return parameters;
    }

    void ICommandBuilder.StartNewCommand()
    {
        _current = appendCommand();
    }

    void ICommandBuilder.AddParameters(object parameters)
    {
        throw new NotSupportedException(
            "No, just no. Marten does not support parameters via anonymous objects in compiled queries");
    }

    #endregion

    /// <summary>
    /// Returns the <see cref="StatisticsMember"/>'s content of <paramref name="query"/> if any
    /// </summary>
    /// <param name="query">The query instance</param>
    /// <returns>A <see cref="QueryStatistics"/> instance if the <see cref="QueryType"/> has a <see cref="StatisticsMember"/>, null otherwise </returns>
    public QueryStatistics? GetStatisticsIfAny(object query)
    {
        if (StatisticsMember is PropertyInfo p)
        {
            return (QueryStatistics)p.GetValue(query) ?? new QueryStatistics();
        }

        if (StatisticsMember is FieldInfo f)
        {
            return (QueryStatistics)f.GetValue(query) ?? new QueryStatistics();
        }

        return null;
    }

    /// <summary>
    /// Tries to create a template from the <paramref name="query"/>, making sure that all <see cref="QueryMembers"/>
    /// have unique values
    /// </summary>
    /// <param name="query">The query to create a template from</param>
    /// <typeparam name="TDoc">Document input type to the <see cref="ICompiledQuery{TDoc,TOut}"/></typeparam>
    /// <typeparam name="TOut">Output type of the <see cref="ICompiledQuery{TDoc,TOut}"/></typeparam>
    /// <returns>An instance of the same type as <paramref name="query"/> with unique values, can be identical to <paramref name="query"/></returns>
    /// <exception cref="InvalidCompiledQueryException">Thrown when a template with unique values could not be created</exception>
    public ICompiledQuery<TDoc, TOut> CreateQueryTemplate<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
    {
        foreach (var parameter in QueryMembers) parameter.StoreValue(query);

        if (!(query is IQueryPlanning) && areAllMemberValuesUnique(query))
        {
            return query;
        }

        try
        {
            return (ICompiledQuery<TDoc, TOut>)TryCreateUniqueTemplate(query.GetType());
        }
        catch (Exception e)
        {
            throw new InvalidCompiledQueryException("Unable to create a Compiled Query template", e);
        }
    }

    /// <summary>
    /// Tries to create a unique template instance of <paramref name="type"/>
    /// </summary>
    /// <param name="type">The type for which to create a unique template instance</param>
    /// <returns>A template instance with unique values for <see cref="QueryMembers"/></returns>
    /// <exception cref="InvalidOperationException">Thrown if an instance of the type cannot be constructed</exception>
    /// <exception cref="InvalidCompiledQueryException">Thrown if unique values cannot be assigned to the created instance</exception>
    public object TryCreateUniqueTemplate(Type type)
    {
        var constructor = type.GetConstructors().MaxBy(x => x.GetParameters().Count());


        if (constructor == null)
        {
            throw new InvalidOperationException("Cannot find a suitable constructor for query planning for type " +
                                                type.FullNameInCode());
        }

        var valueSource = new UniqueValueSource();

        var ctorArgs = valueSource.ArgsFor(constructor);
        var query = Activator.CreateInstance(type, ctorArgs);
        if (query is IQueryPlanning planning)
        {
            planning.SetUniqueValuesForQueryPlanning();
            foreach (var member in QueryMembers) member.StoreValue(query);
        }

        if (areAllMemberValuesUnique(query))
        {
            return query;
        }

        foreach (var queryMember in QueryMembers) queryMember.TryWriteValue(valueSource, query);

        if (areAllMemberValuesUnique(query))
        {
            return query;
        }

        throw new InvalidCompiledQueryException("Marten is unable to create a compiled query plan for type " +
                                                type.FullNameInCode());
    }

    /// <summary>
    /// Checks whether the <paramref name="query"/> has only unique values by applying all <see cref="QueryCompiler.Finders"/>
    /// </summary>
    /// <param name="query">The query object to check</param>
    /// <returns>True if all values are unique, false otherwise</returns>
    private bool areAllMemberValuesUnique(object query)
    {
        return QueryCompiler.Finders.All(x => x.AreValuesUnique(query, this));
    }

    public void MatchParameters(StoreOptions options, ICompiledQueryAwareFilter[] filters)
    {
        foreach (var commandPlan in _commands)
        {
            foreach (var usage in commandPlan.Parameters)
            {
                if (usage.Parameter.Value.Equals(TenantId))
                {
                    usage.IsTenant = true;
                }
                else
                {
                    foreach (var queryMember in QueryMembers)
                    {
                        if (queryMember.TryMatch(usage.Parameter, options, filters, out var filter))
                        {
                            usage.Member = queryMember;
                            usage.Filter = filter;
                            break;
                        }
                    }
                }
            }
        }
    }

    public void GenerateCode(GeneratedMethod method, StoreOptions storeOptions)
    {
        int number = 1;
        foreach (var command in _commands)
        {
            if (number != 1)
            {
                method.Frames.Code($"{{0}}.{nameof(ICommandBuilder.StartNewCommand)}();", Use.Type<ICommandBuilder>());
            }

            var parameters = $"parameters{number}";

            if (command.Parameters.Any())
            {
                method.Frames.Code($"var {parameters} = {{0}}.{nameof(CommandBuilder.AppendWithParameters)}(@{{1}}, '{ParameterPlaceholder}');",
                    Use.Type<ICommandBuilder>(), command.CommandText);

                foreach (var usage in command.Parameters)
                {
                    usage.GenerateCode(method, parameters, storeOptions);
                }
            }
            else
            {
                method.Frames.Code($"{{0}}.Append({{1}});", Use.Type<ICommandBuilder>(), command.CommandText);
            }

            number++;
        }
    }
}
