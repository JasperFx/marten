using System.Collections.Generic;
using Baseline;
using Marten.Util;
using Npgsql;

namespace Marten.Linq
{
    public class WhereFragment : IWhereFragment
    {
        private readonly string _sql;
        private readonly object[] _parameters;

        public WhereFragment(string sql, params object[] parameters)
        {
            _sql = sql;
            _parameters = parameters;
        }

        public string ToSql(NpgsqlCommand command)
        {
            var sql = _sql;
            _parameters.Each(x =>
            {
                var param = command.AddParameter(x);
                sql = sql.ReplaceFirst("?", ":" + param.ParameterName);
            });

            return sql;
        }

        public bool Contains(string sqlText)
        {
            return _sql.Contains(sqlText);
        }
    }
}