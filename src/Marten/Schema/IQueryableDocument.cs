using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq;
using Marten.Linq.Fields;
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

        IField FieldFor(MemberInfo member);

        string[] SelectFields();

        PropertySearching PropertySearching { get; }

        DbObjectName Table { get; }

        DuplicatedField[] DuplicatedFields { get; }

        DeleteStyle DeleteStyle { get; }

        Type DocumentType { get; }
    }

    public static class QueryableDocumentExtensions
    {
        public static IField FieldFor(this IQueryableDocument document, Expression expression)
        {
            return document.FieldFor(FindMembers.Determine(expression));
        }
    }
}
