using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Schema;
using Marten.Services;

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

        public ISelector<T> ToSelector<T>()
        {
            return new SelectTransformer<T>(this);
        }
    }

    public class SelectTransformer<T> : ISelector<T>
    {
        private readonly TargetObject _target;

        public SelectTransformer(TargetObject target)
        {
            _target = target;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetString(0);
            return map.Serializer.FromJson<T>(json);
        }

        public string[] CalculateSelectedFields(IDocumentMapping mapping)
        {
            var jsonBuildObjectArgs = _target.Setters.Select(x => x.ToJsonBuildObjectPair(mapping)).Join(", ");
            return new [] { $"json_build_object({jsonBuildObjectArgs}) as json"};
        }
    }
}