using System;
using System.Threading.Tasks;
using JasperFx;
using Marten.Testing;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_3778_schema_name_issue: OneOffConfigurationsContext
{
    private string Schema = "pprd";

    [Fact]
    public async Task TestSchemaNameEndingWith_d_BeingCutOff_In_Index()
    {
        StoreOptions(options =>
        {
            options.Connection(ConnectionSource.ConnectionString);
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.DatabaseSchemaName = Schema;
            options.Schema.For<User3778>()
                .Index(d => d.Created);
        });

        await theStore.EnsureStorageExistsAsync(typeof(User3778));
    }
}

public record User3778(Guid Id, string Name, DateTimeOffset Created);
