// As XUnity cannot easily handle cancellation token passing
// without additional boilerplate then let's disable this rule

[assembly:
    System.Diagnostics.CodeAnalysis.SuppressMessage("Usage",
        "MA0032:Use an overload with a CancellationToken argument")]
