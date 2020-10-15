using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Storage.Metadata;

namespace Marten.Storage
{
    internal class VersionColumn: MetadataColumn<Guid>, ISelectableColumn
    {
        public VersionColumn() : base(SchemaConstants.VersionColumn, x => x.CurrentVersion)
        {
            Directive = "NOT NULL default(md5(random()::text || clock_timestamp()::text)::uuid)";
            CanAdd = true;
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
            GeneratedMethod sync, int index,
            DocumentMapping mapping)
        {

            var versionPosition = index;//mapping.IsHierarchy() ? 3 : 2;


            async.Frames.CodeAsync($"var version = await reader.GetFieldValueAsync<System.Guid>({versionPosition}, token);");
            sync.Frames.Code($"var version = reader.GetFieldValue<System.Guid>({versionPosition});");

            if (storageStyle != StorageStyle.QueryOnly)
            {
                // Store it
                sync.Frames.Code("_versions[id] = version;");
                async.Frames.Code("_versions[id] = version;");
            }


            if (Member != null)
            {
                sync.Frames.SetMemberValue(Member, "version", mapping.DocumentType, generatedType);
                async.Frames.SetMemberValue(Member, "version", mapping.DocumentType, generatedType);
            }
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            if (Member != null) return true;

            return storageStyle != StorageStyle.QueryOnly;
        }
    }
}
