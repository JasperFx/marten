using System.ComponentModel;
using System.IO;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Util;
using Marten.Internal.Storage;
using Marten.Schema.BulkLoading;

namespace Marten.Internal.CodeGeneration
{
    public class DocumentProvider<T> : IDocumentSourceCode
    {

        public IDocumentStorage<T> QueryOnly { get; set; }
        public IDocumentStorage<T> Lightweight { get; set; }
        public IDocumentStorage<T> IdentityMap { get; set; }
        public IDocumentStorage<T> DirtyTracking { get; set; }
        public IBulkLoader<T> BulkLoader { get; set; }
        internal DocumentOperations Operations { get; set; }
        public GeneratedType QueryOnlyType { get; set; }
        public GeneratedType LightweightType { get; set; }
        public GeneratedType IdentityMapType { get; set; }
        public GeneratedType DirtyTrackingType { get; set; }


        string IDocumentSourceCode.AllSourceCode()
        {
            var writer = new StringWriter();

            foreach (var property in typeof(IDocumentSourceCode).GetProperties())
            {
                var title = property.HasAttribute<DescriptionAttribute>()
                    ? property.GetAttribute<DescriptionAttribute>().Description
                    : property.Name.SplitPascalCase();

                writer.WriteLine("// " + title);
                writer.WriteLine(property.GetValue(this));
                writer.WriteLine();
            }

            return writer.ToString();
        }

        string IDocumentSourceCode.QueryOnlyStorageCode => QueryOnlyType.SourceCode;

        string IDocumentSourceCode.LightweightStorageCode => LightweightType.SourceCode;

        string IDocumentSourceCode.IdentityMapStorageCode => IdentityMapType.SourceCode;

        string IDocumentSourceCode.DirtyTrackingStorageCode => DirtyTrackingType.SourceCode;

        string IDocumentSourceCode.BulkLoaderCode => BulkLoaderType.SourceCode;

        string IDocumentSourceCode.UpsertOperationCode => Operations.Upsert.SourceCode;

        string IDocumentSourceCode.UpdateOperationCode => Operations.Update.SourceCode;

        string IDocumentSourceCode.InsertOperationCode => Operations.Insert.SourceCode;

        string IDocumentSourceCode.QueryOnlySelectorCode => Operations.QueryOnlySelector.SourceCode;

        string IDocumentSourceCode.LightweightSelectorCode => Operations.LightweightSelector.SourceCode;

        string IDocumentSourceCode.IdentityMapSelectorCode => Operations.IdentityMapSelector.SourceCode;

        string IDocumentSourceCode.DirtyCheckingSelectorCode => Operations.DirtyCheckingSelector.SourceCode;
        public GeneratedType BulkLoaderType { get; set; }
        public string SourceCode { get; set; }
    }
}
