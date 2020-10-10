using System;
using System.Reflection;
using Marten.Storage;
using Marten.Util;

namespace Marten.Schema.Identity
{
    public class IdAssigner<TDoc, TId>: IdAssignment<TDoc>
    {
        public IIdGenerator<TId> Generator { get; }

        private readonly Func<TDoc, TId> _getter;
        private readonly Action<TDoc, TId> _setter;

        public IdAssigner(MemberInfo member, IIdGeneration generation)
        {
            Generator = generation.Build<TId>();
            _getter = LambdaBuilder.Getter<TDoc, TId>(member);
            _setter = LambdaBuilder.Setter<TDoc, TId>(member);
        }

        public void Assign(ITenant tenant, TDoc document, object id)
        {
            _setter?.Invoke(document, (TId)id);
        }
    }
}
