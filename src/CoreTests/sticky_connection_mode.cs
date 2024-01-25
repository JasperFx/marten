using System.Data;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace CoreTests;

public class sticky_connection_mode : OneOffConfigurationsContext
{
    [Fact]
    public async Task use_sticky_connection()
    {
        StoreOptions(opts => opts.UseStickyConnectionLifetimes = true);

        var connection = theSession.Connection;
        connection.ShouldNotBeNull();
        var target = Target.Random();

        theSession.Store(target);
        theSession.Connection.ShouldBeTheSameAs(connection);

        await theSession.SaveChangesAsync();
    }

    public static void use_sticky_connections()
    {
        #region sample_use_sticky_connection_lifetimes

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Opt into V6 and earlier "sticky" connection
            // handling
            opts.UseStickyConnectionLifetimes = true;
        });

        #endregion
    }
}
