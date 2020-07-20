using System;
using System.Reflection;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Storage;
using Remotion.Linq;

namespace Marten.Schema
{
    public class ChildDocument: FieldMapping, IQueryableDocument
    {
        public ChildDocument(string locator, Type documentType, StoreOptions options) : base(locator, documentType, options)
        {
            DocumentType = documentType;
        }

        public Type DocumentType { get; set; }
        public TenancyStyle TenancyStyle => TenancyStyle.Single;

        public IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query)
        {
            return query;
        }

        public IWhereFragment DefaultWhereFragment()
        {
            return null;
        }

        public string[] SelectFields()
        {
            return new[] { "x" };
        }


        public DbObjectName Table
        {
            get { throw new NotSupportedException(); }
        }

        public DuplicatedField[] DuplicatedFields { get; }
    }
}
