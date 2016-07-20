using Baseline;
using Marten.Util;
using Npgsql;
using System;
using System.Linq;

namespace Marten.Linq
{
    public class CustomizableWhereFragment : IWhereFragment
    {
        private readonly string _sql;
        private readonly Tuple<object, NpgsqlTypes.NpgsqlDbType?>[] _parameters;
        private readonly string _token;


        public CustomizableWhereFragment(string sql, string paramReplacementToken, params Tuple<object, NpgsqlTypes.NpgsqlDbType?>[] parameters)
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
                var param = command.AddParameter(x.Item1, x.Item2);
                sql = sql.ReplaceFirst(_token, ":" + param.ParameterName);
            });

            return sql;
        }

        public bool Contains(string sqlText)
        {
            return _sql.Contains(sqlText);
        }
    }

    public class WhereFragment : CustomizableWhereFragment
    {
        public WhereFragment(string sql, params object[] parameters) : base(sql, "?", parameters.Select(x => Tuple.Create<object, NpgsqlTypes.NpgsqlDbType?>(x, null)).ToArray()) { }
    }
}