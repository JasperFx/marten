using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration.Util;
using Marten.Util;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Linq.Fields
{
    public class ArrayField : FieldBase
    {
        public ArrayField(string dataLocator, string pgType, ISerializer serializer, MemberInfo[] members) : base(dataLocator, pgType, serializer.Casing, members)
        {
            var rawLocator = RawLocator;

            RawLocator = $"CAST({rawLocator} as jsonb)";

            var collectionType = members.Last().GetMemberType();
            ElementType = collectionType.DetermineElementType();
            var innerPgType = PostgresqlProvider.Instance.GetDatabaseType(ElementType, EnumStorage.AsInteger);


            if (PostgresqlProvider.Instance.HasTypeMapping(ElementType))
            {
                PgType = innerPgType + "[]";
            }

            if (PgType == "jsonb[]")
            {
                PgType = "jsonb";
            }

            TypedLocator = $"CAST({rawLocator} as {PgType})";

            LocatorForIncludedDocumentId =
                $"CAST(CASE WHEN {rawLocator} = '[]' THEN '{{null}}' ELSE ARRAY(SELECT jsonb_array_elements_text(CAST({rawLocator} as jsonb))) END as {innerPgType}[])";

            if (PgType.EqualsIgnoreCase("JSONB"))
            {
                LocatorForFlattenedElements = $"unnest(CAST(CASE WHEN {rawLocator} = '[]' THEN '{{null}}' ELSE ARRAY(SELECT jsonb_array_elements(CAST({rawLocator} as jsonb))) END as jsonb[]))";
            }
            else
            {
                LocatorForFlattenedElements = $"unnest({LocatorForIncludedDocumentId})";
            }
        }

        public Type ElementType { get; }


        public override string SelectorForDuplication(string pgType)
        {
            if (pgType.EqualsIgnoreCase("JSONB"))
            {
                return JSONBLocator.Replace("d.", "");
            }

            return $"CAST(ARRAY(SELECT jsonb_array_elements_text({RawLocator.Replace("d.", "")})) as {pgType})";
        }

        public override string LocatorForIncludedDocumentId
        {
            get;
        }

        public string LocatorForFlattenedElements { get; }
    }
}
