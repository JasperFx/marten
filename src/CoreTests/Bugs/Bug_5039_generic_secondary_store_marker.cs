using System;
using System.Threading.Tasks;
using Marten;
using Marten.Internal;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

/// <summary>
/// Regression test for #5039: registering a secondary store with a <em>generic</em> marker
/// interface threw <see cref="UriFormatException"/> because the closed generic CLR type name
/// contains a backtick + arity (e.g. <c>IMartenStoreMarker`1</c>), which is not a valid URI
/// hostname when composing the <c>marten://</c> subject in <c>SecondaryStoreConfig.Build</c>.
/// </summary>
public class Bug_5039_generic_secondary_store_marker
{
    public sealed class MyContext;
    public sealed class OtherContext;

    public interface IMartenStoreMarker<TContext> : IDocumentStore;

    [Fact]
    public void sanitized_uri_strips_backtick_and_includes_generic_argument()
    {
        var subject = SecondaryStoreConfig<IMartenStoreMarker<MyContext>>
            .SanitizeForUri(typeof(IMartenStoreMarker<MyContext>));

        subject.ShouldNotContain("`");

        // The result must compose into a valid URI
        var uri = new Uri("marten://" + subject);
        uri.Host.ShouldBe("imartenstoremarker-mycontext");

        // Distinct closed generics must map to distinct subjects
        var other = SecondaryStoreConfig<IMartenStoreMarker<OtherContext>>
            .SanitizeForUri(typeof(IMartenStoreMarker<OtherContext>));
        other.ShouldNotBe(subject);
    }

    [Fact]
    public async Task can_register_and_resolve_generic_marker_store()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "bug5039_primary";
                });

                // This threw UriFormatException before the fix
                services.AddMartenStore<IMartenStoreMarker<MyContext>>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "bug5039_ancillary";
                });
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IMartenStoreMarker<MyContext>>();
        store.ShouldNotBeNull();
    }
}
