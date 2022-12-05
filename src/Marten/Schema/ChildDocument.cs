using System;
using Marten.Linq.Fields;
using Marten.Storage;
using Remotion.Linq;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Schema;

public class ChildDocument: FieldMapping
{
    public ChildDocument(string locator, Type documentType, StoreOptions options): base(locator, documentType, options)
    {
        DocumentType = documentType;
    }

    public Type DocumentType { get; set; }
    public TenancyStyle TenancyStyle => TenancyStyle.Single;

    public DbObjectName TableName => throw new NotSupportedException();

    public DuplicatedField[] DuplicatedFields { get; }

    public ISqlFragment FilterDocuments(QueryModel model, ISqlFragment query)
    {
        return query;
    }

    public ISqlFragment DefaultWhereFragment()
    {
        return null;
    }
}
