<!--Title:Compiled Queries-->
<!--Url:saved_queries-->

Marten doesn't support anything yet, but we've talked about possibly supporting something like EF's [Compiled Queries](https://msdn.microsoft.com/library/bb896297(v=vs.100).aspx).

The point is just to avoid the extra cost of parsing Expression trees and building up SQL strings for a query that's going to be frequently used.

See the [GitHub issue for any activity](https://github.com/JasperFx/Marten/issues/25).

There's also a chance that Marten will eventually be an EF7 plugin to get this functionality.
