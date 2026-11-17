#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Members;

namespace Marten.Linq.SqlGeneration;

/// <summary>
/// Reads one scalar column as <typeparamref name="T" />. An enum cannot be read by Npgsql's typed reader from the
/// integer or text column Marten stores it as, so it is read as that column's own type and converted.
/// <para>
/// #5940: the scalar select clause behind <c>SelectMany()</c> over a value collection read an enum element
/// straight through <c>GetFieldValue&lt;TEnum&gt;</c> and threw <c>InvalidCastException</c>. It now shares this
/// reader with the select clause of a top-level <c>Select()</c>, which already converted enums.
/// </para>
/// <para>
/// #6064: an enum stored as text is stored under whatever name the serializer gave the member, which is not the
/// declared one once the member is renamed (<c>[JsonStringEnumMemberName]</c>, <c>[EnumMember]</c>, a naming
/// policy). <c>Enum.Parse</c> only knows the declared names, so the text is matched against the names the
/// serializer writes, through the same <see cref="EnumMemberNames" /> the <c>Where()</c> side renders with (#5376).
/// </para>
/// </summary>
internal static class ScalarFieldReader
{
    public static T Read<T>(DbDataReader reader, int ordinal, ISerializer? serializer = null)
    {
        if (!typeof(T).IsEnum)
        {
            return reader.GetFieldValue<T>(ordinal);
        }

        if (reader.GetFieldType(ordinal) == typeof(string))
        {
            return ParseStoredName<T>(reader.GetFieldValue<string>(ordinal), serializer);
        }

        return (T)Enum.ToObject(typeof(T), reader.GetFieldValue<int>(ordinal));
    }

    public static async Task<T> ReadAsync<T>(DbDataReader reader, int ordinal, CancellationToken token,
        ISerializer? serializer = null)
    {
        if (!typeof(T).IsEnum)
        {
            return await reader.GetFieldValueAsync<T>(ordinal, token).ConfigureAwait(false);
        }

        if (reader.GetFieldType(ordinal) == typeof(string))
        {
            var text = await reader.GetFieldValueAsync<string>(ordinal, token).ConfigureAwait(false);
            return ParseStoredName<T>(text, serializer);
        }

        var value = await reader.GetFieldValueAsync<int>(ordinal, token).ConfigureAwait(false);
        return (T)Enum.ToObject(typeof(T), value);
    }

    /// <summary>
    /// The member <paramref name="text" /> names, by the name <paramref name="serializer" /> stores it under, else
    /// by its declared name, case-insensitively, as before. The second covers a selector built without a session
    /// and text written before a member was renamed.
    /// </summary>
    internal static T ParseStoredName<T>(string text, ISerializer? serializer)
    {
        if (serializer != null && StoredNames.For(serializer, typeof(T)).TryGetValue(text, out var member))
        {
            return (T)member;
        }

        return (T)Enum.Parse(typeof(T), text, ignoreCase: true);
    }

    /// <summary>
    /// Stored name to member, per serializer and enum type, for the same reason <see cref="EnumMemberNames" />
    /// caches per serializer: two stores in one process can spell one member differently.
    /// </summary>
    private static class StoredNames
    {
        private static readonly ConditionalWeakTable<ISerializer, ConcurrentDictionary<Type, Dictionary<string, object>>>
            _bySerializer = new();

        public static Dictionary<string, object> For(ISerializer serializer, Type enumType)
        {
            return _bySerializer.GetOrCreateValue(serializer).GetOrAdd(enumType, build, serializer);
        }

        private static Dictionary<string, object> build(Type enumType, ISerializer serializer)
        {
            var names = new Dictionary<string, object>(StringComparer.Ordinal);

            // GetValuesAsUnderlyingType, not GetValues(Type): the latter needs dynamic code under Native AOT
            foreach (var raw in Enum.GetValuesAsUnderlyingType(enumType))
            {
                var member = Enum.ToObject(enumType, raw);
                names.TryAdd(EnumMemberNames.Of(serializer, enumType, member), member);
            }

            return names;
        }
    }
}
