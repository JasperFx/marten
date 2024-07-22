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

    /// <summary>
    /// Stores the value of the member represented by this <see cref="IQueryMember"/> as currently set on <paramref name="query"/>
    /// </summary>
    /// <param name="query">The object from which to read the value</param>
    void StoreValue(object query);

    bool TryMatch(NpgsqlParameter parameter, StoreOptions options, ICompiledQueryAwareFilter[] filters,
        out ICompiledQueryAwareFilter filter);

    /// <summary>
    /// Tries to write a unique value retrieved from <see cref="valueSource"/> into the member of <paramref name="query"/>
    /// </summary>
    /// <remarks>
    /// This should also update any stored value previously stored by <see cref="StoreValue"/>
    /// </remarks>
    /// <param name="valueSource">A source for unique values</param>
    /// <param name="query">The object to write to</param>
    void TryWriteValue(UniqueValueSource valueSource, object query);
    object GetValueAsObject(object query);
}
