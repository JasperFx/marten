using System;
using System.Reflection;
using JasperFx.Core.Reflection;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Projections.Flattened;

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