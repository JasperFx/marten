<!--Title:Json Serialization-->
<!--Url:json-->

An absolutely essential ingredient in Marten's persistence strategy is JSON serialization of the document objects. Marten aims to make the
JSON serialization extensible and configurable through the native mechanisms in each JSON serialization library.

Internally, Marten uses an adapter interface for JSON serialization:

<[sample:ISerializer]>

To support a new serialization library or customize the JSON serialization options, you can write a new version of `ISerializer` and plug it
into the `DocumentStore` (there's an example of doing that in the section on using Jil).

## Serializing with Newtonsoft.Json

The default JSON serialization strategy inside of Marten uses [Newtonsoft.Json](http://www.newtonsoft.com/json). We have standardized on Newtonsoft.Json
because of its flexibility and ability to handle polymorphism within child collections and eventually [document hierarchies](https://github.com/JasperFx/Marten/issues/44).


## Serializing with Jil

Marten has also been tested using the [Jil library](https://github.com/kevin-montrose/Jil) for JSON serialization. While Jil is not as
flexible as Newtonsoft.Json and might be missing support for some scenarios you may encounter, it is very clearly faster than Newtonsoft.Json.

To use Jil inside of Marten, add a class to your system like this one that implements the `ISerializer` interface:

<[sample:JilSerializer]>

Next, replace the default `ISerializer` when you bootstrap your `DocumentStore` as in this example below:

<[sample:replacing_serializer_with_jil]>

See [Optimizing for Performance in Marten](http://jeremydmiller.com/2015/11/09/optimizing-for-performance-in-marten/) 
and [Optimizing Marten Part 2](http://jeremydmiller.com/2015/11/30/optimizing-marten-part-2/) for some performance comparisons 
of using Jil versus Newtonsoft.Json for serialization within Marten operations.
