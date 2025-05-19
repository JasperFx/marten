using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_648_defensive_programming_checks_on_bad_id_type: IntegrationContext
{
    [Fact]
    public async Task try_to_load_a_guid_identified_type_with_wrong_type()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () =>
        {
            await theSession.LoadAsync<Target>(111);
        });
    }

    [Fact]
    public Task try_to_load_a_guid_identified_type_with_wrong_type_async()
    {
        return Should.ThrowAsync<DocumentIdTypeMismatchException>(async () =>
        {
            await theSession.LoadAsync<Target>(111);
        });
    }


    [Fact]
    public Task try_to_loadmany_a_guid_identified_type_with_wrong_type_async()
    {
        return Should.ThrowAsync<DocumentIdTypeMismatchException>(async () =>
        {
            await theSession.LoadManyAsync<Target>(111, 222);
        });
    }

    public Bug_648_defensive_programming_checks_on_bad_id_type(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
