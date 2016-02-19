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

            var generatedCode = new FileSystem().ReadStringFromFile("storage.cs");

            generatedCode.ShouldContain("public class UserStorage : IDocumentStorage, IBulkLoader<User>, IdAssignment<User>, IResolver<User>");
            generatedCode.ShouldContain("public class CompanyStorage : IDocumentStorage, IBulkLoader<Company>, IdAssignment<Company>, IResolver<Company>");
            generatedCode.ShouldContain("public class IssueStorage : IDocumentStorage, IBulkLoader<Issue>, IdAssignment<Issue>, IResolver<Issue>");
        }
    }
}