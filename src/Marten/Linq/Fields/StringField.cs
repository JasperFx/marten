using System.Reflection;

namespace Marten.Linq.Fields
{
    public class StringField : FieldBase
    {
        public StringField(string dataLocator, Casing casing, MemberInfo[] members) : base(dataLocator, "varchar", casing, members)
        {
        }

        public override string SelectorForDuplication(string pgType)
        {
            return RawLocator.Replace("d.", "");
        }
    }
}
