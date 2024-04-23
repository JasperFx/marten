using System;
using Marten.Events.Aggregation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marten.AsyncDaemon.Testing;

internal class FakeHealthCheckBuilderStub : IHealthChecksBuilder
{
    public IServiceCollection Services { get; set; } = new ServiceCollection();

    public IHealthChecksBuilder Add(HealthCheckRegistration registration)
    {
        Services.AddSingleton(registration);
        return this;
    }
}

public record FakeIrrellevantEvent();
public record FakeEvent();
public class FakeStream1 { public Guid Id { get; set; } }

public class FakeSingleStream1Projection : SingleStreamProjection<FakeStream1>
{
    public void Apply(FakeEvent @event, FakeStream1 projection) { }
}

public class FakeStream2 { public Guid Id { get; set; } }
public class FakeSingleStream2Projection : SingleStreamProjection<FakeStream2>
{
    public void Apply(FakeEvent @event, FakeStream2 projection) { }
}

public class FakeStream3 { public Guid Id { get; set; } }
public class FakeSingleStream3Projection : SingleStreamProjection<FakeStream3>
{
    public void Apply(FakeEvent @event, FakeStream3 projection) { }
}

public class FakeStream4 { public Guid Id { get; set; } }
public class FakeSingleStream4Projection : SingleStreamProjection<FakeStream4>
{
    public void Apply(FakeEvent @event, FakeStream4 projection) { }
}

public class FakeStream5 { public Guid Id { get; set; } }
public class FakeSingleStream5Projection : SingleStreamProjection<FakeStream5>
{
    public void Apply(FakeEvent @event, FakeStream5 projection) { }
}

public class FakeStream6 { public Guid Id { get; set; } }
public class FakeSingleStream6Projection : SingleStreamProjection<FakeStream6>
{
    public void Apply(FakeEvent @event, FakeStream6 projection) { }
}
