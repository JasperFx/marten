using System;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1563_user_friendly_warning_about_public_type : BugIntegrationContext
    {
        [DocumentAlias("internal_doc")]
        internal class InternalDoc
        {
            public Guid Id { get; set; }
        }

        [Fact]
        public void good_error_on_non_public_type()
        {
            var expectedMessage = "Requested document type 'Marten.Testing.Bugs.Bug_1563_user_friendly_warning_about_public_type.InternalDoc' must be either scoped as 'public' or the assembly holding it must use the InternalsVisibleToAttribute pointing to 'Marten.Generated'";

            var ex = Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                var doc = new InternalDoc();
                theSession.Store(doc);
                theSession.SaveChanges();
            });

            ex.Message.ShouldContain(expectedMessage);


        }
    }
}
