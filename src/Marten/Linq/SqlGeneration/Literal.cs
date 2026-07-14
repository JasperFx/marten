#nullable enable
using System;
using System.Text.Json;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

/// <summary>
/// Exactly what it sounds like, represents a little bit
/// of literal SQL within a bigger statement
/// </summary>
/// <param name="Text"></param>
// TODO -- move this to Weasel itself
public record LiteralSql(string Text) : ISqlFragment
{
    public void Apply(ICommandBuilder builder)
    {
        builder.Append(Text);
    }

}

/// <summary>
/// Emits a captured runtime constant as a bound command parameter rather than
/// inlined SQL text. Used for non-string projection constants so that
/// attacker-influenced values (e.g. a JsonElement bound from a request body)
/// cannot break out of the generated SQL. See CWE-89 hardening for Select projections.
/// </summary>
internal record ConstantParameterSql(object Value) : ISqlFragment
{
    public void Apply(ICommandBuilder builder)
    {
        builder.AppendParameter(Unwrap(Value));
    }

    // A System.Text.Json.JsonElement has no Npgsql type handler, so unwrap it to the
    // underlying CLR scalar it represents before binding it as a parameter.
    private static object Unwrap(object value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString()!,
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => (object)DBNull.Value,
                _ => element.GetRawText()
            };
        }

        return value;
    }
}
