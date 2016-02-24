using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;

namespace Marten.Events
{
    public class AggregateModel : IDocumentMapping
    {
        public Type AggregateType { get; }
        public string Alias { get; set; }

        public AggregateModel(Type aggregateType, string alias)
        {
            AggregateType = aggregateType;
            Alias = alias;
        }

        public AggregateModel(Type aggregateType)
        {
            AggregateType = aggregateType;
            Alias = AggregateType.Name.SplitPascalCase().ToLower().Replace(" ", "_");
        }

        public Type DocumentType { get; }
        public string TableName { get; }
        public PropertySearching PropertySearching { get; }
        public IIdGeneration IdStrategy { get; }
        public MemberInfo IdMember { get; }
        public string SelectFields(string tableAlias)
        {
            throw new NotImplementedException();
        }

        public bool ShouldRegenerate(IDocumentSchema schema)
        {
            throw new NotImplementedException();
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            throw new NotImplementedException();
        }

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            throw new NotImplementedException();
        }

        public IWhereFragment DefaultWhereFragment()
        {
            throw new NotImplementedException();
        }

        public IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            throw new NotImplementedException();
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            throw new NotImplementedException();
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            throw new NotImplementedException();
        }

        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            throw new NotImplementedException();
        }
    }
}