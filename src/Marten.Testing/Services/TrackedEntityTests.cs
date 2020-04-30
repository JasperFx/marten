using System;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
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
            SpecificationExtensions.ShouldBeNull(entity.DetectChange());
        }

        [Fact]
        public void detect_changes_positive()
        {
            var target = Target.Random();
            var entity = new TrackedEntity(target.Id, new TestsSerializer(), typeof(Target), target, null);
            target.Long++;

            var change = entity.DetectChange();

            SpecificationExtensions.ShouldNotBeNull(change);
            change.DocumentType.ShouldBe(typeof(Target));
            change.Id.ShouldBe(target.Id);
            change.Json.ShouldBe(new TestsSerializer().ToJson(target));
        }

        [Fact]
        public void detect_changes_negative()
        {
            var target = Target.Random();
            var entity = new TrackedEntity(target.Id, new TestsSerializer(), typeof(Target), target, null);

            SpecificationExtensions.ShouldBeNull(entity.DetectChange());
        }

        [Fact]
        public void change_is_cleared()
        {
            var target = Target.Random();
            var entity = new TrackedEntity(target.Id, new TestsSerializer(), typeof(Target), target,null);
            target.Long++;

            var change = entity.DetectChange();
            change.ChangeCommitted();

            SpecificationExtensions.ShouldBeNull(entity.DetectChange());
        }
    }
}
