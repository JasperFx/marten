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

    /// <summary>
    /// Optional value transform applied just before the parameter is written.
    /// Used to project values like enums-as-strings or value-type wrappers
    /// onto the inner primitive that PostgreSQL actually expects.
    /// Receives the raw value boxed to <see cref="object"/> so the construction
    /// site can build the transform without closing over <typeparamref name="TValue"/>.
    /// </summary>
    private readonly Func<object, object?>? _transform;

    public ParameterSetter(PropertyInfo member) : this(LambdaBuilder.GetProperty<TSource, TValue>(member))
    {

    }

    public ParameterSetter(Func<TSource, TValue> source)
        : this(source, PostgresqlProvider.Instance.ToParameterType(typeof(TValue)), null)
    {
    }

    public ParameterSetter(Func<TSource, TValue> source, NpgsqlDbType dbType, Func<object, object?>? transform)
    {
        _source = source;
        _dbType = dbType;
        _transform = transform;
    }

    /// <summary>
    /// Convenience overload used when the leaf is built from a <see cref="PropertyInfo"/>
    /// rather than from a pre-built getter delegate.
    /// </summary>
    public ParameterSetter(PropertyInfo member, NpgsqlDbType dbType, Func<object, object?>? transform)
        : this(LambdaBuilder.GetProperty<TSource, TValue>(member), dbType, transform)
    {
    }

    public void SetValue(NpgsqlParameter parameter, TSource source)
    {
        var raw = _source(source);
        parameter.NpgsqlDbType = _dbType;
        if (raw == null)
        {
            parameter.Value = DBNull.Value;
            return;
        }

        if (_transform != null)
        {
            parameter.Value = _transform(raw) ?? (object)DBNull.Value;
            return;
        }

        parameter.Value = raw;
    }
}
