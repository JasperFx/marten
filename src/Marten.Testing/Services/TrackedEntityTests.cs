using System;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;

namespace Marten.Testing.Services
{
    public class TrackedEntityTests
    {
        public void detect_changes_with_no_document()
        {
            var entity = new TrackedEntity(Guid.NewGuid(), new JilSerializer(), typeof (Target), null);
            entity.DetectChange().ShouldBeNull();
        }

        public void detect_changes_positive()
        {
            var target = Target.Random();
            var entity = new TrackedEntity(target.Id, new JilSerializer(), typeof(Target), target);
            target.Long++;

            var change = entity.DetectChange();

            change.ShouldNotBeNull();
            change.DocumentType.ShouldBe(typeof(Target));
            change.Id.ShouldBe(target.Id);
            change.Json.ShouldBe(new JilSerializer().ToJson(target));
        }

        public void detect_changes_negative()
        {
            var target = Target.Random();
            var entity = new TrackedEntity(target.Id, new JilSerializer(), typeof(Target), target);

            entity.DetectChange().ShouldBeNull();
        }

        public void change_is_cleared()
        {
            var target = Target.Random();
            var entity = new TrackedEntity(target.Id, new JilSerializer(), typeof(Target), target);
            target.Long++;

            var change = entity.DetectChange();
            change.ChangeCommitted();

            entity.DetectChange().ShouldBeNull();
            

        }
    }
}