using System;
using System.Threading.Tasks;
using Marten.Events.V4Concept.CodeGeneration;
using Marten.Schema;

namespace Marten.Events.V4Concept
{


    /// <summary>
    /// This is the "do anything" projection type
    /// </summary>
    public abstract class EventProjection
    {
        private readonly ProjectMethodCollection _projectMethods;
        private readonly CreateMethodCollection _createMethods;

        public EventProjection()
        {
            _projectMethods = new ProjectMethodCollection(GetType(), null);
            _createMethods = new CreateMethodCollection(GetType(), null);
        }

        [IgnoreProjectionMethod]
        public void Project<TEvent>(Action<TEvent, IDocumentOperations> project)
        {
            _projectMethods.AddLambda(project, typeof(TEvent));
        }

        [IgnoreProjectionMethod]
        public void ProjectAsync<TEvent>(Func<TEvent, IDocumentOperations, Task> project)
        {
            _projectMethods.AddLambda(project, typeof(TEvent));
        }

        /// <summary>
        /// This would be a helper for the open ended EventProjection
        /// </summary>
        internal class ProjectMethodCollection: MethodCollection
        {
            public static readonly string MethodName = "Project";

            public ProjectMethodCollection(Type projectionType, Type aggregateType) : base(MethodName, projectionType, aggregateType)
            {
            }

            public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType, DocumentMapping aggregateMapping,
                MethodSlot slot)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// This would be a helper for the open ended EventProjection
        /// </summary>
        internal class CreateMethodCollection: MethodCollection
        {
            public static readonly string MethodName = "Create";

            public CreateMethodCollection(Type projectionType, Type aggregateType) : base(MethodName, projectionType, aggregateType)
            {
            }

            public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType, DocumentMapping aggregateMapping,
                MethodSlot slot)
            {
                throw new NotImplementedException();
            }
        }

    }

}
