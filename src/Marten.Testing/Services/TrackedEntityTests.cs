using System;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class TrackedEntityTests
    {
        [Fact]
        public void detect_changes_with_no_document()
        {
            var entity = new TrackedEntity(Guid.NewGuid(), new TestsSerializer(), typeof(Target), null, null);
            entity.DetectChange().ShouldBeNull();
        }

        [Fact]
        public void detect_changes_positive()
        {
            var target = Target.Random();
            var entity = new TrackedEntity(target.Id, new TestsSerializer(), typeof(Target), target, null);
            target.Long++;

            var change = entity.DetectChange();

            change.ShouldNotBeNull();
            change.DocumentType.ShouldBe(typeof(Target));
            change.Id.ShouldBe(target.Id);
            change.Json.ShouldBe(new TestsSerializer().ToJson(target));
        }

        [Fact]
        public void detect_changes_negative()
        {
            var target = Target.Random();
            var entity = new TrackedEntity(target.Id, new TestsSerializer(), typeof(Target), target, null);

            entity.DetectChange().ShouldBeNull();
        }

        [Fact]
        public void change_is_cleared()
        {
            var target = Target.Random();
            var entity = new TrackedEntity(target.Id, new TestsSerializer(), typeof(Target), target,null);
            target.Long++;

            var change = entity.DetectChange();
            change.ChangeCommitted();

            entity.DetectChange().ShouldBeNull();
        }
    }
}