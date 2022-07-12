using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Events.CodeGeneration;
using Marten.Events.Daemon;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.Projections
{
    /// <summary>
    ///     This is the "do anything" projection type
    /// </summary>
    public abstract class EventProjection: GeneratedProjection
    {
        private readonly CreateMethodCollection _createMethods;
        private readonly string _inlineTypeName;
        private readonly ProjectMethodCollection _projectMethods;
        private Type _generatedType;

        private GeneratedType _inlineType;
        private bool _isAsync;

        private readonly Lazy<IProjection> _generatedProjection;

        public EventProjection(): base("Projections")
        {
            _projectMethods = new ProjectMethodCollection(GetType());
            _createMethods = new CreateMethodCollection(GetType());

            ProjectionName = GetType().FullNameInCode();
            _inlineTypeName = GetType().ToSuffixedTypeName("InlineProjection");

            _generatedProjection = new Lazy<IProjection>(() =>
            {
                if (_generatedType == null)
                    throw new InvalidOperationException("The EventProjection has not created its inner IProjection");

                var projection = (IProjection)Activator.CreateInstance(_generatedType, this);
                foreach (var setter in _inlineType.Setters)
                {
                    setter.SetInitialValue(projection);
                }

                return projection;
            });
        }

        protected override IProjection buildProjectionObject(DocumentStore store)
        {
            return _generatedProjection.Value;
        }

        protected override bool needsSettersGenerated()
        {
            return _inlineType == null;
        }

        protected override ValueTask<EventRangeGroup> groupEvents(DocumentStore store, IMartenDatabase daemonDatabase, EventRange range,
            CancellationToken cancellationToken)
        {
            return new ValueTask<EventRangeGroup>(new TenantedEventRangeGroup(store, daemonDatabase, _generatedProjection.Value, range, cancellationToken));
        }


        protected override void assembleTypes(GeneratedAssembly assembly, StoreOptions options)
        {
            assembly.Rules.Assemblies.Add(GetType().Assembly);
            assembly.Rules.Assemblies.AddRange(_projectMethods.ReferencedAssemblies());
            assembly.Rules.Assemblies.AddRange(_createMethods.ReferencedAssemblies());

            assembly.UsingNamespaces.Add("System.Linq");

            _isAsync = _createMethods.IsAsync || _projectMethods.IsAsync;

            var baseType = _isAsync ? typeof(AsyncEventProjection<>) : typeof(SyncEventProjection<>);
            baseType = baseType.MakeGenericType(GetType());
            _inlineType = assembly.AddType(_inlineTypeName, baseType);

            var method = _inlineType.MethodFor("ApplyEvent");
            method.DerivedVariables.Add(new Variable(GetType(), "Projection"));

            var eventHandling = MethodCollection.AddEventHandling(null, null, _createMethods, _projectMethods);
            method.Frames.Add(eventHandling);
        }

        protected override bool tryAttachTypes(Assembly assembly, StoreOptions options)
        {
            _generatedType = assembly.GetExportedTypes().FirstOrDefault(x => x.Name == _inlineTypeName);
            return _generatedType != null;
        }

        internal override void AssembleAndAssertValidity()
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

            IncludedEventTypes.Fill(MethodCollection.AllEventTypes(_createMethods, _projectMethods));

            foreach (var method in _createMethods.Methods)
            {
                var docType = method.ReturnType;
                if (docType.Closes(typeof(Task<>)))
                {
                    RegisterPublishedType(docType.GetGenericArguments().Single());
                }
                else
                {
                    RegisterPublishedType(docType);
                }
            }
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
        ///     This would be a helper for the open ended EventProjection
        /// </summary>
        internal class ProjectMethodCollection: MethodCollection
        {
            public static readonly string MethodName = "Project";


            public ProjectMethodCollection(Type projectionType): base(MethodName, projectionType, null)
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

            public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
                IDocumentMapping aggregateMapping,
                MethodSlot slot)
            {
                return new ProjectMethodCall(slot);
            }
        }

        internal class ProjectMethodCall: MethodCall, IEventHandlingFrame
        {
            public ProjectMethodCall(MethodSlot slot): base(slot.HandlerType, (MethodInfo)slot.Method)
            {
                EventType = Method.GetEventType(null);
                Target = slot.Setter;
            }

            public Type EventType { get; }

            public void Configure(EventProcessingFrame parent)
            {
                // Replace any arguments to IEvent<T>

                TrySetArgument(parent.SpecificEvent);

                // Replace any arguments to the specific T event type
                TrySetArgument(parent.DataOnly);
            }
        }

        /// <summary>
        ///     This would be a helper for the open ended EventProjection
        /// </summary>
        internal class CreateMethodCollection: MethodCollection
        {
            public static readonly string MethodName = "Create";
            public static readonly string TransformMethodName = "Transform";

            public CreateMethodCollection(Type projectionType): base(new[] { MethodName, TransformMethodName },
                projectionType, null)
            {
                _validArgumentTypes.Add(typeof(IDocumentOperations));
            }

            public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
                IDocumentMapping aggregateMapping,
                MethodSlot slot)
            {
                return new CreateMethodFrame(slot);
            }

            internal override void validateMethod(MethodSlot method)
            {
                if (method.ReturnType == typeof(void))
                {
                    method.AddError("The return value must be a new document");
                }
            }
        }

        internal class CreateMethodFrame: MethodCall, IEventHandlingFrame
        {
            private static int _counter;

            private Variable _operations;

            public CreateMethodFrame(MethodSlot slot): base(slot.HandlerType, (MethodInfo)slot.Method)
            {
                EventType = Method.GetEventType(null);
                ReturnVariable.OverrideName(ReturnVariable.Usage + ++_counter);
            }

            public Type EventType { get; }

            public void Configure(EventProcessingFrame parent)
            {
                // Replace any arguments to IEvent<T>
                TrySetArgument(parent.SpecificEvent);

                // Replace any arguments to the specific T event type
                TrySetArgument(parent.DataOnly);
            }

            public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
            {
                foreach (var variable in base.FindVariables(chain)) yield return variable;

                _operations = chain.FindVariable(typeof(IDocumentOperations));
            }

            public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
            {
                base.GenerateCode(method, writer);
                writer.WriteLine($"{_operations.Usage}.Store({ReturnVariable.Usage});");
            }
        }
    }

    public abstract class SyncEventProjection<T>: SyncEventProjectionBase where T : EventProjection
    {
        public SyncEventProjection(T projection)
        {
            Projection = projection;
        }

        public T Projection { get; }
    }

    public abstract class AsyncEventProjection<T>: AsyncEventProjectionBase where T : EventProjection
    {
        public AsyncEventProjection(T projection)
        {
            Projection = projection;
        }

        public T Projection { get; }
    }
}
