using System;
using Marten.Extras;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Extras
{
    public class ProjectionSource
    {
        public Guid Id = Guid.NewGuid();

        public string Value { get; set; }
        public int AnotherValue { get; set; }
        public object ThirdValue { get; set; }
    }

    public class ProjectionTarget
    {
        public string Value { get; set; }
        public object ThirdValue { get; set; }
    }

    public class project_document_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void project_a_persisted_document_from_store()
        {
            var projectionSource = new ProjectionSource() {Value = "Value", AnotherValue = int.MaxValue, ThirdValue = DateTime.UtcNow };

            theSession.Store(projectionSource);
            theSession.SaveChanges();

            var projection = ProjectionBuilder<ProjectionSource>.Project().Include(i => i.Value).Include(i => i.ThirdValue).To<ProjectionTarget>();

            var projectedValue = theContainer.GetInstance<IDocumentStore>().Project(projectionSource.Id, projection);

            Assert.Equal(projectionSource.Value, projectedValue.Value);
            Assert.Equal(projectionSource.ThirdValue, projectedValue.ThirdValue);
        }

        [Fact]
        public void project_a_persisted_document_from_session()
        {
            var projectionSource = new ProjectionSource() { Value = "Value", AnotherValue = int.MaxValue, ThirdValue = DateTime.UtcNow };

            theSession.Store(projectionSource);
            theSession.SaveChanges();

            var projection = ProjectionBuilder<ProjectionSource>.Project().Include(i => i.Value).Include(i => i.ThirdValue).To<ProjectionTarget>();

            var projectedValue = theSession.Project(projectionSource.Id, projection);

            Assert.Equal(projectionSource.Value, projectedValue.Value);
            Assert.Equal(projectionSource.ThirdValue, projectedValue.ThirdValue);
        }
    }
}