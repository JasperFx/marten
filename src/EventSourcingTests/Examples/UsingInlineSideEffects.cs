using System.Threading.Tasks;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

public class UsingInlineSideEffects
{
    public static async Task bootstrap()
    {
        #region sample_using_EnableSideEffectsOnInlineProjections

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // This is your magic setting to tell Marten to process any projection
            // side effects even when running Inline
            opts.Events.EnableSideEffectsOnInlineProjections = true;
        });

        #endregion
    }
}
