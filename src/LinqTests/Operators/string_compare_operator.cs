using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class string_compare_operator: IntegrationContext
{
    [Fact]
    public async Task string_compare_works()
    {
        // Arrange
        var targets = new[]
        {
            new Target { String = "Apple" },
            new Target { String = "Banana" },
            new Target { String = "Cherry" },
            new Target { String = "Durian" }
        };

        theSession.Store(targets);
        await theSession.SaveChangesAsync();

        // Act
        var queryable = theSession.Query<Target>()
            .Where(x => string.Compare(x.String, "Cherry") > 0);

        // Assert
        var expected = targets
            .Where(x => string.Compare(x.String, "Cherry") > 0)
            .Select(x => x.String);

        queryable.Select(x => x.String).ToList().ShouldBe(expected);
    }

    [Fact]
    public async Task string_compare_to_works()
    {
        // Arrange
        var targets = new[]
        {
            new Target { String = "Apple" },
            new Target { String = "Banana" },
            new Target { String = "Cherry" },
            new Target { String = "Durian" }
        };

        theSession.Store(targets);
        await theSession.SaveChangesAsync();

        // Act
        var queryable = theSession.Query<Target>()
            .Where(x => x.String.CompareTo("Banana") > 0);

        // Assert
        var expected = targets
            .Where(x => x.String.CompareTo("Banana") > 0)
            .Select(x => x.String);

        queryable.Select(x => x.String).ToList().ShouldBe(expected);
    }

    [Fact]
    public async Task string_compare_ignore_case_works()
    {
        // Arrange
        var targets = new[]
        {
            new Target { String = "apple" },
            new Target { String = "Banana" },
            new Target { String = "cherry" },
            new Target { String = "Durian" }
        };

        theSession.Store(targets);
        await theSession.SaveChangesAsync();

        // Act
        var queryable = theSession.Query<Target>()
            .Where(x => string.Compare(x.String, "BANANA", StringComparison.OrdinalIgnoreCase) > 0);

        // Assert
        var expected = targets
            .Where(x => string.Compare(x.String, "BANANA", StringComparison.OrdinalIgnoreCase) > 0)
            .Select(x => x.String);

        queryable.Select(x => x.String).ToList().ShouldBe(expected);
    }

    [Fact]
    public async Task string_compare_with_invariant_culture_and_ignore_case_works()
    {
        // Arrange
        var targets = new[]
        {
            new Target { String = "apple" },
            new Target { String = "Banana" },
            new Target { String = "cherry" }
        };

        theSession.Store(targets);
        await theSession.SaveChangesAsync();

        // Act
        var queryable = theSession.Query<Target>()
            .Where(x => string.Compare(x.String, "APPLE", CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0);

        // Assert
        var expected = targets
            .Where(x => string.Compare(x.String, "APPLE", CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0)
            .Select(x => x.String);

        queryable.Select(x => x.String).ToList().ShouldBe(expected);
    }

    [Fact]
    // Test requires the following SQL to be run: create collation "tr-TR" (locale='tr-TR.utf8');
    public async Task string_compare_with_turkish_culture_case_insensitive_behavior()
    {
        // Arrange
        var turkishCulture = CultureInfo.GetCultureInfo("tr-TR");
        var targets = new[]
        {
            new Target { String = "İi" }, // Turkish uppercase İ
            new Target { String = "ıi" }  // Turkish lowercase ı
        };

        theSession.Store(targets);
        await theSession.SaveChangesAsync();

        // Act
        var queryable = theSession.Query<Target>()
            .Where(x => string.Compare(x.String, "ii", turkishCulture, CompareOptions.IgnoreCase) == 0);

        // Assert
        var expected = targets
            .Where(x => string.Compare(x.String, "ii", turkishCulture, CompareOptions.IgnoreCase) == 0)
            .Select(x => x.String);

        queryable.Select(x => x.String).ToList().ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    // Test requires the following SQL to be run: create collation "en-US" (locale='en-US.utf8');
    public async Task string_compare_with_english_culture_ignore_case_works()
    {
        // Arrange
        var englishCulture = CultureInfo.GetCultureInfo("en-US");
        var targets = new[]
        {
            new Target { String = "Apple" },
            new Target { String = "apple" }
        };

        theSession.Store(targets);
        await theSession.SaveChangesAsync();

        // Act
        var queryable = theSession.Query<Target>()
            .Where(x => string.Compare(x.String, "APPLE", true, englishCulture) == 0);

        // Assert
        var expected = targets
            .Where(x => string.Compare(x.String, "APPLE", true, englishCulture) == 0)
            .Select(x => x.String);

        queryable.Select(x => x.String).ToList().ShouldBe(expected);
    }

    [Fact]
    // Test requires the following SQL to be run: create collation "de-DE" (locale='de-DE.utf8');
    public async Task string_compare_with_german_culture_and_compare_options_works()
    {
        // Arrange
        var germanCulture = CultureInfo.GetCultureInfo("de-DE");
        var targets = new[]
        {
            new Target { String = "Straße" },
            new Target { String = "Strasse" }
        };

        theSession.Store(targets);
        await theSession.SaveChangesAsync();

        // Act
        var queryable = theSession.Query<Target>()
            .Where(x => string.Compare(x.String, "Strasse", germanCulture, CompareOptions.IgnoreSymbols) == 0);

        // Assert
        var expected = targets
            .Where(x => string.Compare(x.String, "Strasse", germanCulture, CompareOptions.IgnoreSymbols) == 0)
            .Select(x => x.String);

        queryable.Select(x => x.String).ToList().ShouldBe(expected);
    }

    [Fact]
    // Test requires the following SQL to be run: create collation "da-DK" (locale='da-DK.utf8');
    public async Task string_compare_to_with_culture_works()
    {
        // Arrange
        var danishCulture = CultureInfo.GetCultureInfo("da-DK");
        var targets = new[]
        {
            new Target { String = "Apple" },
            new Target { String = "Æble" }
        };

        theSession.Store(targets);
        await theSession.SaveChangesAsync();

        // Act
        var queryable = theSession.Query<Target>()
            .Where(x => string.Compare(x.String, "Apple", danishCulture, CompareOptions.None) > 0);

        // Assert
        var expected = targets
            .Where(x => string.Compare(x.String, "Apple", danishCulture, CompareOptions.None) > 0)
            .Select(x => x.String);

        queryable.Select(x => x.String).ToList().ShouldBe(expected);
    }

    public string_compare_operator(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
