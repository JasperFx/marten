using Weasel.Postgresql;

namespace Marten.Events.Archiving
{
    internal class AllEventsFilter: IArchiveFilter
    {
        public void Apply(CommandBuilder builder)
        {
            builder.Append("1 = 1");
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}