using LamarCodeGeneration;
using Marten.Schema;
using Marten.Storage;

namespace Marten.V4Internals
{
    internal class DocumentOperations
    {
        public DocumentOperations(GeneratedAssembly assembly, DocumentMapping mapping)
        {
            DeleteById = new DeleteByIdBuilder(mapping).Build(assembly);
            DeleteByWhere = new DeleteWhereBuilder(mapping).Build(assembly);
            Upsert = new DocumentFunctionOperationBuilder(mapping, new UpsertFunction(mapping), StorageRole.Upsert)
                    .BuildType(assembly);
            Insert = new DocumentFunctionOperationBuilder(mapping, new InsertFunction(mapping), StorageRole.Insert)
                    .BuildType(assembly);
            Update = new DocumentFunctionOperationBuilder(mapping, new UpdateFunction(mapping), StorageRole.Update)
                    .BuildType(assembly);
            QueryOnlySelector = new SelectorBuilder(mapping, StorageStyle.QueryOnly).BuildType(assembly);
            LightweightSelector = new SelectorBuilder(mapping, StorageStyle.Lightweight).BuildType(assembly);
            IdentityMapSelector = new SelectorBuilder(mapping, StorageStyle.IdentityMap).BuildType(assembly);
            DirtyCheckingSelector = new SelectorBuilder(mapping, StorageStyle.DirtyTracking).BuildType(assembly);

            if (mapping.UseOptimisticConcurrency)
            {
                Overwrite = new DocumentFunctionOperationBuilder(mapping, new OverwriteFunction(mapping), StorageRole.Update)
                        .BuildType(assembly);
            }
        }

        public GeneratedType DirtyCheckingSelector { get; set; }

        public GeneratedType Upsert { get; set; }
        public GeneratedType Insert { get; set; }
        public GeneratedType Update { get; set; }
        public GeneratedType Overwrite { get; set; }

        public GeneratedType DeleteById { get; set; }
        public GeneratedType DeleteByWhere { get; set; }
        public GeneratedType QueryOnlySelector { get; set; }
        public GeneratedType LightweightSelector { get; set; }
        public GeneratedType IdentityMapSelector { get; set; }
    }
}
