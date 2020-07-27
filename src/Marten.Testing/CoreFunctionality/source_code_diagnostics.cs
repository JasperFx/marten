using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.CoreFunctionality
{
    public class source_code_diagnostics : IntegrationContext
    {
        private readonly ITestOutputHelper _output;
        private IDocumentSourceCode theSourceCode;

        [UseOptimisticConcurrency, SoftDeleted]
        public class OptimisticVersionedDoc
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        public source_code_diagnostics(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
            theSourceCode = theStore.Advanced.SourceCodeForDocumentType(typeof(OptimisticVersionedDoc));
        }

        [Fact]
        public void can_access_the_source_code_for_a_document_type()
        {
            theSourceCode.ShouldNotBeNull();
        }

        public static IEnumerable<object[]> Properties()
        {
            foreach (var property in typeof(IDocumentSourceCode).GetProperties())
            {
                yield return new object[] {property};
            }
        }

        [Theory]
        [MemberData(nameof(Properties))]
        public void can_retrieve_source_code_for_generated_type(PropertyInfo property)
        {
            var code = (string)property.GetValue(theSourceCode);
            code.ShouldNotBeNull();

            theSourceCode.AllSourceCode().ShouldContain(code);
        }
    }
}
