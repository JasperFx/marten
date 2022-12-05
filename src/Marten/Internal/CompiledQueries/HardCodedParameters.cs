using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Schema.Arguments;
using Npgsql;

namespace Marten.Internal.CompiledQueries;

public class HardCodedParameters
{
    private readonly IDictionary<int, NpgsqlParameter> _parameters = new Dictionary<int, NpgsqlParameter>();

    public HardCodedParameters(CompiledQueryPlan plan)
    {
        var parameters = plan.Command.Parameters;
        HasTenantId = parameters.Any(x => x.ParameterName == TenantIdArgument.ArgName);
        if (HasTenantId)
        {
            parameters.RemoveAll(x => x.ParameterName == TenantIdArgument.ArgName);
        }


        for (var i = 0; i < parameters.Count; i++)
        {
            if (plan.Parameters.All(x => !x.ParameterIndexes.Contains(i)))
            {
                Record(i, parameters[i]);
            }
        }
    }

    public bool HasTenantId { get; }

    public bool HasAny()
    {
        return _parameters.Any();
    }

    public void Record(int index, NpgsqlParameter parameter)
    {
        _parameters[index] = parameter;
    }

    public void Apply(NpgsqlParameter[] parameters)
    {
        foreach (var pair in _parameters)
        {
            var parameter = parameters[pair.Key];
            parameter.Value = pair.Value.Value;
            parameter.NpgsqlValue = pair.Value.NpgsqlValue;
            parameter.NpgsqlDbType = pair.Value.NpgsqlDbType;
        }
    }
}
