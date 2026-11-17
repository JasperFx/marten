using System.Threading.Tasks;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

public class UsingInlineSideEffects
{
    public static async Task bootstrap()
    {
        #region sample_using_enablesideeffectsoninlineprojections

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // On by default since Marten 10. Set it to false to only run
            // projection side effects in the async daemon
            opts.Events.EnableSideEffectsOnInlineProjections = true;
        });

        #endregion
    }
}
