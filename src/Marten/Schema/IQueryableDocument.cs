using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Linq;
using Marten.Services.Includes;
using Remotion.Linq;

namespace Marten.Schema
{
    public interface IQueryableDocument
    {
        IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query);

        IWhereFragment DefaultWhereFragment();

        IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IQueryableDocument other, MemberInfo[] members, Action<TOther> callback);

        IField FieldFor(IEnumerable<MemberInfo> members);

        string[] SelectFields();

        PropertySearching PropertySearching { get; }

        TableName Table { get; }
    }
}