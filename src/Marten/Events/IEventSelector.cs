using Marten.Linq;

namespace Marten.Events
{
    internal interface IEventSelector: ISelector<IEvent>
    {
        EventGraph Events { get; }
    }
}
