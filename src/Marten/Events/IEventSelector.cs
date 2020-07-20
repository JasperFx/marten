using Marten.Internal.Linq;
using Marten.Util;

namespace Marten.Events
{
    internal interface IEventSelector: ISelector<IEvent>
    {
        EventGraph Events { get; }
        void WriteSelectClause(CommandBuilder sql);
        string[] SelectFields();
    }
}
