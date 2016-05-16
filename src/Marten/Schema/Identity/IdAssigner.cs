using System;
using System.Reflection;
using Marten.Util;

namespace Marten.Schema.Identity
{
    public class IdAssigner<TDoc, TId> : IdAssignment<TDoc>
    {
        private readonly IIdGenerator<TId> _generator;
        private readonly Func<TDoc, TId> _getter;
        private readonly Action<TDoc, TId> _setter;

        public IdAssigner(MemberInfo member, IIdGeneration generation, IDocumentSchema schema)
        {
            _generator = generation.Build<TId>(schema);
            _getter = LambdaBuilder.Getter<TDoc, TId>(member);
            _setter = LambdaBuilder.Setter<TDoc, TId>(member);
        }

        public object Assign(TDoc document, out bool assigned)
        {
            var original = _getter(document);

            var id = _generator.Assign(original, out assigned);

            if (assigned)
            {
                if (_setter == null)
                {
                    throw new InvalidOperationException($"The identity of {typeof(TDoc)} cannot be assigned");
                }

                _setter(document, id);
            }

            return id;
        }

        public void Assign(TDoc document, object id)
        {
            if (_setter != null) _setter(document, (TId) id);
        }
    }
}