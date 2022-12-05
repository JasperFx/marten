using System;
using System.Collections.Generic;
using System.Reflection;
using JasperFx.CodeGeneration;
using Npgsql;

namespace Marten.Internal.CompiledQueries;

public interface IQueryMember
{
    Type Type { get; }

    MemberInfo Member { get; }
    IList<int> ParameterIndexes { get; }
    bool CanWrite();
    void GenerateCode(GeneratedMethod method, StoreOptions storeOptions);
    void StoreValue(object query);
    void TryMatch(List<NpgsqlParameter> parameters, StoreOptions storeOptions);
    void TryWriteValue(UniqueValueSource valueSource, object query);
    object GetValueAsObject(object query);
}
