using System;
using System.Collections.Generic;
using System.Reflection;
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

    /// <summary>
    ///     Offer this member's canned template value to every filter so that filters
    ///     carrying multiple member values inside a single parameter (jsonpath vars
    ///     payloads, containment dictionaries) can associate each of their captured
    ///     values with the right query member. Parameter matching alone only pairs
    ///     one member with one parameter.
    /// </summary>
    void MarkFilterUsages(StoreOptions options, ICompiledQueryAwareFilter[] filters);

    void TryWriteValue(UniqueValueSource valueSource, object query);
    object GetValueAsObject(object query);
}
