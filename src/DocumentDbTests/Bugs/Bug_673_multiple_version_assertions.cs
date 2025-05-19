using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_673_multiple_version_assertions: IntegrationContext
{
    [Fact]
    public async Task replaces_the_max_version_assertion()
    {
        var streamId = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new WhateverEvent(), new WhateverEvent());

            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            var state = await session.Events.FetchStreamStateAsync(streamId);
            // ... do some stuff
            var expectedVersion = state.Version + 1;
            session.Events.Append(streamId, expectedVersion, new WhateverEvent());
            // ... do some more stuff
            expectedVersion += 1;
            session.Events.Append(streamId, expectedVersion, new WhateverEvent());
            await session.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task replaces_the_max_version_assertion_for_string_identity()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new WhateverEvent(), new WhateverEvent());

            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            var state = await session.Events.FetchStreamStateAsync(streamId);
            // ... do some stuff
            var expectedVersion = state.Version + 1;
            session.Events.Append(streamId, expectedVersion, new WhateverEvent());
            // ... do some more stuff
            expectedVersion += 1;
            session.Events.Append(streamId, expectedVersion, new WhateverEvent());
            await session.SaveChangesAsync();
        }
    }

    public Bug_673_multiple_version_assertions(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

public class WhateverEvent
{
}
