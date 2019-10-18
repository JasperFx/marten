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

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (CollectionStorage == CollectionStorage.AsArray && JsonNetCollectionToArrayJsonConverter.Instance.CanConvert(property.PropertyType))
            {
                property.Converter = JsonNetCollectionToArrayJsonConverter.Instance;
            }

            if (NonPublicMembersStorage.HasFlag(NonPublicMembersStorage.NonPublicSetters) && member is PropertyInfo pi)
            {
                property.Readable = pi.GetMethod != null;
                property.Writable = pi.SetMethod != null;
            }
            return property;
        }

        private void SetNamingStrategy(Casing casing)
        {
            if (casing == Casing.CamelCase)
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = true, OverrideSpecifiedNames = true };
            }
            else if (casing == Casing.SnakeCase)
            {
                NamingStrategy = new SnakeCaseNamingStrategy { ProcessDictionaryKeys = true, OverrideSpecifiedNames = true };
            };
        }
    }
}
