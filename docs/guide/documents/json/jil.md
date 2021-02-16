# Serializing with Jil

Marten has also been tested using the [Jil library](https://github.com/kevin-montrose/Jil) for JSON serialization. While Jil is not as
flexible as Newtonsoft.Json and might be missing support for some scenarios you may encounter, it is very clearly faster than Newtonsoft.Json.

To use Jil inside of Marten, add a class to your system like this one that implements the `ISerializer` interface:

<<< @/../src/Marten.Testing/performance_tuning.cs#sample_JilSerializer

Next, replace the default `ISerializer` when you bootstrap your `DocumentStore` as in this example below:

<<< @/../src/Marten.Testing/performance_tuning.cs#sample_replacing_serializer_with_jil

See [Optimizing for Performance in Marten](http://jeremydmiller.com/2015/11/09/optimizing-for-performance-in-marten/) 
and [Optimizing Marten Part 2](http://jeremydmiller.com/2015/11/30/optimizing-marten-part-2/) for some performance comparisons 
of using Jil versus Newtonsoft.Json for serialization within Marten operations.
