using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Marten.Generation;
using Marten.Linq;

namespace Marten.Schema
{
    public interface IDocumentMapping
    {
        string UpsertName { get; }
        Type DocumentType { get; }
        string TableName { get; }
        PropertySearching PropertySearching { get; }
        IIdGeneration IdStrategy { get; }
        MemberInfo IdMember { get; }
        IList<IndexDefinition> Indexes { get; }
        string SelectFields(string tableAlias);

        TableDefinition ToTable(IDocumentSchema schema);
        UpsertFunction ToUpsertFunction();

        IField FieldFor(IEnumerable<MemberInfo> members);

        string ToResolveMethod(string typeName);

        IWhereFragment FilterDocuments(IWhereFragment query);

        IEnumerable<StorageArgument> ToArguments();
        IWhereFragment DefaultWhereFragment();
        IDocumentStorage BuildStorage(IDocumentSchema schema);

        void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer);
    }
}