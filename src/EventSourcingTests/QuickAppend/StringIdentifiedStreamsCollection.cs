using Xunit;

namespace EventSourcingTests.QuickAppend;

[CollectionDefinition("quick_string_identified_streams")]
public class StringIdentifiedStreamsCollection: ICollectionFixture<StringIdentifiedStreamsFixture>
{
}
