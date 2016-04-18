<!--title:Precompiling Marten Code for Faster Initialization-->

Marten is using code generation and [runtime compilation using Roslyn](http://jeremydmiller.com/2015/11/11/using-roslyn-for-runtime-code-generation-in-marten/) for the dynamic elements of handling specific documents. That technique has been very effective and we would argue that it is
much more approachable than the older techniques of building Expression trees to Func's or IL generation. Unfortunately, the first call to
Roslyn incurs a pretty significant "warm up" cost of 3-6 seconds. 

In the case where your Marten configuration is not changing, but you'd really like your application to spin up faster than Roslyn allows, Marten can now 
export the dynamic code to a file and use the pre-compiled `IDocumentStorage` classes at runtime in place of the Roslyn compilation.

## Exporting the Dynamic Storage Code

To dump the storage code that would normally be compiled at runtime by Roslyn, build an `IDocumentStore` exactly the way it should be configured for
your system, and call the `IDocumentStore.Advanced.WriteStorageCode()` method like this:

<[sample:exporting_the_storage_code]>

Once that code is written to a file, you should be able to include it into your .Net project.

Even outside of pre-compiling, you may find it valuable to export the dynamic code just to understand what Marten is doing internally.


## Using Pre-built Storage Objects

The other half of pre-compiling storage objects is to direct Marten to try to use precompiled classes wherever possible:

<[sample:import-document-storage-from-an-assembly]>

Behind the scenes, the helper method shown above for importing storage types is just finding and adding storage types to the `StoreOptions.PreBuiltStorage` list. You can also add storage types directly to the `StoreOptions` object. This may end up being a standard extension mechanism for Marten.

## Forcing Marten to Generate Code Upfront

You can also force Marten to generate the dynamic code for each registered document type upfront with this call:

<[sample:pregenerate_storage_code]>