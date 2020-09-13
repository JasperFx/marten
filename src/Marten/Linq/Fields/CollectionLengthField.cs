using System;
using System.Reflection;

namespace Marten.Linq.Fields
{
    public class CollectionLengthField: FieldBase
    {
        public CollectionLengthField(ArrayField inner, MemberInfo[] members): base($"jsonb_array_length({inner.JSONBLocator})", "int", Casing.Default, members)
        {
            TypedLocator = $"jsonb_array_length({inner.JSONBLocator})";
        }

        public override string SelectorForDuplication(string pgType)
        {
            throw new System.NotSupportedException();
        }
    }


}
