using System.Reflection;
using Marten.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Marten.Services.Json
{
    public class JsonNetContractResolver : DefaultContractResolver
    {
        public Casing Casing { get; }

        public CollectionStorage CollectionStorage { get; }

        public JsonNetContractResolver()
        {
        }

        public JsonNetContractResolver(Casing casing, CollectionStorage collectionStorage)
        {
            Casing = casing;
            CollectionStorage = collectionStorage;

            SetNamingStrategy(casing);
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (CollectionStorage == CollectionStorage.AsArray && CollectionToArrayJsonConverter.Instance.CanConvert(property.PropertyType))
            {
                property.Converter = CollectionToArrayJsonConverter.Instance;
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