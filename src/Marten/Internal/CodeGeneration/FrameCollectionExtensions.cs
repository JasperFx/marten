using System.Threading;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Schema;

namespace Marten.Internal.CodeGeneration
{
    internal static class FrameCollectionExtensions
    {
        public const string DocumentVariableName = "document";

        public static void StoreInIdentityMap(this FramesCollection frames, DocumentMapping mapping)
        {
            frames.Code("_identityMap[id] = document;");
        }

        public static void StoreTracker(this FramesCollection frames)
        {
            frames.Code("StoreTracker({0}, document);", Use.Type<IMartenSession>());
        }

        public static void Deserialize(this FramesCollection frames, IDocumentMapping mapping, int index)
        {
            var documentType = mapping.DocumentType;
            var document = new Variable(documentType, DocumentVariableName);
            if (mapping is DocumentMapping d)
            {
                if (!d.IsHierarchy())
                {
                    frames.Code($@"
{documentType.FullNameInCode()} document;
BLOCK:using (var json = reader.GetTextReader({index}))
    document = _serializer.FromJson<{documentType.FullNameInCode()}>(json);
END
").Creates(document);
                }
                else
                {
                    // Hierarchy path is different
                    frames.Code($@"
{documentType.FullNameInCode()} document;
var typeAlias = reader.GetFieldValue<string>({index + 1});
BLOCK:using (var json = reader.GetTextReader({index}))
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

        public static void DeserializeAsync(this FramesCollection frames, IDocumentMapping mapping, int index)
        {
            var documentType = mapping.DocumentType;
            var document = new Variable(documentType, DocumentVariableName);
            if (mapping is DocumentMapping d)
            {
                if (!d.IsHierarchy())
                {
                    frames.Code($@"
{documentType.FullNameInCode()} document;
BLOCK:using (var json = reader.GetTextReader({index}))
    document = _serializer.FromJson<{documentType.FullNameInCode()}>(json);
END
").Creates(document);
                }
                else
                {
                    frames.CodeAsync($@"
{documentType.FullNameInCode()} document;
var typeAlias = await reader.GetFieldValueAsync<string>({index + 1}, {{0}}).ConfigureAwait(false);
BLOCK:using (var json = reader.GetTextReader({index}))
    document = ({documentType.FullNameInCode()}) _serializer.FromJson(_mapping.TypeFor(typeAlias), json);
END
", Use.Type<CancellationToken>()).Creates(document);
                }

            }


        }



    }
}
