<!--Title: Custom Projections-->

## Multistream Projections using ViewProjection 
 
The `ViewProjection` class is an implementation of the `IProjection` that can handle building a projection from multiple streams. 
 
This can be setup from configuration like: 
 
<[sample:viewprojection-from-configuration]> 
 
or through a class like: 
 
<[sample:viewprojection-from-class]> 
 

`ProjectEvent` by default takes two parameters: 
* property from event that will be used as projection document selector,
* apply method that describes the projection by itself.

`DeleteEvent` takes the first parameter - as by the nature of this method it's only needed to select which document should be deleted.

Both methods may also select multiple Ids:
* `ProjectEvent` if a `List<TId>` is passed, the handler method will be called for each Id in the collection. 
* `DeleteEvent` if a `List<TId>` is passed, then each document tied to the Id in the collection will be removed. 

Each of these methods take various overloads that allow selecting the Id field implicitly, through a property or through two different Funcs `Func<IDocumentSession, TEvent, TId>` and `Func<TEvent, TId>`. 

<div class="alert alert-warning">
<b><u>Warning:</u></b>
<br />
Projection class needs to have <b>Id</b> property with public getter or property marked with <b>Identity</b> attribute.
<br /><br />
It comes of the way how Marten handles projection mechanism:
<br />
<ol>
<li>Try to find document that has the same <b>Id</b> as the value of the property selected from event (so eg. for <b>UserCreated</b> event it will be <b>UserId</b>).</li>
<li>
    If such document exists, then new record needs to be created. Marten by default tries to use <b>default constructor</b>. <br />
    Default constructor doesn't have to be public, might be also private or protected. <br />
    If class does not have the default constructor then it creates an uninitialized object (see <a href="https://docs.microsoft.com/en-us/dotnet/api/system.runtime.serialization.formatterservices.getuninitializedobject?view=netframework-4.8" target="_parent">more</a>).<br />
    Because of that, no member initializers will be run so all of them need to be initialized in the event handler methods.
</li>
<li>If document with such <b>Id</b> was found then it's being loaded from database.</li>
<li>Document is updated with the defined in <b>ViewProjection</b> logic (using expression from second <b>ProjectEvent</b> parameter).</li>
<li>Created or updated document is upserted to database.</li>
</div>

### Using event meta data 
 
If additional Marten event details are needed, then events can use the `ProjectionEvent<>` generic when setting them up with `ProjectEvent`. `ProjectionEvent` exposes the Marten Id, Version, Timestamp and Data.

<[sample:viewprojection-from-class-with-eventdata]>


### Injecting helpers classes

ViewProjections instances are created (by default) during the `DocumentStore` initialization. Marten gives also possible to register them with factory method. With such registration projections are created on runtime during the events application. Thanks to that it's possible to setup custom creation logic or event connect dependency injection mechanism.

<[sample:viewprojection-from-class-with-injection-configuration]> 

By convention it's needed to provide the default constructor with projections definition and other with code injection (that calls the default constructor).

<[sample:viewprojection-from-class-with-injection]> 


### Using async projections

It's also possible to use async version of `ProjectEvent`. Using `ProjectEventAsync` gives possibility to call the async apis (from Marten or other frameworks) to get better resources utilization. 

Sample usage could be loading other document/projection to create denormalized view.

<[sample:viewprojection-from-class-async-with-load]> 
