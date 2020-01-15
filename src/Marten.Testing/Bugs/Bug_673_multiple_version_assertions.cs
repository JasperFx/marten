using System;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_673_multiple_version_assertions: IntegratedFixture
    {
        [Fact]
        public void replaces_the_max_version_assertion()
        {
            var streamId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.Events.Append(streamId, new WhateverEvent(), new WhateverEvent());

                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var state = session.Events.FetchStreamState(streamId);
                // ... do some stuff
                var expectedVersion = state.Version + 1;
                session.Events.Append(streamId, expectedVersion, new WhateverEvent());
                // ... do some more stuff
                expectedVersion += 1;
                session.Events.Append(streamId, expectedVersion, new WhateverEvent());
                session.SaveChanges();
            }
        }
    }

    public class WhateverEvent
    {
    }
}
