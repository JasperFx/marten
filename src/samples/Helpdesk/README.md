# Helpdesk Sample

- Simplest CQRS and Event Sourcing flow using Minimal API,
- Cutting the number of layers to bare minimum,
- Using all Marten helpers like `WriteToAggregate`, `AggregateStream` to simplify the processing,
- Examples of all the typical Marten's projections,
- example of how and where to use C# Records, Nullable Reference Types, etc,
- No Aggregates! Commands are handled in the domain service as pure functions.

You can watch the webinar on YouTube where I'm explaining the details of the implementation:

<a href="https://www.youtube.com/watch?v=jnDchr5eabI&list=PLw-VZz_H4iiqUeEBDfGNendS0B3qIk-ps&index=1" target="_blank"><img src="https://img.youtube.com/vi/jnDchr5eabI/0.jpg" alt="Practical Event Sourcing with Marten and .NET" width="640" height="480" border="10" /></a>

or read the articles explaining this design:

- [Slim your aggregates with Event Sourcing!](https://event-driven.io/en/slim_your_entities_with_event_sourcing/?utm_source=event_sourcing_net)
- [Event-driven projections in Marten explained](https://event-driven.io/pl/projections_in_marten_explained/?utm_source=event_sourcing_net)

It's a clone of the original sample from @oskardudycz [Event Sourcing .NET repository](https://github.com/oskardudycz/EventSourcing.NetCore/tree/main/Sample/Helpdesk).
