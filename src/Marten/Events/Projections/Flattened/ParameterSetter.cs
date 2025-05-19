using System;
using System.Reflection;
using JasperFx.Core.Reflection;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Projections.Flattened;

internal class ParameterSetter<TSource, TValue>: IParameterSetter<TSource>
{
    private readonly Func<TSource, TValue> _source;
    private readonly NpgsqlDbType _dbType;

    public ParameterSetter(PropertyInfo member) : this(LambdaBuilder.GetProperty<TSource, TValue>(member))
    {

    }

    public ParameterSetter(Func<TSource, TValue> source)
    {
        _source = source;
        _dbType = PostgresqlProvider.Instance.ToParameterType(typeof(TValue));
    }

    public void SetValue(NpgsqlParameter parameter, TSource source)
    {
        var raw = _source(source);
        parameter.NpgsqlDbType = _dbType;
        if (raw == null)
        {
            parameter.Value = DBNull.Value;
        }
        else
        {
            parameter.Value = raw;
        }
    }
}