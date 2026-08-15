namespace TestFramework.Web.Stub;

/// <summary>
/// How <c>WebExt.Stub.Reset(...)</c> separates this run's calls from everything already logged.
/// </summary>
/// <remarks>
/// A stub server's request log is global. Which of the two modes is correct depends entirely on
/// whether the run owns the stub or shares it.
/// </remarks>
public enum StubResetMode
{
    /// <summary>
    /// Records the newest logged timestamp and ignores everything at or before it. Nothing is deleted.
    /// </summary>
    /// <remarks>
    /// The default, because it is the only mode that is safe on a stub other runs are using. The
    /// watermark is read from the stub's own clock, so no clock skew between test host and stub can
    /// distort it. Calls the stub logged without a timestamp stay in scope: they cannot be placed
    /// relative to the watermark, and silently dropping evidence would be worse than keeping too
    /// much. Concurrent runs against one stub still see each other's calls — only a stub this run
    /// owns gives fully isolated evidence.
    /// </remarks>
    Watermark = 0,

    /// <summary>
    /// Deletes the stub's request log on the server.
    /// </summary>
    /// <remarks>
    /// Gives the cleanest evidence and is the right choice for a stub this run owns. On a shared
    /// stub it destroys other runs' evidence, which is why it is not the default.
    /// </remarks>
    ClearServerLog = 1,
}
