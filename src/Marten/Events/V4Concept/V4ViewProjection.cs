using System;
using System.Reflection;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Events.V4Concept.CodeGeneration;
using Marten.Schema;

namespace Marten.Events.V4Concept
{
    /// <summary>
    /// This would be a helper for the open ended, ViewProjection
    /// </summary>
    internal class ProjectMethodCollection: MethodCollection
    {
        public static readonly string MethodName = "Process";

        public ProjectMethodCollection(Type projectionType, Type aggregateType) : base(MethodName, projectionType, aggregateType)
        {
        }

        public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType, DocumentMapping aggregateMapping,
            MethodSlot slot)
        {
            throw new NotImplementedException();
        }
    }

    // This would be the basis for the newly improved V4 version of ViewProjection
    public abstract class ViewProjection
    {
        private readonly ProjectMethodCollection _projectMethods;
        private string _projectionName; // More on this later

        public ViewProjection()
        {
            _projectMethods = new ProjectMethodCollection(GetType(), null);
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

    }

}
