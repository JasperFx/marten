using System;
using System.Linq;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Util;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Linq.Fields;

public class ArrayField: FieldBase
{
    public ArrayField(string dataLocator, string pgType, ISerializer serializer, MemberInfo[] members): base(
        dataLocator, pgType, serializer.Casing, members)
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
            $"CAST(ARRAY(SELECT jsonb_array_elements_text(CAST({rawLocator} as jsonb))) as {innerPgType}[])";

        LocatorForElements = PgType.EqualsIgnoreCase("JSONB")
            ? $"CAST(ARRAY(SELECT jsonb_array_elements(CAST({rawLocator} as jsonb))) as jsonb[])"
            : LocatorForIncludedDocumentId;

        LocatorForFlattenedElements = $"unnest({LocatorForElements})";
    }

    public Type ElementType { get; }

    public override string LocatorForIncludedDocumentId
    {
        get;
    }

    public string LocatorForFlattenedElements { get; }

    public string LocatorForElements { get; }


    public override string SelectorForDuplication(string pgType)
    {
        if (pgType.EqualsIgnoreCase("JSONB"))
        {
            return JSONBLocator.Replace("d.", "");
        }

        return $"CAST(ARRAY(SELECT jsonb_array_elements_text({RawLocator.Replace("d.", "")})) as {pgType})";
    }
}
