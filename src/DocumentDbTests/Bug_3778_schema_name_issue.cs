using System;
using System.Threading.Tasks;
using JasperFx;
using Marten.Testing;
using Marten.Testing.Harness;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_3778_schema_name__ending_with_d_issue: OneOffConfigurationsContext
{
    [Theory]
    [InlineData("pprd")]
    [InlineData("d")]
    public async Task TestSchemaNameEndingWith_d_In_Index(string schemaName)
    {
        StoreOptions(options =>
        {
            options.Connection(ConnectionSource.ConnectionString);
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.DatabaseSchemaName = schemaName;
            options.Schema.For<User3778>()
                .Index(d => d.D1)
                .Index(d => d.D2);
        });

        await theStore.EnsureStorageExistsAsync(typeof(User3778));
    }

    [Theory]
    [InlineData("pprd")]
    [InlineData("d")]
    public async Task TestSchemaNameEndingWith_d_In_NGramIndex(string schemaName)
    {
        StoreOptions(options =>
        {
            options.Connection(ConnectionSource.ConnectionString);
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.DatabaseSchemaName = schemaName;
            options.Schema.For<User3778>()
                .NgramIndex(d => d.Name);
        });

        await theStore.EnsureStorageExistsAsync(typeof(User3778));
    }

    [Theory]
    [InlineData("pprd")]
    [InlineData("d")]
    public async Task TestSchemaNameEndingWith_d_In_FulltextIndex(string schemaName)
    {
        StoreOptions(options =>
        {
            options.Connection(ConnectionSource.ConnectionString);
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.DatabaseSchemaName = schemaName;
            options.Schema.For<User3778>()
                .FullTextIndex(d => d.Name);
        });

        await theStore.EnsureStorageExistsAsync(typeof(User3778));
    }

    [Theory]
    [InlineData("pprd")]
    [InlineData("d")]
    public async Task TestSchemaNameEndingWith_d_In_DuplicateFields(string schemaName)
    {
        StoreOptions(options =>
        {
            options.Connection(ConnectionSource.ConnectionString);
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.DatabaseSchemaName = schemaName;
            options.Schema.For<User3778>()
                .Duplicate(d => d.Manager.Name, configure: idx =>
                {
                    idx.Name = "idx_manager_name";
                    idx.Method = IndexMethod.hash;
                });
        });

        await theStore.EnsureStorageExistsAsync(typeof(User3778));
    }
}

public record User3778(Guid Id, string Name, DateTimeOffset D1, DateOnly D2, User3778 Manager);
