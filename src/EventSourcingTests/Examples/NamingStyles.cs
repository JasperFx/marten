using JasperFx.Events;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

public class NamingStyles
{
    public static void configure_different_naming_styles()
    {
        #region sample_event_naming_style

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));


            // This is the default behavior, but just showing you that
            // this is an option
            opts.Events.EventNamingStyle = EventNamingStyle.ClassicTypeName;

            // This mode is "the classic style Marten has always used, except smart enough
            // to disambiguate inner classes that have the same type name"
            opts.Events.EventNamingStyle = EventNamingStyle.SmarterTypeName;

            // Forget all the pretty naming aliases, just use the .NET full type name for
            // the event type name
            opts.Events.EventNamingStyle = EventNamingStyle.FullTypeName;
        });

        #endregion
    }
}
