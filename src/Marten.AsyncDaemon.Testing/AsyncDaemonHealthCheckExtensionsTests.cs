using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Xunit;
using static Marten.Events.Daemon.AsyncDaemonHealthCheckExtensions;

namespace Marten.AsyncDaemon.Testing;

public class AsyncDaemonHealthCheckExtensionsTests
{
    private FakeHealthCheckBuilderStub builder = new();

    public void Dispose()
    {
        builder = new();
    }

    [Fact]
    public void should_add_settings_to_services()
    {
        builder.Services.ShouldNotContain(x => x.ServiceType == typeof(AsyncDaemonHealthCheckSettings));

        builder.AddMartenAsyncDaemonHealthCheck(200);

        builder.Services.ShouldContain(x => x.ServiceType == typeof(AsyncDaemonHealthCheckSettings));
    }

    [Fact]
    public void should_add_healthcheck_to_services()
    {
        builder.AddMartenAsyncDaemonHealthCheck();

        var services = builder.Services.BuildServiceProvider();
        var healthCheckRegistrations = services.GetServices<HealthCheckRegistration>();
        healthCheckRegistrations.ShouldContain(reg => reg.Name == nameof(AsyncDaemonHealthCheck));
    }
}

internal class FakeHealthCheckBuilderStub : IHealthChecksBuilder
{
    public IServiceCollection Services { get; set; } = new ServiceCollection();

    public IHealthChecksBuilder Add(HealthCheckRegistration registration)
    {
        Services.AddSingleton(registration);
        return this;
    }
}
