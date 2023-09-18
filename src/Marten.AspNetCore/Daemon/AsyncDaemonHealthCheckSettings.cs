#nullable enable

namespace Marten.Events.Daemon.Internal;

/// <summary>
/// Internal class used to DI settings to async daemon health check
/// </summary>
/// <param name="MaxEventLag"></param>
/// <returns></returns>
public record AsyncDaemonHealthCheckSettings(int MaxEventLag);
