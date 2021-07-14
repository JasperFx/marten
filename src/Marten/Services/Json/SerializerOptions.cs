using Weasel.Core;
using Weasel.Postgresql;

#nullable enable
namespace Marten.Services.Json
{
    public class SerializerOptions
    {
        public EnumStorage EnumStorage { get; set; } = EnumStorage.AsInteger;

        public Casing Casing { get; set; } = Casing.Default;

        public CollectionStorage CollectionStorage { get; set; } = CollectionStorage.Default;

        public NonPublicMembersStorage NonPublicMembersStorage { get; set; } = NonPublicMembersStorage.Default;
    }
}
