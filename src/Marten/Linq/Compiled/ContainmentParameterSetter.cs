using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Linq.Compiled
{
    public interface IContainmentParameterSetter : IDbParameterSetter
    {
        void AddElement(string[] keys, MemberInfo member);
        void Constant(string[] keys, object value);
    }

    public class ContainmentParameterSetter<TQuery> : IContainmentParameterSetter
    {
        private readonly ISerializer _serializer;
        private readonly MemberInfo[] _pathToCollection;

        public ContainmentParameterSetter(ISerializer serializer, MemberInfo[] pathToCollection)
        {
            _serializer = serializer;
            _pathToCollection = pathToCollection;
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

        public NpgsqlParameter AddParameter(object query, CommandBuilder command)
        {
            var dict = BuildDictionary((TQuery) query);

            var array = new IDictionary<string, object>[] {dict};
            dict = new Dictionary<string, object> { {_pathToCollection.Last().Name, array} };

            _pathToCollection.Reverse().Skip(1).Each(member =>
            {
                dict = new Dictionary<string, object> {{member.Name, dict}};
            });


            var json = _serializer.ToCleanJson(dict);

            var param = command.AddParameter(json);
            param.NpgsqlDbType = NpgsqlDbType.Jsonb;

            return param;
        }


    }
}