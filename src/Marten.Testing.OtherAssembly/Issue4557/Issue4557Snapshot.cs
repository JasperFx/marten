using System;
using JasperFx.Events.Projections;
using Marten;

namespace Marten.Testing.OtherAssembly.Issue4557;

// Reproduction types for https://github.com/JasperFx/marten/issues/4557.
//
// These deliberately live in Marten.Testing.OtherAssembly because that project
// references Marten via ProjectReference but does NOT reference the
// JasperFx.Events.SourceGenerator analyzer (Marten hides it with
// PrivateAssets=all, so it never flows to a consumer). That mirrors a real
// consumer app that only does `<PackageReference Include="Marten" />`.
//
// MyType is a self-aggregating immutable record with static Create/Apply,
// registered via `Snapshot<MyType>(Inline)` — exactly the user's sample. Note
// it is intentionally NOT `partial`: self-aggregating types registered through
// Snapshot<T> do not require partial (that requirement is only for projection
// subclasses). The breakage is purely that no source-generated dispatcher is
// emitted in this analyzer-free assembly.
public static class Issue4557Registration
{
    public static void Configure(StoreOptions options)
    {
        options.Projections.Snapshot<MyType>(SnapshotLifecycle.Inline);
    }
}

public record CreateMyType(Guid Id, string Name);

public record MyTypeCreated(Guid Id, string Name);

public record MyTypeIncremented(int Amount);

public record MyType(Guid Id, string Name, int Count, string LastEvent)
{
    public static MyType Create(MyTypeCreated @event)
    {
        return new MyType(@event.Id, @event.Name, 0, nameof(MyTypeCreated));
    }

    public static MyType Apply(MyTypeIncremented @event, MyType current)
    {
        return current with
        {
            Count = current.Count + @event.Amount,
            LastEvent = nameof(MyTypeIncremented)
        };
    }
}
