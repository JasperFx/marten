using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Weasel.Postgresql;
using Marten.Util;
using Weasel.Core;

namespace Marten.Schema
{
    /// <summary>
    /// IDocumentMapping implementation for a document type that's a subclass of a parent type, and
    /// maps to the parent storage
    /// </summary>
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

        IDocumentMapping IDocumentMapping.Root => Parent;
        public Type DocumentType { get; }

        public DbObjectName TableName => Parent.TableName;

        public Type IdType => Parent.IdType;


        private static string GetTypeMartenAlias(Type documentType)
        {
            return GetTypeMartenAlias(new MappedType(documentType));
        }

        private static string GetTypeMartenAlias(MappedType documentType)
        {
            var typeName = documentType.Type.Name;

            if (documentType.Type.GetTypeInfo().IsGenericType)
                typeName = documentType.Type.GetPrettyName();
            return documentType.Alias ??
                   (documentType.Type.IsNested
                       ? $"{documentType.Type.DeclaringType.Name}.{typeName}"
                       : typeName)
                       .Replace(".", "_")
                       .SplitCamelCase()
                       .Replace(" ", "_")
                       .ToLowerInvariant();
        }


    }
}
