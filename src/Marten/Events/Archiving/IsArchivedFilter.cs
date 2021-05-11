using Weasel.Postgresql;

namespace Marten.Events.Archiving
{
    internal class IsArchivedFilter: IArchiveFilter
    {
        private static readonly string _sql = $"d.{IsArchivedColumn.ColumnName} = TRUE";

        public void Apply(CommandBuilder builder)
        {
            builder.Append(_sql);
        }

        public bool Contains(string sqlText)
        {
            return _sql.Contains(sqlText);
        }
    }
}