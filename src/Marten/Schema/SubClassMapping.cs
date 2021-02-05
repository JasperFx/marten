using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;

namespace Marten.Schema
{
    public class SubClassMapping: IDocumentMapping
    {
        public SubClassMapping(Type documentType, DocumentMapping parent, StoreOptions storeOptions,
            string alias = null)
        {
            DocumentType = documentType;
            Inner = new DocumentMapping(documentType, storeOptions);
            Parent = parent;
            Alias = alias ?? GetTypeMartenAlias(documentType);
            Aliases = new[] {Alias};
        }

        public SubClassMapping(Type documentType, DocumentMapping parent, StoreOptions storeOptions,
            IEnumerable<MappedType> otherSubclassTypes, string alias = null)
            : this(documentType, parent, storeOptions, alias)
        {
            Aliases = otherSubclassTypes
                .Where(
                    t =>
                        t.Type.GetTypeInfo().IsSubclassOf(documentType) ||
                        (documentType.GetTypeInfo().IsInterface && t.Type.GetInterfaces().Contains(documentType)) ||
                        t.Type == documentType)
                .Select(GetTypeMartenAlias).Concat(Aliases).ToArray();
        }

        public DocumentMapping Inner { get; }

        public DocumentMapping Parent { get; }

        public string[] Aliases { get; }
        public string Alias { get; set; }


        public DeleteStyle DeleteStyle => Parent.DeleteStyle;

        public IDocumentMapping Root => Parent;
        public Type DocumentType { get; }

        public DbObjectName TableName => Parent.TableName;

        public Type IdType => Parent.IdType;


        private static string GetTypeMartenAlias(Type documentType)
        {
            return GetTypeMartenAlias(new MappedType(documentType));
        }

        private static string GetTypeMartenAlias(MappedType documentType)
        {
            return documentType.Alias ??
                   documentType.Type.GetTypeName()
                       .Replace(".", "_")
                       .SplitCamelCase()
                       .Replace(" ", "_")
                       .ToLowerInvariant();
        }
    }
}
