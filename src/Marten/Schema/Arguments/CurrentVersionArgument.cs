using System;
using System.Linq.Expressions;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class CurrentVersionArgument: UpsertArgument
    {
        public CurrentVersionArgument()
        {
            Arg = "current_version";
            PostgresType = "uuid";
            DbType = NpgsqlDbType.Uuid;
            Column = null;
        }

        public override void GenerateCode(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            method.Frames.Code("setCurrentVersionParameter({0}[{1}]);", parameters, i);
        }
    }
}
