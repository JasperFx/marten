#nullable enable
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Util;
using Newtonsoft.Json;
using Weasel.Postgresql.SqlGeneration;

namespace DocumentDbTests.CustomFields
{
    public readonly partial struct CustomId
    {
        public CustomId(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Value { get; }
    }

    public class CustomIdField: FieldBase
    {
        public CustomIdField(string dataLocator, Casing casing, MemberInfo[] members): base(dataLocator, "varchar",
            casing, members)
        {
        }

        public override ISqlFragment CreateComparison(string op, ConstantExpression value,
            Expression memberExpression)
        {
            if (value.Value == null)
            {
                return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
            }

            var def = new CommandParameter(((CustomId)value.Value).Value);
            return new ComparisonFilter(this, def, op);
        }

        public override string SelectorForDuplication(string pgType)
        {
            return RawLocator.Replace("d.", "");
        }
    }

    public class CustomIdFieldSource: IFieldSource
    {
        public bool TryResolve(string dataLocator, StoreOptions options, ISerializer serializer,
            Type documentType,
            MemberInfo[] members, out IField? field)
        {
            var fieldType = members.Last().GetRawMemberType();

            if (fieldType == null)
            {
                field = null;
                return false;
            }

            if (fieldType == typeof(CustomId))
            {
                field = new CustomIdField(dataLocator, serializer.Casing, members);
                return true;
            }

            if (fieldType.IsNullable() &&
                fieldType.GetGenericArguments()[0] == typeof(CustomId))
            {
                field = new NullableTypeField(new CustomIdField(dataLocator, serializer.Casing, members));
                return true;
            }

            field = null;
            return false;
        }
    }

    #region Boilerplate

    /// <summary>
    ///     This is all boilerplate that can be abstracted away using a library like StronglyTypedId.
    ///     Code is based on what the StronglyTypedId would generate for a struct with a string backing field.
    ///     Value is stored as a string in postgres.
    ///     For more info, see: https://github.com/andrewlock/StronglyTypedId
    /// </summary>
    [JsonConverter(typeof(CustomIdNewtonsoftJsonConverter))]
    public readonly partial struct CustomId: IComparable<CustomId>, IEquatable<CustomId>
    {
        public bool Equals(CustomId other)
        {
            return (Value, other.Value) switch
            {
                (null, null) => true,
                (null, _) => false,
                (_, null) => false,
                (_, _) => Value.Equals(other.Value)
            };
        }

        public override bool Equals(object? obj)
        {
            if (obj is null)
            {
                return false;
            }

            return obj is CustomId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(CustomId a, CustomId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(CustomId a, CustomId b)
        {
            return !(a == b);
        }

        public int CompareTo(CustomId other)
        {
            return (Value, other.Value) switch
            {
                (null, null) => 0,
                (null, _) => -1,
                (_, null) => 1,
                (_, _) => string.Compare(Value, other.Value, StringComparison.Ordinal)
            };
        }

        private class CustomIdNewtonsoftJsonConverter: JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(CustomId);
            }

            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                if (value is null)
                {
                    serializer.Serialize(writer, null);
                    return;
                }

                var id = (CustomId)value;
                serializer.Serialize(writer, id.Value);
            }

            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue,
                JsonSerializer serializer)
            {
                var deserialized = serializer.Deserialize<string>(reader);
                return deserialized is null ? null : new CustomId(deserialized);
            }
        }
    }

    #endregion
}
