using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Schema;

namespace Marten.V4Internals
{
    public static class FrameCollectionExtensions
    {
        public const string DocumentVariableName = "document";

        public static void GetId(this FramesCollection frames, DocumentMapping mapping)
        {
            frames.Code($"var id = reader.GetFieldValue<{mapping.IdType.FullNameInCode()}>(1);");
        }

        public static void GetIdAsync(this FramesCollection frames, DocumentMapping mapping)
        {
            frames.CodeAsync($"var id = await reader.GetFieldValueAsync<{mapping.IdType.FullNameInCode()}>(1, token);");
        }

        public static void CheckIdentityMap(this FramesCollection frames, DocumentMapping mapping)
        {
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(mapping.IdType, mapping.DocumentType);

            var dict = new Use(dictionaryType);

            frames.Code(@"
BLOCK:if ({0}.TryGetValue(id, out var existing))
return existing;
END
", dict);
        }

        public static void StoreInIdentityMap(this FramesCollection frames, DocumentMapping mapping)
        {
            frames.Code("_identityMap[id] = document;");
        }

        public static void Deserialize(this FramesCollection frames, Type documentType)
        {
            var document = new Variable(documentType, DocumentVariableName);
            frames.Code($@"
{documentType.FullNameInCode()} document;
BLOCK:using (var json = reader.GetTextReader(0))
    document = _serializer.FromJson<{documentType.FullNameInCode()}>(json);
END
").Creates(document);
        }

        public static void StoreVersion(this FramesCollection frames, bool isAsync, DocumentMapping mapping,
            int position)
        {
            // Get the version
            if (isAsync)
                frames.CodeAsync($"var version = await reader.GetFieldValueAsync<System.Guid>({position}, token);");
            else
                frames.Code($"var version = reader.GetFieldValue<System.Guid>({position});");

            // Store it
            frames.Code("_versions[id] = version;");

            // Set on document
            if (mapping.VersionMember != null) frames.Code($"document.{mapping.VersionMember.Name} = version;");
        }
    }
}
