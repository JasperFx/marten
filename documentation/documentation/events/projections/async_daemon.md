<!--Title: Async Projections Daemon-->

For the most information, see Jeremy's blog post on [Offline Event Processing in Marten with the new “Async Daemon”](https://jeremydmiller.com/2016/08/04/offline-event-processing-in-marten-with-the-new-async-daemon/).

## Rebuilding Projections

Projections need to be rebuilt when the code that defines them changes in a way that requires events to be reapplied in order to maintain correct state. Using an `IDaemon` this is easy to execute on-demand:

<[sample:rebuild-single-projection]>
