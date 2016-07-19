using Baseline;
using Marten.Util;
using Npgsql;

namespace Marten.Linq
{
    public class WhereFragment : IWhereFragment
    {
        private readonly string _sql;
        private readonly object[] _parameters;
        private readonly string _token;


        public WhereFragment(string sql, params object[] parameters) : this(sql, "?", parameters) { }
        public WhereFragment(string sql, string paramReplacementToken, params object[] parameters)
        {
            _sql = sql;
            _parameters = parameters;
            _token = paramReplacementToken;
        }

        public string ToSql(NpgsqlCommand command)
        {
            var sql = _sql;
            _parameters.Each(x =>
            {
                var param = command.AddParameter(x);
                sql = sql.ReplaceFirst(_token, ":" + param.ParameterName);
            });

            return sql;
        }

        public bool Contains(string sqlText)
        {
            return _sql.Contains(sqlText);
        }
    }
}