using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Linq;
using Marten.Services.Includes;

namespace Marten.Schema
{
    public interface IQueryableDocument
    {
        IWhereFragment FilterDocuments(IWhereFragment query);

        IWhereFragment DefaultWhereFragment();

        IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IQueryableDocument other, MemberInfo[] members, Action<TOther> callback) where TOther : class;

        IField FieldFor(IEnumerable<MemberInfo> members);

        string[] SelectFields();

        PropertySearching PropertySearching { get; }

        TableName Table { get; }
    }
}