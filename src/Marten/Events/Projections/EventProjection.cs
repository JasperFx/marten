using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using LamarCompiler;
using Marten.Events.CodeGeneration;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Events.Projections
{


    /// <summary>
    /// This is the "do anything" projection type
    /// </summary>
    public abstract class EventProjection : IProjectionSource, IValidatedProjection
    {
        private readonly ProjectMethodCollection _projectMethods;
        private readonly CreateMethodCollection _createMethods;

        public EventProjection()
        {
            _projectMethods = new ProjectMethodCollection(GetType());
            _createMethods = new CreateMethodCollection(GetType());

            // TODO -- get fancier later
            ProjectionName = GetType().FullName;
        }

        void IValidatedProjection.AssertValidity()
        {
            if (!_projectMethods.Methods.Any() && !_createMethods.Methods.Any())
            {
                throw new InvalidProjectionException(
                    $"EventProjection {GetType().FullNameInCode()} has no valid projection operations");
            }

            var invalidMethods = MethodCollection.FindInvalidMethods(GetType(), _projectMethods, _createMethods);

            if (invalidMethods.Any())
            {
                throw new InvalidProjectionException(this, invalidMethods);
            }
        }

        IEnumerable<string> IValidatedProjection.ValidateConfiguration(StoreOptions options)
        {
            // Nothing
            yield break;
        }

        [MartenIgnore]
        public void Project<TEvent>(Action<TEvent, IDocumentOperations> project)
        {
            _projectMethods.AddLambda(project, typeof(TEvent));
        }

        [MartenIgnore]
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


            public ProjectMethodCollection(Type projectionType) : base(MethodName, projectionType, null)
            {
                _validArgumentTypes.Add(typeof(IDocumentOperations));
                _validReturnTypes.Add(typeof(void));
                _validReturnTypes.Add(typeof(Task));
            }

            internal override void validateMethod(MethodSlot method)
            {
                if (method.Method.GetParameters().All(x => x.ParameterType != typeof(IDocumentOperations)))
                {
                    method.AddError($"{typeof(IDocumentOperations).FullNameInCode()} is a required parameter");
                }
            }

            public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType, DocumentMapping aggregateMapping,
                MethodSlot slot)
            {
                return new ProjectMethodCall(slot);
            }
        }

        internal class ProjectMethodCall: MethodCall, IEventHandlingFrame
        {
            public ProjectMethodCall(MethodSlot slot) : base(slot.HandlerType, (MethodInfo) slot.Method)
            {
                EventType = Method.GetEventType(null);
                Target = slot.Setter;
            }

            public Type EventType { get; }

            public void Configure(EventProcessingFrame parent)
            {
                // Replace any arguments to Event<T>
                TrySetArgument(parent.SpecificEvent);

                // Replace any arguments to the specific T event type
                TrySetArgument(parent.DataOnly);
            }
        }

        /// <summary>
        /// This would be a helper for the open ended EventProjection
        /// </summary>
        internal class CreateMethodCollection: MethodCollection
        {
            public static readonly string MethodName = "Create";
            public static readonly string TransformMethodName = "Transform";

            public CreateMethodCollection(Type projectionType): base(new[] { MethodName, TransformMethodName}, projectionType, null)
            {
                _validArgumentTypes.Add(typeof(IDocumentOperations));
            }

            public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType, DocumentMapping aggregateMapping,
                MethodSlot slot)
            {
                return new CreateMethodFrame(slot);
            }

            internal override void validateMethod(MethodSlot method)
            {
                if (method.ReturnType == typeof(void))
                {
                    method.AddError($"The return value must be a new document");
                }
            }
        }

        internal class CreateMethodFrame: MethodCall, IEventHandlingFrame
        {
            private Variable _operations;

            public CreateMethodFrame(MethodSlot slot) : base(slot.HandlerType, (MethodInfo) slot.Method)
            {
                EventType = Method.GetEventType(null);
            }

            public Type EventType { get; }

            public void Configure(EventProcessingFrame parent)
            {
                // Replace any arguments to Event<T>
                TrySetArgument(parent.SpecificEvent);

                // Replace any arguments to the specific T event type
                TrySetArgument(parent.DataOnly);
            }

            public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
            {
                foreach (var variable in base.FindVariables(chain))
                {
                    yield return variable;
                }

                _operations = chain.FindVariable(typeof(IDocumentOperations));

            }

            public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
            {
                base.GenerateCode(method, writer);
                writer.WriteLine($"{_operations.Usage}.Store({ReturnVariable.Usage});");
            }
        }

        public string ProjectionName { get; }

        private GeneratedType _inlineType;
        private GeneratedAssembly _assembly;
        private bool _isAsync;

        IInlineProjection IProjectionSource.BuildInline(StoreOptions options)
        {
            if (_inlineType == null)
            {
                Compile(options);
            }

            Debug.WriteLine(_inlineType.SourceCode);

            var inline = (IInlineProjection)Activator.CreateInstance(_inlineType.CompiledType, this);
            _inlineType.ApplySetterValues(inline);

            return inline;
        }

        internal void Compile(StoreOptions options)
        {
            _assembly = new GeneratedAssembly(new GenerationRules("Marten.Generated"));

            _assembly.Generation.Assemblies.Add(GetType().Assembly);
            _assembly.Generation.Assemblies.AddRange(_projectMethods.ReferencedAssemblies());
            _assembly.Generation.Assemblies.AddRange(_createMethods.ReferencedAssemblies());

            _assembly.Namespaces.Add("System.Linq");

            _isAsync = _createMethods.IsAsync || _projectMethods.IsAsync;

            var baseType = _isAsync ? typeof(AsyncInlineEventProjection<>) : typeof(SyncInlineEventProjection<>);
            baseType = baseType.MakeGenericType(GetType());
            _inlineType = _assembly.AddType(GetType().Name.Sanitize() + "GeneratedInlineProjection", baseType);

            var method = _inlineType.MethodFor("ApplyEvent");
            method.DerivedVariables.Add(new Variable(GetType(), "Projection"));

            var handlers = MethodCollection.AddEventHandling(null, null, _createMethods, _projectMethods);
            method.Frames.AddRange(handlers);

            var assemblyGenerator = new AssemblyGenerator();

            assemblyGenerator.ReferenceAssembly(typeof(IMartenSession).Assembly);
            assemblyGenerator.Compile(_assembly);
        }

    }

    public abstract class SyncInlineEventProjection<T>: IInlineProjection where T : EventProjection
    {
        public T Projection { get; }

        public SyncInlineEventProjection(T projection)
        {
            Projection = projection;
        }

        public void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams)
        {
            foreach (var stream in streams)
            {
                foreach (var @event in stream.Events)
                {
                    ApplyEvent(session, stream, @event);
                }
            }
        }

        public abstract void ApplyEvent(IDocumentOperations operations, StreamAction streamAction, IEvent e);

        public Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
        {
            Apply(session, streams);
            return Task.CompletedTask;
        }
    }

    public abstract class AsyncInlineEventProjection<T> : IInlineProjection where T : EventProjection
    {
        public T Projection { get; }

        public AsyncInlineEventProjection(T projection)
        {
            Projection = projection;
        }

        public async Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
        {
            foreach (var stream in streams)
            {
                foreach (var @event in stream.Events)
                {
                    await ApplyEvent(session, stream, @event, cancellation);
                }
            }
        }

        public abstract Task ApplyEvent(IDocumentOperations operations, StreamAction streamAction, IEvent e,
            CancellationToken cancellationToken);

        public void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams)
        {
            ApplyAsync(session, streams, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

}
