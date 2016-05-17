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

        string ColumnName { get; }

        void WritePatch(DocumentMapping mapping, Action<string> writer);
        object GetValue(Expression valueExpression);

        Type MemberType { get; }
        bool ShouldUseContainmentOperator();
    }
}