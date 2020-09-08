using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Remotion.Linq;

namespace Marten.Schema
{
    public interface IQueryableDocument : IFieldMapping
    {
        ISqlFragment FilterDocuments(QueryModel model, ISqlFragment query);

        ISqlFragment DefaultWhereFragment();

        string[] SelectFields();

        DbObjectName Table { get; }

        DuplicatedField[] DuplicatedFields { get; }

        Type DocumentType { get; }

        TenancyStyle TenancyStyle { get; }
    }

}
