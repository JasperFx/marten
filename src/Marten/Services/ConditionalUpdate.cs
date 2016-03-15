using System;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Services
{
    public class ConditionalUpdate
    {
        public Type DocumentType { get; }
        public object Id { get; }
        public object Document { get; }
        public Expression Query { get; }

        public ConditionalUpdate(Type documentType, object id, object document, Expression query)
        {
            DocumentType = documentType;
            Id = id;
            Document = document;
            Query = query;
        }
        
        public void Configure(MartenExpressionParser parser, IDocumentStorage storage, IDocumentMapping mapping, UpdateBatch batch)
        {            
            var where = parser.ParseWhereFragment(mapping, Query);
            @where = mapping.FilterDocuments(@where);

            var map = mapping as DocumentMapping;
            if (map == null)
            {
                throw new InvalidOperationException("ConditionalUpdate only works with mappings that support upserts");
            }

            var function = map.UpsertName;
            var idType = mapping.IdMember.GetMemberType();
            var pgIdType= TypeMappings.ToDbType(idType);
            var whereCommand = new NpgsqlCommand();
            var whereQuery = @where.ToSql(whereCommand);
            batch.Sproc(function).JsonEntity("doc", Document).Param("docId", Id, pgIdType).PostCondition($" where exists (select 1 from {map.TableName} as d where {whereQuery})", whereCommand.Parameters);                        
        }        
    }
}