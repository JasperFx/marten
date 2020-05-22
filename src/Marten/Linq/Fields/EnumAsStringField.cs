using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;

namespace Marten.Linq.Fields
{
    public class EnumAsStringField : FieldBase
    {
        public EnumAsStringField(string dataLocator, Casing casing, MemberInfo[] members)
            : base(dataLocator, "varchar", casing, members)
        {
            if (!FieldType.IsEnum) throw new ArgumentOutOfRangeException(nameof(members), "Not an Enum type");
        }

        public override object GetValueForCompiledQueryParameter(Expression expression)
        {
            var raw = expression.Value();

            // This is to deal with Nullable enums
            if (raw == null) return null;

            return Enum.GetName(FieldType, raw);
        }

        public override string SelectorForDuplication(string pgType)
        {
            // TODO -- eliminate the replace
            return RawLocator.Replace("d.", "");
        }
    }
}
