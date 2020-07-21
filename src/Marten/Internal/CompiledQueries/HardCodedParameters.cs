using System.Collections.Generic;
using System.Linq;
using Marten.Schema.Arguments;
using Npgsql;

namespace Marten.Internal.CompiledQueries
{
    public class HardCodedParameters
    {
        private readonly IDictionary<int, NpgsqlParameter> _parameters = new Dictionary<int, NpgsqlParameter>();

        public HardCodedParameters(CompiledQueryPlan plan)
        {
            for (int i = 0; i < plan.Command.Parameters.Count; i++)
            {
                if (plan.Parameters.All(x => x.ParameterIndex != i))
                {
                    Record(i, plan.Command.Parameters[i]);
                }
            }
        }

        public bool HasAny()
        {
            return _parameters.Any();
        }

        public void Record(int index, NpgsqlParameter parameter)
        {
            // Ignore :tenantid
            if (parameter.ParameterName == TenantIdArgument.ArgName)
            {
                HasTenantId = true;
                return;
            }

            // May need to skip tenantid
            var i = HasTenantId ? index - 1 : index;

            _parameters[i] = parameter;
        }

        public bool HasTenantId { get; private set; }

        public void Apply(NpgsqlParameter[] parameters)
        {
            foreach (var pair in _parameters)
            {
                var parameter = parameters[pair.Key];
                parameter.Value = pair.Value.Value;
                parameter.NpgsqlValue = pair.Value.NpgsqlValue;
            }
        }
    }
}
