using Weasel.Postgresql;

namespace Marten.Events.Archiving;

internal class AllEventsFilter: IArchiveFilter
{
    public void Apply(ICommandBuilder builder)
    {
        builder.Append("1 = 1");
    }
}
