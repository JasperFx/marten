using System;
using System.Collections.Generic;
using System.Reflection;
using JasperFx.CodeGeneration;
using Npgsql;

namespace Marten.Internal.CompiledQueries;

internal interface IQueryMember
{
    Type Type { get; }

    MemberInfo Member { get; }
    List<CompiledParameterApplication> Usages { get; }
    bool CanWrite();
    void GenerateCode(GeneratedMethod method, StoreOptions storeOptions);
    void StoreValue(object query);
    void TryMatch(List<NpgsqlParameter> parameters, ICompiledQueryAwareFilter[] filters,
        StoreOptions storeOptions);
    void TryWriteValue(UniqueValueSource valueSource, object query);
    object GetValueAsObject(object query);
}
