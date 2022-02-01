using System.Text.Json.Serialization;

namespace CoreTests.Documents
{
    public class StringDoc
    {
        [JsonInclude] // this is needed to make System.Text.Json happy
        public string Id;
        public string Size { get; set; }
    }
}
