using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Schema;

namespace Marten.Services
{
    public class Delete
    {
        public Type DocumentType { get; }
        public object Id { get; }
        public object Document { get; }

        public Delete(Type documentType, object id, object document = null)
        {
            DocumentType = documentType;
            Id = id;
            Document = document;
        }

        public Delete(Type documentType, DocumentQuery query)
        {
            DocumentType = documentType;
            Query = query;
        }

        [Obsolete("Try to use QueryModel here instead")]
        public DocumentQuery Query { get; set; }

        public void Configure(MartenExpressionParser parser, IDocumentStorage storage, IDocumentMapping mapping, UpdateBatch batch)
        {
            if (Query == null)
            {
                batch.Delete(mapping.Table, Id, storage.IdType);
            }
            else
            {
                var where = Query.BuildWhereClause();
                batch.DeleteWhere(mapping.Table, where);
            }
            
        }

        public override string ToString()
        {
            if (Query != null) return $"Delete {DocumentType} matching {Query}";

            return $"Delete {DocumentType} with Id {Id}";
        }
    }
}