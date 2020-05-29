using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Linq.Parsing
{
    public interface ITenantWhereFragment
    {
    }

    public class AnyTenant: WhereFragment, IMethodCallParser, ITenantWhereFragment
    {
        public AnyTenant() : base("1=1")
        {
        }

        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.AnyTenant)
                   && expression.Method.DeclaringType == typeof(LinqExtensions);
        }

        public IWhereFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            return this;
        }
    }

    public class TenantIsOneOf: IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.TenantIsOneOf)
                   && expression.Method.DeclaringType == typeof(LinqExtensions);
        }

        public IWhereFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var values = expression.Arguments.Last().Value().As<string[]>();
            return new TenantIsOneOfWhereFragment(values);
        }
    }

    public class TenantIsOneOfWhereFragment: IWhereFragment, ITenantWhereFragment
    {
        private static readonly string _filter = $"{TenantIdColumn.Name} = ANY(?)";

        private readonly string[] _values;

        public TenantIsOneOfWhereFragment(string[] values)
        {
            _values = values;
        }

        public void Apply(CommandBuilder builder)
        {
            var param = builder.AddParameter(_values);
            param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
            builder.Append(_filter.Replace("?", ":" + param.ParameterName));
        }

        public bool Contains(string sqlText)
        {
            return _filter.Contains(sqlText);
        }
    }
}
