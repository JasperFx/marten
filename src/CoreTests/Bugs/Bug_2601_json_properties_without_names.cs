using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_2601_json_properties_without_names: BugIntegrationContext
{
    [Fact]
    public async Task document_with_json_properties_without_names_should_still_be_queryable()
    {
        StoreOptions(opts => opts.RegisterDocumentType<DocWithJsonProperties>());

        var newDoc = new DocWithJsonProperties {Id = CombGuidIdGeneration.NewGuid(), Name = "foo"};
        theSession.Store(newDoc);
        await theSession.SaveChangesAsync();

        // expected query
        //  where (d.data ->> 'Name' = $1)

        // actual query
        //  where (d.data ->> '' = $1)
        var lookup = await theSession.Query<DocWithJsonProperties>().Where(x => x.Name == "foo").FirstOrDefaultAsync();
        Assert.NotNull(lookup);
        Assert.Equal(newDoc.Id, lookup.Id);
    }

    public class DocWithJsonProperties
    {
        public Guid Id { get; set; }

        // Lack of "propertyName" here
        [JsonProperty]
        public string Name { get; set; }
    }
}
