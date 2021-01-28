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
    If no such document exists, then a new record needs to be created. Marten by default tries to use the <b>default constructor</b>. <br />
    The default constructor doesn't have to be public, it can also be private or protected. <br />
    If the class does not have a default constructor then it creates an uninitialized object (see <a href="https://docs.microsoft.com/en-us/dotnet/api/system.runtime.serialization.formatterservices.getuninitializedobject?view=netframework-4.8" target="_parent">more</a>).<br />
    Because of that, no member initializers will be run so all of them need to be initialized in the event handler methods.
</li>
<li>If a document with such <b>Id</b> was found then it's being loaded from database.</li>
<li>Document is updated with logic defined in the <b>ViewProjection</b> (using expression from second <b>ProjectEvent</b> parameter).</li>
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

<div class="alert alert-warning">
<b><u>Warning:</u></b>
<br />
Note the "async projections" term in this context means that they are using the .NET async/await mechanism that helps to use threads efficiently without locking them. <br />
It does not refer to async projections as eventually consistent. Such option provides <[linkto:documentation/events/projections/async_daemon;title=Async Daemon]>.
</div>

### Update only projection

ProjectEvent overloads contain additional boolean parameter <b>onlyUpdate</b>. By default, it's set to false which mean that Marten will do create or update operation with projection view.
<br /><br />
Lets' look on the following scenario of the projection that manages the newsletter Subscription.<br />
1. New reader subscribed to newsletter and <i>ReaderSubscribed</i> event was published. Projection handles the event and creates new view record in database. <br />
2. User opened newsletter and <i>NewsletterOpened</i> event was published. Projection handles the event and updates view in database with incremented opens count. <br />
3. User unsubscribed from newsletter and <i>ReaderUnsubscribed</i> event was published. Projection removed the view from database (because we market it with `DeleteEvent`). <br />
4. User opened newsletter after unsubscribing and <i>NewsletterOpened</i> event was published. As there is no record in database if we use the default behaviour then new record will be created with only data that are applied for the <i>NewsletterOpened</i> event. That's might create views with unexpected state. <u>In that case, <b>onlyUpdate</b> set to <b>true</b> should be used. Having that, if the view does not exist then the event will not be projected and new view record will not be created in database.</u> <br />

<[sample:viewprojection-with-update-only]> 

