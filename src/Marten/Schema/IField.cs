using System;
using System.IO;
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
    }
}