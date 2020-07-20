using System.Threading;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Schema;

namespace Marten.Internal.CodeGeneration
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

        public static void StoreInIdentityMap(this FramesCollection frames, DocumentMapping mapping)
        {
            frames.Code("_identityMap[id] = document;");
        }

        public static void StoreTracker(this FramesCollection frames)
        {
            frames.Code("StoreTracker({0}, document);", Use.Type<IMartenSession>());
        }

        public static void CheckExistingFirst(this FramesCollection frames)
        {
            frames.Code("if (_identityMap.TryGetValue(id, out var existing)) return existing;");
        }

        public static void Deserialize(this FramesCollection frames, IDocumentMapping mapping)
        {
            var documentType = mapping.DocumentType;
            var document = new Variable(documentType, DocumentVariableName);
            if (mapping is DocumentMapping d)
            {
                if (!d.IsHierarchy())
                {
                    frames.Code($@"
{documentType.FullNameInCode()} document;
BLOCK:using (var json = reader.GetTextReader(0))
    document = _serializer.FromJson<{documentType.FullNameInCode()}>(json);
END
").Creates(document);
                }
                else
                {
                    // Hierarchy path is different
                    frames.Code($@"
{documentType.FullNameInCode()} document;
var typeAlias = reader.GetFieldValue<string>(2);
BLOCK:using (var json = reader.GetTextReader(0))
    document = ({documentType.FullNameInCode()}) _serializer.FromJson(_mapping.TypeFor(typeAlias), json);
END
").Creates(document);
                }
            }
        }

        public static void MarkAsLoaded(this FramesCollection frames)
        {
            frames.Code($"{{0}}.{nameof(IMartenSession.MarkAsDocumentLoaded)}(id, document);", Use.Type<IMartenSession>());
        }

        public static void DeserializeAsync(this FramesCollection frames, IDocumentMapping mapping)
        {
            var documentType = mapping.DocumentType;
            var document = new Variable(documentType, DocumentVariableName);
            if (mapping is DocumentMapping d)
            {
                if (!d.IsHierarchy())
                {
                    frames.Code($@"
{documentType.FullNameInCode()} document;
BLOCK:using (var json = reader.GetTextReader(0))
    document = _serializer.FromJson<{documentType.FullNameInCode()}>(json);
END
").Creates(document);
                }
                else
                {
                    frames.CodeAsync($@"
{documentType.FullNameInCode()} document;
var typeAlias = await reader.GetFieldValueAsync<string>(2, {{0}}).ConfigureAwait(false);
BLOCK:using (var json = reader.GetTextReader(0))
    document = ({documentType.FullNameInCode()}) _serializer.FromJson(_mapping.TypeFor(typeAlias), json);
END
", Use.Type<CancellationToken>()).Creates(document);
                }

            }


        }


        public static void StoreVersion(this FramesCollection frames, bool isAsync, DocumentMapping mapping,
            int position)
        {
            // Get the version
            if (isAsync)
            {
                frames.CodeAsync($"var version = await reader.GetFieldValueAsync<System.Guid>({position}, token);");
            }
            else
            {
                frames.Code($"var version = reader.GetFieldValue<System.Guid>({position});");
            }

            // Store it
            frames.Code("_versions[id] = version;");

            // Set on document
            if (mapping.VersionMember != null)
            {
                frames.Code($"document.{mapping.VersionMember.Name} = version;");
            }
        }
    }
}
