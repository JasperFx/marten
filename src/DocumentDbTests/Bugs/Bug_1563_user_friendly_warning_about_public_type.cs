using System;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Testing.Harness;
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
    public void good_error_on_non_public_type()
    {
        var expectedMessage =
            $"Requested document type '{typeof(InternalDoc).FullNameInCode()}' must be scoped as 'public'";

        var ex = Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
        {
            var doc = new InternalDoc();
            TheSession.Store(doc);
            TheSession.SaveChanges();
        });

        ex.Message.ShouldContain(expectedMessage);
    }
}
