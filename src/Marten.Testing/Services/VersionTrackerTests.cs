using System;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class VersionTrackerTests
    {
        [Fact]
        public void null_when_it_does_not_have_it()
        {
            var tracker = new VersionTracker();
            tracker.Version<User>(Guid.NewGuid())
                .ShouldBeNull();
        }

        [Fact]
        public void find_version_for_doc_by_type_and_id()
        {
            var id = Guid.NewGuid();

            var userVersion = 1;

            var issueVersion = 2;

            var tracker = new VersionTracker();

            tracker.Store<User>(id, userVersion);
            tracker.Store<Issue>(id, issueVersion);

            tracker.Version<User>(id).ShouldBe(userVersion);
            tracker.Version<Issue>(id).ShouldBe(issueVersion);

        }

        [Fact]
        public void can_overwrite_version()
        {
            var id = Guid.NewGuid();

            var version1 = 1;
            var version2 = 2;

            var tracker = new VersionTracker();
            tracker.Store<User>(id, version1);

            tracker.Store<User>(id, version2);

            tracker.Version<User>(id).ShouldBe(version2);
        }
    }
}