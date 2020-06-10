using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Util;

namespace Marten.V4Internals
{
    public class SelectorBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly StorageStyle _style;

        public SelectorBuilder(DocumentMapping mapping, StorageStyle style)
        {
            _mapping = mapping;
            _style = style;
        }

        public GeneratedType BuildType(GeneratedAssembly assembly)
        {
            var typeName = $"{_style}{_mapping.DocumentType.Name}Selector";


            var baseType = determineBaseType();

            var type = assembly.AddType(typeName, baseType);
            var interfaceType = typeof(ISelector<>).MakeGenericType(_mapping.DocumentType);
            type.Implements(interfaceType);

            var sync = type.MethodFor("Resolve");
            var async = type.MethodFor("ResolveAsync");

            var versionPosition = _mapping.IsHierarchy() ? 3 : 2;

            switch (_style)
            {
                case StorageStyle.QueryOnly:
                    sync.Frames.Deserialize(_mapping.DocumentType);
                    async.Frames.Deserialize(_mapping.DocumentType);
                    break;

                case StorageStyle.IdentityMap:

                    sync.Frames.Deserialize(_mapping.DocumentType);
                    async.Frames.Deserialize(_mapping.DocumentType);

                    sync.Frames.GetId(_mapping);
                    async.Frames.GetIdAsync(_mapping);


                    sync.Frames.StoreVersion(false, _mapping, versionPosition);
                    async.Frames.StoreVersion(true, _mapping, versionPosition);

                    sync.Frames.StoreInIdentityMap(_mapping);
                    async.Frames.StoreInIdentityMap(_mapping);

                    break;

                case StorageStyle.Lightweight:

                    sync.Frames.Deserialize(_mapping.DocumentType);
                    async.Frames.Deserialize(_mapping.DocumentType);

                    sync.Frames.GetId(_mapping);
                    async.Frames.GetIdAsync(_mapping);

                    sync.Frames.StoreVersion(false, _mapping, versionPosition);
                    async.Frames.StoreVersion(true, _mapping, versionPosition);

                    break;
                default:
                    throw new NotImplementedException("Not yet supporting dirty checking");
            }


            sync.Frames.Return(_mapping.DocumentType);
            if (_style == StorageStyle.QueryOnly)
            {
                async.Frames.Code("return Task.FromResult(document);");
            }
            else
            {
                async.Frames.Return(_mapping.DocumentType);
            }

            return type;
        }

        private Type determineBaseType()
        {
            switch (_style)
            {
                case StorageStyle.QueryOnly:
                    return typeof(DocumentSelectorWithOnlySerializer);
                case StorageStyle.IdentityMap:
                    return typeof(DocumentSelectorWithIdentityMap<,>).MakeGenericType(_mapping.DocumentType,
                        _mapping.IdType);
                case StorageStyle.Lightweight:
                    return typeof(DocumentSelectorWithVersions<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);

                default:
                    throw new NotImplementedException("Not yet supporting dirty checking");
            }
        }
    }


    public abstract class DocumentSelectorWithIdentityMap<T, TId>
    {
        protected readonly ISerializer _serializer;
        protected readonly Dictionary<TId, T> _identityMap;
        protected readonly Dictionary<TId, Guid> _versions;

        public DocumentSelectorWithIdentityMap(IMartenSession session)
        {
            _serializer = session.Serializer;
            _versions = session.Versions.ForType<T, TId>();
            if (session.ItemMap.TryGetValue(typeof(T), out var dict))
            {
                _identityMap = (Dictionary<TId, T>)dict;
            }
            else
            {
                _identityMap = new Dictionary<TId, T>();
                session.ItemMap[typeof(T)] = _identityMap;
            }
        }
    }

    public abstract class DocumentSelectorWithVersions<T, TId>
    {
        protected readonly ISerializer _serializer;
        protected readonly Dictionary<TId, Guid> _versions;

        public DocumentSelectorWithVersions(IMartenSession session)
        {
            _serializer = session.Serializer;
            _versions = session.Versions.ForType<T, TId>();
        }
    }

    public abstract class DocumentSelectorWithOnlySerializer
    {
        protected readonly ISerializer _serializer;

        public DocumentSelectorWithOnlySerializer(IMartenSession session)
        {
            _serializer = session.Serializer;
        }
    }
}
