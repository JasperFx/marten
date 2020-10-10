using LamarCodeGeneration;
using Marten.Internal.Operations;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Internal.CodeGeneration
{
    internal class DocumentOperations
    {
        public DocumentOperations(GeneratedAssembly assembly, DocumentMapping mapping, StoreOptions options)
        {
            Upsert = new DocumentFunctionOperationBuilder(mapping, mapping.Schema.Upsert, OperationRole.Upsert, options)
                    .BuildType(assembly);
            Insert = new DocumentFunctionOperationBuilder(mapping, mapping.Schema.Insert, OperationRole.Insert, options)
                    .BuildType(assembly);
            Update = new DocumentFunctionOperationBuilder(mapping, mapping.Schema.Update, OperationRole.Update, options)
                    .BuildType(assembly);





            QueryOnlySelector = new SelectorBuilder(mapping, StorageStyle.QueryOnly).BuildType(assembly);
            LightweightSelector = new SelectorBuilder(mapping, StorageStyle.Lightweight).BuildType(assembly);
            IdentityMapSelector = new SelectorBuilder(mapping, StorageStyle.IdentityMap).BuildType(assembly);
            DirtyCheckingSelector = new SelectorBuilder(mapping, StorageStyle.DirtyTracking).BuildType(assembly);

            if (mapping.UseOptimisticConcurrency)
            {
                Overwrite = new DocumentFunctionOperationBuilder(mapping, mapping.Schema.Overwrite, OperationRole.Update, options)
                        .BuildType(assembly);
            }
        }



        public GeneratedType Upsert { get; set; }
        public GeneratedType Insert { get; set; }
        public GeneratedType Update { get; set; }
        public GeneratedType Overwrite { get; set; }

        public GeneratedType QueryOnlySelector { get; set; }
        public GeneratedType LightweightSelector { get; set; }
        public GeneratedType IdentityMapSelector { get; set; }

        public GeneratedType DirtyCheckingSelector { get; set; }

    }
}
