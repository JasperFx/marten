using System;
using System.Reflection;
using Marten.Storage;
using Marten.Util;

namespace Marten.Schema.Identity
{
    public class IdAssigner<TDoc, TId> : IdAssignment<TDoc>
    {
        private readonly IIdGenerator<TId> _generator;
        private readonly Func<TDoc, TId> _getter;
        private readonly Action<TDoc, TId> _setter;

        public IdAssigner(MemberInfo member, IIdGeneration generation, ITenant tenant)
        {
            _generator = generation.Build<TId>();
            _getter = LambdaBuilder.Getter<TDoc, TId>(member);
            _setter = LambdaBuilder.Setter<TDoc, TId>(member);
        }

        public IIdGenerator<TId> Generator => _generator;

        public object Assign(ITenant tenant, TDoc document, out bool assigned)
        {
            var original = _getter != null ? _getter(document) : default(TId);

            var id = _generator.Assign(tenant, original, out assigned);

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

        public void Assign(ITenant tenant, TDoc document, object id)
        {
            _setter?.Invoke(document, (TId) id);
        }
    }
}