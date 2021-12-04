using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Marten.Services.Json
{
    public class JsonNetContractResolver: DefaultContractResolver
    {
        public Casing Casing { get; }

        public CollectionStorage CollectionStorage { get; }

        public NonPublicMembersStorage NonPublicMembersStorage { get; }

        public JsonNetContractResolver()
        {
        }

        public JsonNetContractResolver(Casing casing, CollectionStorage collectionStorage, NonPublicMembersStorage nonPublicMembersStorage = NonPublicMembersStorage.Default)
        {
            Casing = casing;
            CollectionStorage = collectionStorage;
            NonPublicMembersStorage = nonPublicMembersStorage;

            SetNamingStrategy(casing);
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);

            if (!NonPublicMembersStorage.HasFlag(NonPublicMembersStorage.NonPublicConstructor))
                return contract;

            return JsonNetObjectContractProvider.UsingNonDefaultConstructor(
                contract,
                objectType,
                base.CreateConstructorParameters
            );
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (CollectionStorage == CollectionStorage.AsArray && JsonNetCollectionToArrayJsonConverter.Instance.CanConvert(property.PropertyType))
            {
                property.Converter = JsonNetCollectionToArrayJsonConverter.Instance;
            }

            if (!NonPublicMembersStorage.HasFlag(NonPublicMembersStorage.NonPublicSetters) ||
                member is not PropertyInfo pi) return property;

            property.Readable = pi.GetMethod != null;
            property.Writable = pi.SetMethod != null;
            return property;
        }

        private void SetNamingStrategy(Casing casing)
        {
            NamingStrategy = casing switch
            {
                Casing.CamelCase => new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = true, OverrideSpecifiedNames = true
                },
                Casing.SnakeCase => new SnakeCaseNamingStrategy
                {
                    ProcessDictionaryKeys = true, OverrideSpecifiedNames = true
                },
                _ => NamingStrategy
            };
            ;
        }
    }
}
