using Weasel.Postgresql;

namespace Marten.Events.Archiving
{
    internal class IsNotArchivedFilter: IArchiveFilter
    {
        private static readonly string _sql = $"d.{IsArchivedColumn.ColumnName} = FALSE";

        public static readonly IsNotArchivedFilter Instance = new IsNotArchivedFilter();

        private IsNotArchivedFilter()
        {

        }

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