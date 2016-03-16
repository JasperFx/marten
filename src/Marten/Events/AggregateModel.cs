using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Util;

namespace Marten.Events
{
    public class AggregateModel : IDocumentMapping
    {
        public Type AggregateType { get; }
        public string Alias { get; set; }

        public AggregateModel(Type aggregateType, string alias)
        {
            if (!aggregateType.IsConcreteTypeOf<IAggregate>())
            {
                throw new ArgumentOutOfRangeException(nameof(aggregateType), "Only types implementing IAggregate can be used as an aggregate");
            }

            AggregateType = aggregateType;
            Alias = alias;
        }

        public AggregateModel(Type aggregateType)
            : this(aggregateType, aggregateType.Name.ToTableAlias())
        {
            Alias = AggregateType.Name.ToTableAlias();
        }

        public Type DocumentType { get; }
        public string TableName { get; }
        public PropertySearching PropertySearching { get; }
        public IIdGeneration IdStrategy { get; }
        public MemberInfo IdMember { get; }
        public string[] SelectFields()
        {
            throw new NotImplementedException();
        }


        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql)
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