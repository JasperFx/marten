using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.PLv8.Patching;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.PLv8.Testing.Patching
{
    public class Bug_2170_need_to_compile_the_linq_statement_with_sub_queries : BugIntegrationContext
    {

        [Fact]
        public async Task do_not_blow_up()
        {
            StoreOptions(opts => opts.UseJavascriptTransformsAndPatching());

            theSession.Patch<PatchUser>(u => u.ExternalReferences.References.Any(r => r.Reference == "foo"))
                .Set(u => u.PhotoUrl, "some url");

            await theSession.SaveChangesAsync();
        }
    }

    public class PatchUser
    {
        public Guid Id { get; set; }
        public string PhotoUrl { get; set; }

        public ExternalReference ExternalReferences { get; set; } = new ExternalReference();
    }

    public class ExternalReference
    {
        public List<ReferenceItem> References { get; set; } = new List<ReferenceItem>();
    }

    public class ReferenceItem
    {
        public string Reference { get; set; }
    }
}
