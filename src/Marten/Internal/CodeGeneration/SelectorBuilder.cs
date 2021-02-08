using System;
using System.Linq;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Linq.Selectors;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Internal.CodeGeneration
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
            var typeName = $"{_style}{_mapping.DocumentType.Name.Sanitize()}Selector";


            var baseType = determineBaseType();

            var type = assembly.AddType(typeName, baseType);
            var interfaceType = typeof(ISelector<>).MakeGenericType(_mapping.DocumentType);
            type.Implements(interfaceType);

            var sync = type.MethodFor("Resolve");
            var async = type.MethodFor("ResolveAsync");

            var table = _mapping.Schema.Table;
            var columns = table.SelectColumns(_style);

            for (var i = 0; i < columns.Length; i++)
            {
                // TODO -- use the memo-ized table
                columns[i].GenerateCode(_style, type, async, sync, i, _mapping);
            }

            generateIdentityMapAndTrackingCode(sync, async, _style);

            sync.Frames.Return(_mapping.DocumentType);
            if (async.Frames.Any(x => x.IsAsync))
            {
                async.Frames.Return(_mapping.DocumentType);
            }
            else
            {
                async.Frames.Code($"return {typeof(Task).FullNameInCode()}.FromResult(document);");
            }

            return type;
        }

        private void generateIdentityMapAndTrackingCode(GeneratedMethod sync, GeneratedMethod @async, StorageStyle storageStyle)
        {
            if (storageStyle == StorageStyle.QueryOnly) return;

            sync.Frames.MarkAsLoaded();
            async.Frames.MarkAsLoaded();

            if (storageStyle == StorageStyle.Lightweight) return;

            sync.Frames.StoreInIdentityMap(_mapping);
            async.Frames.StoreInIdentityMap(_mapping);

            if (storageStyle == StorageStyle.DirtyTracking)
            {
                sync.Frames.StoreTracker();
                async.Frames.StoreTracker();
            }
        }

        private Type determineBaseType()
        {
            switch (_style)
            {
                case StorageStyle.QueryOnly:
                    return typeof(DocumentSelectorWithOnlySerializer);

                case StorageStyle.IdentityMap:
                    return typeof(DocumentSelectorWithIdentityMap<,>)
                        .MakeGenericType(_mapping.DocumentType, _mapping.IdType);

                case StorageStyle.Lightweight:
                    return typeof(DocumentSelectorWithVersions<,>)
                        .MakeGenericType(_mapping.DocumentType, _mapping.IdType);

                case StorageStyle.DirtyTracking:
                    return typeof(DocumentSelectorWithDirtyChecking<,>)
                        .MakeGenericType(_mapping.DocumentType, _mapping.IdType);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
