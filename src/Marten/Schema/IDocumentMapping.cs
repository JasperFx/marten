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
        string SelectFields(string tableAlias);

        bool ShouldRegenerate(IDocumentSchema schema);

        IField FieldFor(IEnumerable<MemberInfo> members);

        IWhereFragment FilterDocuments(IWhereFragment query);


        IWhereFragment DefaultWhereFragment();
        IDocumentStorage BuildStorage(IDocumentSchema schema);

        void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer);
    }
}