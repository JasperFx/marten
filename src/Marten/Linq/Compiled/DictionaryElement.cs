using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;

namespace Marten.Linq.Compiled
{
    public class DictionaryElement<TQuery, TElement> : IDictionaryElement<TQuery>
    {
        public string[] Keys { get; }
        private readonly Func<TQuery, TElement> _getter;

        public DictionaryElement(EnumStorage storage, string[] keys, MemberInfo member)
        {
            Keys = keys;
            _getter = LambdaBuilder.Getter<TQuery, TElement>(storage, new []{member});

            Member = member;
        }

        public MemberInfo Member { get; }

        public void Write(TQuery target, IDictionary<string, object> dictionary)
        {
            var value = _getter(target);

            var dict = dictionary;
            for (int i = 0; i < Keys.Length - 1; i++)
            {
                var key = Keys[i];
                if (!dict.ContainsKey(key))
                {
                    var child = new Dictionary<string, object>();
                    dict.Add(key, child);
                    dict = child;
                }
                else
                {
                    dict = dict[key].As<IDictionary<string, object>>();
                }
            }

            dict.Add(Keys.Last(), value);
        }
    }
}