using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Schema;

namespace Marten.Services
{
    [Obsolete]
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

        public Delete(Type documentType, IWhereFragment @where)
        {
            DocumentType = documentType;
            Where = @where;
        }

        public IWhereFragment Where { get; set; }

        public void Configure(MartenExpressionParser parser, IDocumentStorage storage, IQueryableDocument mapping, UpdateBatch batch)
        {
            if (Where == null)
            {
                batch.Delete(mapping.Table, Id, storage.IdType);
            }
            else
            {
                batch.DeleteWhere(mapping.Table, Where);
            }
            
        }

        public override string ToString()
        {
            if (Where != null) return $"Delete {DocumentType} matching {Where}";

            return $"Delete {DocumentType} with Id {Id}";
        }
    }
}