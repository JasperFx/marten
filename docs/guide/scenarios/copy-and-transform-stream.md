# Copy and transform stream

This scenario demonstrates how to copy and transform event stream to enable

* Introduction of new events
* Deletion of events

## Scenario

Lets say we have an event stream, from which we would like to delete events of specific kind. Furthermore, we have a new event type that we would like to compose from existing data (akin to versioning). In the sample below, we setup our initial stream.

<!-- snippet: sample_scenario-copyandtransformstream-setup -->
<!-- endSnippet -->

Next, we introduce a new event type to expand the `MembersJoined` to a series of events, one for each member.

<!-- snippet: sample_scenario-copyandtransformstream-newevent -->
<!-- endSnippet -->

Lastly, we want trolls (`MonsterSlayed`) removed from our stream. However, the stream is a series of ordered, immutable data, with no functionality to patch or otherwise modify existing data. Instead of trying to mutate the stream, we can use the copy and transform pattern to introduce a new event stream. We do this by copying the existing stream to a new one, while applying any needed transforms to the event data being copied.

<!-- snippet: sample_scenario-copyandtransformstream-transform -->
<!-- endSnippet -->

As the new stream is produced, within the same transaction we introduce an event dictating the stream being copied to have been moved. This should serve as an indication to no longer append new events into the stream. Furthermore, it ensures that the underlying stream being copied has not changed during the copy & transform process (as we assert on the expected stream version).

<!-- snippet: sample_scenario-copyandtransformstream-streammoved -->
<!-- endSnippet -->
