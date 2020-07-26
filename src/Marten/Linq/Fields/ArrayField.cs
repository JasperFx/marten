using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;

namespace Marten.Linq.Fields
{
    public class ArrayField : FieldBase
    {
        public ArrayField(string dataLocator, string pgType, Casing casing, MemberInfo[] members) : base(dataLocator, pgType, casing, members)
        {
            var rawLocator = RawLocator;

            RawLocator = $"CAST({rawLocator} as jsonb)";

            if (PgType == "jsonb[]")
            {
                PgType = "jsonb";
            }

            TypedLocator = $"CAST({rawLocator} as {PgType})";

            var collectionType = members.Last().GetMemberType();
            var elementType = collectionType.GetElementType();
            var innerPgType = TypeMappings.GetPgType(elementType, EnumStorage.AsInteger);

            LocatorForIncludedDocumentId =
                $"unnest(CAST(ARRAY(SELECT jsonb_array_elements_text(CAST({rawLocator} as jsonb))) as {innerPgType}[]))";
        }

        public override string SelectorForDuplication(string pgType)
        {
            if (pgType.EqualsIgnoreCase("JSONB"))
            {
                return JSONBLocator.Replace("d.", "");
            }

            // TODO -- get rid of the replacement here
            return $"CAST(ARRAY(SELECT jsonb_array_elements_text({RawLocator.Replace("d.", "")})) as {pgType})";
        }

        public override string LocatorForIncludedDocumentId
        {
            get;
        }
    }
}
