# Json Serialization

An absolutely essential ingredient in Marten's persistence strategy is JSON serialization of the document objects. Marten aims to make the
JSON serialization extensible and configurable through the native mechanisms in each JSON serialization library. For the purposes of having
a smooth "getting started" story, Marten comes out of the box with support for a very basic usage of Newtonsoft.Json as the main JSON serializer.

Internally, Marten uses an adapter interface for JSON serialization:

<!-- snippet: sample_ISerializer -->
<!-- endSnippet -->

To support a new serialization library or customize the JSON serialization options, you can write a new version of `ISerializer` and plug it
into the `DocumentStore` (there's an example of doing that in the section on using Jil).
