using System;
using System.Diagnostics;
using Baseline;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Codegen
{
    public class dumping_the_complete_source_code_Tests
    {
        [Fact]
        public void write_code_for_all_the_known_document_types()
        {
            // SAMPLE: exporting_the_storage_code
            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();
            }))
            {
                store.Advanced.WriteStorageCode("storage.cs");
            }
            // ENDSAMPLE

            var generatedCode = new FileSystem().ReadStringFromFile("storage.cs");

            generatedCode.ShouldContain("public class UserStorage : Resolver<User>, IDocumentStorage, IBulkLoader<User>, IdAssignment<User>, IResolver<User>");
            generatedCode.ShouldContain("public class CompanyStorage : Resolver<Company>, IDocumentStorage, IBulkLoader<Company>, IdAssignment<Company>, IResolver<Company>");
            generatedCode.ShouldContain("public class IssueStorage : Resolver<Issue>, IDocumentStorage, IBulkLoader<Issue>, IdAssignment<Issue>, IResolver<Issue>");
        }
    }
}