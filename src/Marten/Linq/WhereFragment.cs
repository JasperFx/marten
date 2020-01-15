using System;
using System.Linq;
using Baseline;
using Marten.Util;

namespace Marten.Linq
{
    public class CustomizableWhereFragment: IWhereFragment
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

        public void Apply(CommandBuilder builder)
        {
            var sql = _sql;
            _parameters.Each(x =>
            {
                var param = builder.AddParameter(x.Item1, x.Item2);
                sql = sql.ReplaceFirst(_token, ":" + param.ParameterName);
            });

            builder.Append(sql);
        }

        public bool Contains(string sqlText)
        {
            return _sql.Contains(sqlText);
        }
    }

    public class WhereFragment: CustomizableWhereFragment
    {
        public WhereFragment(string sql, params object[] parameters) : base(sql, "?", parameters.Select(x => Tuple.Create(x, TypeMappings.TryGetDbType(x?.GetType()))).ToArray())
        {
        }
    }
}
