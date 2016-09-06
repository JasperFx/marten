using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Linq.Compiled
{
    public class ContainmentParameterSetter<TQuery> : IDbParameterSetter
    {
        private readonly ISerializer _serializer;

        public ContainmentParameterSetter(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public void AddElement(string[] keys, MemberInfo member)
        {
            var memberType = member.GetMemberType();
            var setterType = typeof(DictionaryElement<,>).MakeGenericType(typeof(TQuery), memberType);

            var setter = Activator.CreateInstance(setterType, _serializer.EnumStorage, keys, member);
            Elements.Add((IDictionaryElement<TQuery>) setter);
        }

        public void Constant(string[] keys, object value)
        {
            var memberType = value.GetType();
            var setterType = typeof(DictionaryElement<,>).MakeGenericType(typeof(TQuery), memberType);

            var setter = Activator.CreateInstance(setterType, keys, value);
            Elements.Add((IDictionaryElement<TQuery>)setter);
        }

        public IList<IDictionaryElement<TQuery>> Elements { get; } = new List<IDictionaryElement<TQuery>>();

        public IDictionary<string, object> BuildDictionary(TQuery query)
        {
            var dict = new Dictionary<string, object>();

            foreach (var element in Elements)
            {
                element.Write(query, dict);
            }

            return dict;
        }

        public NpgsqlParameter AddParameter(object query, NpgsqlCommand command)
        {
            var dict = BuildDictionary((TQuery) query);
            var json = _serializer.ToCleanJson(dict);

            var param = command.AddParameter(json);
            param.NpgsqlDbType = NpgsqlDbType.Jsonb;

            return param;
        }


    }
}