using System;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_1563_user_friendly_warning_about_public_type: BugIntegrationContext
{
    [DocumentAlias("internal_doc")]
    internal class InternalDoc
    {
        public Guid Id { get; set; }
    }

    [Fact]
    public async Task good_error_on_non_public_type()
    {
        var expectedMessage =
            $"Requested document type '{typeof(InternalDoc).FullNameInCode()}' must be scoped as 'public'";

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            var doc = new InternalDoc();
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        });

        ex.Message.ShouldContain(expectedMessage, Case.Insensitive);
    }
}
