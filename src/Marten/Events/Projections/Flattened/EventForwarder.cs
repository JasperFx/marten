using JasperFx.Events;
using Npgsql;

namespace Marten.Events.Projections.Flattened;

internal class EventForwarder<T>: IParameterSetter<IEvent>
{
    private readonly IParameterSetter<T> _inner;

    public EventForwarder(IParameterSetter<T> inner)
    {
        _inner = inner;
    }

    public void SetValue(NpgsqlParameter parameter, IEvent source)
    {
        _inner.SetValue(parameter, (T)source.Data);
    }
}