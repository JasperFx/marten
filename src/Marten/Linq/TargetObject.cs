using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
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

        public ISelector<T> ToSelector<T>(IQueryableDocument mapping, bool distinct = false)
        {
            return new SelectTransformer<T>(mapping, this, distinct);
        }

        public string ToSelectField(IQueryableDocument mapping)
        {
            var jsonBuildObjectArgs = Setters.Select(x => x.ToJsonBuildObjectPair(mapping)).Join(", ");
            return  $"jsonb_build_object({jsonBuildObjectArgs}) as json";
        }

        public ISelector<T> ToJsonSelector<T>(IQueryableDocument mapping)
        {
            var field = ToSelectField(mapping);
            return new JsonSelector(field).As<ISelector<T>>();
        }
    }
}