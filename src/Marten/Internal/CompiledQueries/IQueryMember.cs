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
    bool CanWrite();

    void StoreValue(object query);

    bool TryMatch(NpgsqlParameter parameter, StoreOptions options, ICompiledQueryAwareFilter[] filters,
        out ICompiledQueryAwareFilter filter);

    void TryWriteValue(UniqueValueSource valueSource, object query);
    object GetValueAsObject(object query);
}
