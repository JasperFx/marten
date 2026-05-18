#nullable enable
using System.Reflection;

namespace Marten.Linq.SqlGeneration.Filters;

/// <summary>
/// Tracks the usage of values within a serialized dictionary that is part of either
/// a JSONPath or containment operator query. Captured at LINQ-parse time and walked
/// by <see cref="CompiledQueryDictionaryBuilder"/> when a compiled-query setter
/// substitutes raw constants with member reads off the user's compiled-query
/// instance.
/// </summary>
public class DictionaryValueUsage
{
    public object Value { get; }

    public DictionaryValueUsage(object value)
    {
        Value = value;
    }

    public MemberInfo? QueryMember { get; set; }
}
