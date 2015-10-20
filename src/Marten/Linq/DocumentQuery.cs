using Npgsql;

namespace Marten.Linq
{
    public class DocumentQuery<T>
    {
        private readonly string _tableName;

        public DocumentQuery(string tableName)
        {
            _tableName = tableName;
        }

        public IWhereFragment Where { get; set; }

        public NpgsqlCommand ToCommand()
        {
            var command = new NpgsqlCommand();
            var sql = "select data from " + _tableName;

            if (Where != null)
            {
                sql += " where " + Where.ToSql(command);
            }

            command.CommandText = sql;

            return command;
        }

    }
}