using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Bugs;

public class Bug_1975_bulk_insert_of_nested_guid : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_1975_bulk_insert_of_nested_guid(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task can_bulk_insert()
    {
        StoreOptions(options =>
        {
            options.Schema.For<ParentDoc>().Duplicate(x => x.Child.Id);
        });

        await theStore.BulkInsertDocumentsAsync(new List<ParentDoc>
        {
            new ParentDoc
            {
                Id = Guid.NewGuid(),
                Child = new ParentDoc.Nested(Guid.NewGuid())
            }
        });
    }


    [Fact]
    public void can_bulk_insert_sync()
    {
        StoreOptions(options =>
        {
            options.Schema.For<ParentDoc>().Duplicate(x => x.Child.Id);
        });

        theStore.BulkInsertDocuments(new List<ParentDoc>
        {
            new ParentDoc
            {
                Id = Guid.NewGuid(),
                Child = new ParentDoc.Nested(Guid.NewGuid())
            }
        });
    }
}

public class ParentDoc
{
    public Guid Id { get; set; }

    public Nested Child { get; set; }

    public class Nested
    {
        public Nested(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; set; }
    }
}