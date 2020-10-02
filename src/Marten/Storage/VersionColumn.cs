using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class VersionColumn: MetadataColumn, ISelectableColumn
    {
        public VersionColumn() : base(DocumentMapping.VersionColumn, "uuid")
        {
            Directive = "NOT NULL default(md5(random()::text || clock_timestamp()::text)::uuid)";
            CanAdd = true;
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
            GeneratedMethod sync, int index,
            DocumentMapping mapping)
        {
            if (storageStyle == StorageStyle.QueryOnly)
            {
                if (mapping.VersionMember != null)
                {

                }

                return;
            }

            var versionPosition = mapping.IsHierarchy() ? 3 : 2;


            async.Frames.CodeAsync($"var version = await reader.GetFieldValueAsync<System.Guid>({versionPosition}, token);");
            sync.Frames.Code($"var version = reader.GetFieldValue<System.Guid>({versionPosition});");

            // Store it
            sync.Frames.Code("_versions[id] = version;");
            async.Frames.Code("_versions[id] = version;");

            // TODO -- this needs to happen on QueryOnly as well!!!!!
            // Set on document
            if (mapping.VersionMember != null)
            {
                sync.Frames.SetMemberValue(mapping.VersionMember, "version", mapping.DocumentType, generatedType);
                async.Frames.SetMemberValue(mapping.VersionMember, "version", mapping.DocumentType, generatedType);
            }
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            if (mapping.VersionMember != null) return true;

            return storageStyle != StorageStyle.QueryOnly;
        }

        public override async Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token)
        {
            metadata.CurrentVersion = await reader.GetFieldValueAsync<Guid>(index, token);
        }

        public override void Apply(DocumentMetadata metadata, int index, DbDataReader reader)
        {
            metadata.CurrentVersion = reader.GetFieldValue<Guid>(index);
        }
    }
}
