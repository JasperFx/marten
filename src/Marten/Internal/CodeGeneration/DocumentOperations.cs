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
            DeleteById = new DeleteByIdBuilder(mapping).Build(assembly);
            DeleteByWhere = new DeleteWhereBuilder(mapping).Build(assembly);
            Upsert = new DocumentFunctionOperationBuilder(mapping, new UpsertFunction(mapping), OperationRole.Upsert, options)
                    .BuildType(assembly);
            Insert = new DocumentFunctionOperationBuilder(mapping, new InsertFunction(mapping), OperationRole.Insert, options)
                    .BuildType(assembly);
            Update = new DocumentFunctionOperationBuilder(mapping, new UpdateFunction(mapping), OperationRole.Update, options)
                    .BuildType(assembly);





            QueryOnlySelector = new SelectorBuilder(mapping, StorageStyle.QueryOnly).BuildType(assembly);
            LightweightSelector = new SelectorBuilder(mapping, StorageStyle.Lightweight).BuildType(assembly);
            IdentityMapSelector = new SelectorBuilder(mapping, StorageStyle.IdentityMap).BuildType(assembly);
            DirtyCheckingSelector = new SelectorBuilder(mapping, StorageStyle.DirtyTracking).BuildType(assembly);

            if (mapping.UseOptimisticConcurrency)
            {
                Overwrite = new DocumentFunctionOperationBuilder(mapping, new OverwriteFunction(mapping), OperationRole.Update, options)
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
