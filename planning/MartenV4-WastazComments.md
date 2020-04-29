# Projections

## Building projections
While its nice that Marten provides several options for actually constructing projections (`ITransform`, `AggregateStreamWith`, `ViewProjection`, 
raw `IProjection`, etc...honestly Ive probably forgotten some way of doing it) in the end the ones that I end up using 99% of the time is
either `ViewProjection` or implementing the raw `IProjection`. The main reason for this is that most of the other options are too limiting
when building projections. 

Working with `ViewProjection` is quite nice, however occasionally the number of overloads can make it hard because its absolutely not hard
to make the compiler confused over which overload you are actually trying to use. The main thing that I cant do with `ViewProjection` which
often is the thing that makes me drop down to `IProjection` is project an event to multiple documents, which is something that can be very useful
for solving some problems. 

Working with `IProjection` often feels very raw, and I have to admit that I still havent figured out the actual difference between `Apply` and `ApplyAsync`
because it seems like they are...executed depending on if the projection runs inline or via the async daemon? Thats just confusing tbh.

Also (and now we are getting ahead of myself), projections depend on mutating state. I think this is fine, even fine as a default. But I do wish that
there would be an option to return an entire new object instead of mutating. This would also help a lot in the F# world.

## Running projections
Its great that Marten supports both running the projections inline and async. While Im suspecting that some really hardcore cqrs/es people
balk at the prospect of inline projections, they are a great option to have in some cases even though I try hard not to overuse them.

The async daemon however, is a bit lacking (though it does its job ok). The documentation is an old blog post by Jeremy which doesnt say
that much about it tbh. And you cant shard or put up multiples of it for some sort of HA scenario. It feels a bit scary just running a single service
with the daemon in production per database. The error handling is a bit funky as well, and if you have an even that crashes a projection somewhere
in the middle of an event page then actually singling out _that single event_ to have a look at it properly can be very frustrating. I have so far been
lucky enough to find these weird events in my QA environment so I havent had to do that in production _yet_ but I do not look forward to doing that.

Replaying projections seems to be a thing that you can do, but I find the documentation lacking on this so Im a bit unsure of what actually this does
which tends to make me prefer spinning up a new projection in parallel and then killing off the old one when it is in sync. It feels safer. But maybe it isnt?

## Unit testing projections
Projections should be easy to unit test in theory. Apply event A, B, C verify state X, right? But the `IProjection` interface doesnt really make it
that easy, and the way that they are executed in Marten makes it...harder. In the end I have found it easier to integration test them than it is to 
unit test them but even while I am not in the "integration tests are evil" camp they are more heavyweight to setup and maintain. And unit testing a
projection _should_ be a simple thing. But in Marten it is not.

## Running projections against other databases
One thing that has come up more than once is that we want to run an (async, not inline) projection from an eventstore in database X, but write the projected document
in database Y. This is ofc impossible today. Main reason for this is that we dont want to give certain apps access to the "important" database but
rather let them keep their own little sandbox. This could maybe be solved with schemas as well, but then they are still on the same database potentially
causing load there that we dont want. To be honest, I think this might not necessarily be the easiest problem to solve robustly.


# Integrating with an event bus

This is a bit of a special topic but I think that Im not alone in doing this so I think its worth putting my thoughts down on this topic as well. 
We use MassTransit and RabbitMq and we have probably made _every single mistake that you can do_ publishing events on the bus. 

We started out making the classic mistake of publishing our eventsourced events on the event bus. This was of course a bad move and everyone suffered.
Eventsourced events != Eventbus events. We ended up building a separate message model where the messages that we post on the bus is enriched with additional
information taken mainly from the aggregate. We plugged this into our homegrown integration library together with a hook for mapping between the events
saved in Marten and the events published to the messagebus. This has served us very well.

However now we end up with a different discussion. The dreaded 2PC. I saw somewhere (though I cant find it again now) someone suggesting using `SessionListener` 
to publish to an event bus. _This way lies madness._ We ended up implementing the same algorithm that NServiceBus uses to "guarantee" eventual message 
publishing after saving to the eventstore. This algorithm works, but its not the best performance-wise and it in theory requires infinite retries in order for
the guarantee to actually be a guarantee. Right now we are where we are and reimplementing this part in our homegrown lib is not really on the table right
now. But if I had to implement this again knowing what I do today I would 100% certainly implement publishing to the message bus as an async projection.

Running projections off the message bus was also a thing that we tried doing for a while. It has some good parts, but also some bad parts. The main issue
is of course that you suddenly end up having to handle things like out-of-order messages, duplicated messages, not being sure of where you are in the stream
etc. Its quite a lot to keep in mind, but for some cases its quite useful. But complexity immediately rises, especially in writing the projections, and lets not
even talk about event replay. I saw that you were discussing allowing projections from a message bus, and it would be a great feature to have but be prepared
that in order to support the same type of projections as you can do today is gonna require quite a bit of infrastructure. But if you can pull it off, Id certainly be happy! ;)


# Immutable objects

Honestly, you knew this were coming. But hear me out.

## Immutable events
Events should be immutable, thats just a basic fact of eventsourcing. But due to serialization woes etc the common implementation of events are with 
`public string Fruit { get; set; }` because thats just life in C# world. Now, this "gentleman immutability" "works" but it is...not great?

### It aint immutable
Sure, if you go assign stuff to it and end up in trouble because of it thats your own fault. But I am also a firm believer in the principle of least surprise, if you shouldnt 
set a property there shouldnt be a setter for it. If a method shouldnt be in the public interface of a class then make it private or internal.

### F# support
Mutable bags of data like this is highly non-idiomatic in F#, and key to providing good F# support is going to be in part allowing for better support for 
immutable object. The normal modus operandi there would be to implement an event as a record type (which in turn is probably wrapped in a DU, but lets table that discussion for right now)

```fsharp
type MyEvent = {
  fruit : string
  numberOfFruitsBought : int
}
```

This event is immutable. By adding the `[<CLIMutable>]` attribute on it we can get public setters generated on it as well that cant be used from F#, solving some
issues with immutability and serialization but at the cost of your immutable object...not actually being immutable anymore.

## Immutable aggregates

I dont necessarily think that this should be the default way of working because there is certainly a cost to pay for allocating a lot of objects. But in F#-code or more functional-style code it would 
be a lot nicer to be able to not mutate the aggregate class but instead return a new instance of the aggregate with the current event applied to it. And honestly I think that it wouldnt be that hard to
support this mode of operation as well as the mutable way of working.


# Nullable reference types

A "nice" part of C#8 is the addition of nullable reference types. However, after working with these for a while you quickly realize that they come with a decent amount of caveats.
The number 1 realization that I _hope_ more of the .NET community is finally going to realize as they start using this feature more is that _reading data from a database is external input_.

This leads to basically two choices. Either you can make your document/event model the optimistic or the pessmistic way:

```csharp
public class NutrientInfo { /* class omitted for brevity */ }

// Optimistic
public class FruitDocument {
  public Guid Id { get; set; }
  public string Name { get; set; }
  public int Ranking { get; set; }
  public NutrientInfo { get; set; }
}

// Pessimistic
public class FruitDocument {
  public Guid Id { get; set; }
  public string? Name { get; set; }
  public int Ranking { get; set; }
  public NutrientInfo? { get; set; }
}
```

So which should you do? Honestly Id say the pessimistic one, because we are deserializing json here. And even in the optimistic one you _probably_ check the value of `Ranking` depending on
what values are valid in your domain because thats gonna be `default(int)` if its missing. Now, _this has always been a problem_ but NRTs really shove it in your face when you realize that
you probably need to mark a ton of shit as nullable in every document/event you have.

We have tried both using optimistic and pessimistic models. Pessimistic models leads to...code that you dont really want. But optimistic models however leads to either a false sense of
security or you have to force a validation step for your document after each load. Thing that we think works best so far has been building validators with FluentValidation for every event/document
type, using optimistic models and validating the documents after reading them. Its...not that fun, but it works and it lets us actually model the documents/events according to how they should look
according to the domain.

However there are some ways to make this easier.
Ive seen the discussions on the "streaming api", but I think that what may be missing from these discussions is that there can actually be tremendous alternate value in providing a way to
easily read raw documents/events as `string` or `Stream`. Because it allows an easier way of hooking in a _parser_, not a _serializer_. There's some good blog posts out there on the virtues of
parsing instead of validating (or serializing) so Im not gonna repeat the points that they already made. But with this type of streaming api you could much easier hook up a parser which would not
only help in having an easier way of constructing correct objects that respects NRTs, but could also help a lot with the F# side of this where immutability and discriminated unions are not always the
best at serializing/deserializing. You can probably do some things like that with the `ISerializer` interface today, but having a way to just bypass serialization completely is quite powerful.

# Event versioning

Event versioning is hard. Greg Youngs book on the topic almost does justice to how hard it is. Its hard. Marten doesnt really have any support for event versioning out of the box, apart from the
usual "serializer can handle putting default values on added fields". But it would certainly help to have more powerful choices here in upgrading events as they evolve. I dont have a great suggestion
on exactly how this should be done though, and sadly I dont really love any solution that I have seen in other libs/frameworks attempting to do this either. Currently I tend to write a migration reading
the events, parsing them as `JObject` or `JToken` and working with the json directly and then updating. It works. Its not great, but it works. There's certainly space here for thinking about this should be done.

# Snapshots

Snapshots are useful. The main input I have here is that _dont fall into the same trap as a lot of other ES libraries on the .NET platform_. And the trap that I am talking about is the assumption
that each stream should have one and only one singe aggregate type associated with it. Marten can currently write a stream type in `mt_streams` which sadly is generated from a .NET type name, Id
personally prefer if the stream type was an actual string input instead of generated from a .NET type to further disconnect it from an aggregate type. IMHO streams are the important things, aggregates less so.

# Grab bag of minor pains

* Different exceptions can be thrown on optimistic concurrency faults. Especially for the event store. 
  Depending on where the optimistic concurrency fails you can get one of 2-3 different exceptions back which all means that the optimistic concurrency check failed. This is not very nice tbh.
* No way of mixing stream id types, its either all strings or all guids. And what you chose when you started is what you are stuck with :(
* PLV8. Its both a blessing and a curse. Its great that its possible to disable it, but I would love to see a list on what features stop working when it is disabled so I can consider if I should cave in to ops constant complaints about running PLV8.

# Grab bag of praise

* Honestly. Marten has been great. I just feel that I have to say this after all of this. Marten has worked amazingly well for us and its in many cases a pleasure to work with.
  I keep recommending it as the default choice for persistance for basically everything in our org because its just such a safe and sane default.
* Part of the greatness of Marten which I want to praise a little bit extra is that it is truly a _library_. It doesnt overstay its welcome and its often pretty easy enough
  to reach under the hood and see whats going on and customize it. 
* And the other part of Martens secret sauce is imho how it is extremely _pragmatic_. This is a word that gets thrown around a lot, but the way that Marten lets you mix and match
  ES, documents and classic rdbms models and do things that might not always be the "purist" way of doing things (inline projections, transactions) is great for _getting things done_.
* Performance-wise, we havent had any issues yet apart from one event stream in our stage database that one of our customers was reusing over and over again for their automated testing which grew to several 100k
  events and caused everything to be _really slow_. But thats not anywhere near normal usage, and theyve been told to stop doing that...