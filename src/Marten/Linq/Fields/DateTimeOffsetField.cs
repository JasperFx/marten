using System.Reflection;

namespace Marten.Linq.Fields
{
    public class DateTimeOffsetField : FieldBase
    {
        public DateTimeOffsetField(string dataLocator, string schemaName, Casing casing, MemberInfo[] members) : base(dataLocator, "timestamp with time zone", casing, members)
        {
            TypedLocator = $"{schemaName}.mt_immutable_timestamptz({RawLocator})";
        }

        public override string SelectorForDuplication(string pgType)
        {
            // TODO -- eliminate replace
            return TypedLocator.Replace("d.", "");
        }
    }
}
