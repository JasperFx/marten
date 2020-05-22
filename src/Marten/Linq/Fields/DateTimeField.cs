using System.Reflection;

namespace Marten.Linq.Fields
{
    public class DateTimeField : FieldBase
    {
        public DateTimeField(string dataLocator, string schemaName, Casing casing, MemberInfo[] members) : base(dataLocator, "timestamp without time zone", casing, members)
        {
            TypedLocator = $"{schemaName}.mt_immutable_timestamp({RawLocator})";
        }

        public override string SelectorForDuplication(string pgType)
        {
            // TODO -- eliminate replace
            return TypedLocator.Replace("d.", "");
        }
    }
}
