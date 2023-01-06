using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Pagination;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2433_include_with_take: IntegrationContext
{
    public Bug_2433_include_with_take(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Theory]
    [InlineData(false, 1, 1)]
    [InlineData(false, 1, 2)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 1, 2)]
    public async Task should_include_proper_related_documents(bool useOrderBy, int pageNumber, int pageSize)
    {
        // Arrange
        var countries = new[]
        {
            new Country(new Guid("81f74a51-d6df-4d01-b060-571d557b5301"), "Country 01"),
            new Country(new Guid("676f4513-c148-4dfe-b67e-279c7d382572"), "Country 02"),
            new Country(new Guid("b8a5e6c0-a191-4599-b52d-4347e4d991f7"), "Country 03")
        };

        var customers = new[]
        {
            new Customer(new Guid("f651db49-f08d-4446-881c-4f2791ecfb36"), "Country 01", countries[0].Id),
            new Customer(new Guid("17b93f98-2424-40aa-a460-8912d479690b"), "Country 02", countries[1].Id),
            new Customer(new Guid("ed2a05e4-1bb9-4bdf-9b32-01808b3fdc50"), "Country 03", countries[2].Id)
        };

        await theStore.BulkInsertAsync(countries);
        await theStore.BulkInsertAsync(customers);
        await theSession.SaveChangesAsync();

        // Act
        var includedCountries = new Dictionary<Guid, Country>();

        await using var querySession = theStore.QuerySession();
        IQueryable<Customer> query = querySession.Query<Customer>()
            .Include(x => x.CountryId!, includedCountries);

        if (useOrderBy)
        {
            query = query
                .OrderByDescending(x => x.Name);
        }

        var loadedCustomers = await query
            .ToPagedListAsync(pageNumber, pageSize);

        // Assert
        var loadedCountryIds = loadedCustomers
            .Select(x => x.CountryId)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var includedCountryIds = includedCountries.Values
            .Select(x => x.Id)
            .OrderBy(x => x)
            .ToArray();

        includedCountryIds.ShouldBe(loadedCountryIds);
    }

    public record Customer(Guid Id, string Name, Guid CountryId): Entity(Id);

    public record Country(Guid Id, string Name): Entity(Id);

    public abstract record Entity(Guid Id);
}
