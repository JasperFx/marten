using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Schema;

namespace Marten.Linq
{
    public class TargetObject
    {
        public Type Type { get; }
        public readonly IList<SetterBinding> Setters = new List<SetterBinding>(); 

        public TargetObject(Type type)
        {
            Type = type;
        }

        public SelectedField StartBinding(MemberInfo member)
        {
            var setter = new SetterBinding(member);
            Setters.Add(setter);

            return setter.Field;
        }

        public ISelector<T> ToSelector<T>(IDocumentMapping mapping)
        {
            return new SelectTransformer<T>(mapping, this);
        }
    }
}