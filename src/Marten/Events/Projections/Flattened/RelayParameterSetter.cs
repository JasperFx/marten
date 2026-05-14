using System;
using System.Reflection;
using JasperFx.Core.Reflection;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Projections.Flattened;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
internal class RelayParameterSetter<TSource, TValue>: IParameterSetter<TSource>
{
    private readonly NpgsqlDbType _dbType;
    private readonly IParameterSetter<TValue> _relay;
    private readonly Func<TSource, TValue> _source;

    public RelayParameterSetter(NpgsqlDbType dbType, IParameterSetter<TValue> relay, MemberInfo member)
        : this(dbType, relay, LambdaBuilder.GetProperty<TSource, TValue>((PropertyInfo)member))
    {

    }

    public RelayParameterSetter(NpgsqlDbType dbType, IParameterSetter<TValue> relay, Func<TSource, TValue> source)
    {
        _dbType = dbType;
        _relay = relay;
        _source = source;
    }

    public void SetValue(NpgsqlParameter parameter, TSource source)
    {
        var raw = _source(source);
        if (raw == null)
        {
            parameter.NpgsqlDbType = _dbType;
            parameter.Value = DBNull.Value;
        }
        else
        {
            _relay.SetValue(parameter, raw);
        }
    }
}