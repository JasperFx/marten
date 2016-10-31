using System;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;

namespace Marten.Schema
{
    public interface IField
    {
        MemberInfo[] Members { get; }
        string MemberName { get; }

        string SqlLocator { get; }

        string SelectionLocator { get; }

        string ColumnName { get; }

        void WritePatch(DocumentMapping mapping, SchemaPatch patch);
        object GetValue(Expression valueExpression);

        Type MemberType { get; }
        bool ShouldUseContainmentOperator();
        string LocatorFor(string rootTableAlias);
    }
}