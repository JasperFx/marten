using Npgsql;

namespace Marten.Events.Projections.Flattened;

internal interface IParameterSetter<TSource>
{
    void SetValue(NpgsqlParameter parameter, TSource source);
}