using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal interface IEventHandlingFrame
    {
        void Configure(EventProcessingFrame parent);
        Type EventType { get; }
    }

    /// <summary>
    /// Calls an AggregatedProjection.Apply() method
    /// </summary>
    internal class EventProcessingFrame : Frame
    {
        private Variable _event;
        private readonly IList<Frame> _inner = new List<Frame>();

        public Type AggregateType { get; }

        public Type EventType { get; }

        public EventProcessingFrame(bool isAsync, Type aggregateType, Type eventType) : base(isAsync)
        {
            EventType = eventType;
            AggregateType = aggregateType;

            // TODO -- watch the naming here.
            SpecificEvent = new Variable(typeof(Event<>).MakeGenericType(eventType), "event_" + eventType.Name);
            DataOnly = new Variable(EventType, $"{SpecificEvent.Usage}.{nameof(Event<string>.Data)}");
        }

        public EventProcessingFrame(Type aggregateType, IEventHandlingFrame inner)
            : this(inner.As<Frame>().IsAsync, aggregateType, inner.EventType)
        {
            Add(inner.As<Frame>());
        }


        public Variable SpecificEvent { get; }

        public Variable Aggregate { get; private set; }

        public Variable DataOnly { get; }

        public void Add(Frame inner)
        {
            _inner.Add(inner);
        }


        public IfStyle IfStyle { get; set; } = IfStyle.If;

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            // You don't need it if you're in a Create method
            Aggregate = chain.TryFindVariable(AggregateType, VariableSource.All);
            if (Aggregate != null) yield return Aggregate;

            foreach (var inner in _inner.OfType<IEventHandlingFrame>())
            {
                inner.Configure(this);
            }

            _event = chain.FindVariable(typeof(IEvent));

            yield return _event;

            foreach (var inner in _inner)
            {
                foreach (var variable in inner.FindVariables(chain))
                {
                    yield return variable;
                }
            }
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            IfStyle.Open(writer, $"{_event.Usage} is {SpecificEvent.VariableType.FullNameInCode()} {SpecificEvent.Usage}");

            foreach (var frame in _inner)
            {
                frame.GenerateCode(method, writer);
            }

            IfStyle.Close(writer);

            Next?.GenerateCode(method, writer);
        }
    }
}
