# Aggregates, Events, Repositories

This use case demonstrates how to capture state changes in events and then replaying that state from the database. This is done by first introducing some supporting infrastructure, then implementing a model of invoice, together with invoice lines, on top of that.

## Scenario

To model, capture and replay the state of an object through events, some infrastructure is established to dispatch events to their respective handlers. This is demonstrated in the `AggregateBase` class below - it serves as the basis for objects whose state is to be modeled.

<<< @/../src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#sample_scenario-aggregate-base

With the first piece of infrastructure implemented, two events to capture state changes of an invoice are introduced. Namely, creation of an invoice, accompanied by an invoice number, and addition of lines to an invoice:

<<< @/../src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#sample_scenario-aggregate-events

With the events in place to present the deltas of an invoice, an aggregate is implemented, using the infrastructure presented above, to create and replay state from the described events.

<<< @/../src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#sample_scenario-aggregate-invoice

The implemented invoice protects its state by not exposing mutable data, while enforcing its contracts through argument validation. Once an applicable state modification is introduced, either through the constructor (which numbers our invoice and captures that in an event) or the `Invoice.AddLine` method, a respective event capturing that data is recorded.

Lastly, to persist the deltas described above and to replay the state of an object from such persisted data, a repository is implemented. The said repository pushes the deltas of an object to event stream, indexed by the ID of the object.

<<< @/../src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#sample_scenario-aggregate-repository

With the last infrastructure component in place, versioned invoices can now be created, persisted and hydrated through Marten. For this purpose, first an invoice is created:

<<< @/../src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#sample_scenario-aggregate-createinvoice

Then, with an instantiated & configured Document Store (in this case with string as event stream identity) a repository is bootstrapped. The newly created invoice is then passed to the repository, which pushes the deltas to the database and clears them from the to-be-committed list of changes. Once persisted, the invoice data is replayed from the database and verified to match the data of the original item.

<<< @/../src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#sample_scenario-aggregate-storeandreadinvoice

With this infrastructure in place and the ability to model change as events, it is also possible to replay back any previous state of the object. For example, it is possible to see what the invoice looked with only the first line added:

<<< @/../src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#sample_scenario-aggregate-versionedload

Lastly, to prevent our invoice from getting into a conflited state, the version attribute of the item is used to assert that the state of the object has not changed between replaying its state and introducing new deltas:

<<< @/../src/Marten.Testing/Events/ScenarioAggregateAndRepository.cs#sample_scenario-aggregate-conflict
