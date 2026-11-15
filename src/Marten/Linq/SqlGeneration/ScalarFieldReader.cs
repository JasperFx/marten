#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq.SqlGeneration;

/// <summary>
/// Reads one scalar column as <typeparamref name="T" />. An enum cannot be read by Npgsql's typed reader from the
/// integer or text column Marten stores it as, so it is read as that column's own type and converted.
/// <para>
/// #5940: the scalar select clause behind <c>SelectMany()</c> over a value collection read an enum element
/// straight through <c>GetFieldValue&lt;TEnum&gt;</c> and threw <c>InvalidCastException</c>. It now shares this
/// reader with the select clause of a top-level <c>Select()</c>, which already converted enums.
/// </para>
/// </summary>
internal static class ScalarFieldReader
{
    public static T Read<T>(DbDataReader reader, int ordinal)
    {
        if (!typeof(T).IsEnum)
        {
            return reader.GetFieldValue<T>(ordinal);
        }

        if (reader.GetFieldType(ordinal) == typeof(string))
        {
            return (T)Enum.Parse(typeof(T), reader.GetFieldValue<string>(ordinal), ignoreCase: true);
        }

        return (T)Enum.ToObject(typeof(T), reader.GetFieldValue<int>(ordinal));
    }

    public static async Task<T> ReadAsync<T>(DbDataReader reader, int ordinal, CancellationToken token)
    {
        if (!typeof(T).IsEnum)
        {
            return await reader.GetFieldValueAsync<T>(ordinal, token).ConfigureAwait(false);
        }

        if (reader.GetFieldType(ordinal) == typeof(string))
        {
            var text = await reader.GetFieldValueAsync<string>(ordinal, token).ConfigureAwait(false);
            return (T)Enum.Parse(typeof(T), text, ignoreCase: true);
        }

        var value = await reader.GetFieldValueAsync<int>(ordinal, token).ConfigureAwait(false);
        return (T)Enum.ToObject(typeof(T), value);
    }
}
