using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.SessionMechanics;

public interface IInvoicingStore: IDocumentStore
{
}

public class reset_all_data_usage_ihost
{
    private readonly ITestOutputHelper _output;

    public reset_all_data_usage_ihost(
        ITestOutputHelper output
    ) => _output = output;

    [Fact]
    public async Task can_reset_all_data_on_ihost()
    {
        #region sample_reset_all_data_ihost

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(
                services =>
                {
                    services.AddMarten(
                            opts =>
                            {
                                opts.Connection(ConnectionSource.ConnectionString);
                                opts.Logger(new TestOutputMartenLogger(_output));
                            }
                        )
                        .InitializeWith(new reset_all_data_usage.Users());
                }
            )
            .StartAsync();

        await host.ResetAllMartenDataAsync();

        #endregion
    }

    [Fact]
    public async Task can_reset_all_data_on_ihost_for_specific_marten_database()
    {
        #region sample_reset_all_data_ihost_specific_database

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(
                services =>
                {
                    services.AddMarten(
                            opts =>
                            {
                                opts.Connection(ConnectionSource.ConnectionString);
                                opts.Logger(new TestOutputMartenLogger(_output));
                            }
                        )
                        .InitializeWith(new reset_all_data_usage.Users());
                }
            )
            .StartAsync();

        await host.ResetAllMartenDataAsync<IInvoicingStore>();

        #endregion
    }
}
