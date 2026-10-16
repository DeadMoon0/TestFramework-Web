using System;
using System.Net.Http;
using TestFramework.Core.Steps;
using TestFramework.Web.Stub;
using TestFramework.Web.Stub.Steps;

namespace TestFramework.Web;

public static partial class WebExt
{
    /// <summary>
    /// Creates steps that observe a stubbed dependency.
    /// </summary>
    /// <remarks>
    /// A stub is asserted on through its own request log, so these steps say what the application under
    /// test actually sent outwards -- the half of its behaviour a response body cannot show.
    /// </remarks>
    public class StubProxy
    {
        /// <summary>
        /// Waits until the stub receives a call.
        /// </summary>
        /// <param name="identifier">The stub identifier.</param>
        /// <param name="method">The method the awaited call must have.</param>
        /// <param name="path">The path the awaited call must have.</param>
        /// <returns>An event, bounded by the step timeout.</returns>
        public StubCalledEvent Called(StubIdentifier identifier, HttpMethod method, string path)
        {
            // Refused rather than read as GET: a missing method is a mistake in the timeline, and waiting for GET
            // instead waits, until the step's timeout, for a call the test never meant.
            ArgumentNullException.ThrowIfNull(method);

            return new(identifier, method.Method, path);
        }

        /// <summary>
        /// Reads the calls the stub has received so far.
        /// </summary>
        /// <param name="identifier">The stub identifier.</param>
        /// <returns>The step, which <c>WithMethod(...)</c>, <c>WithPath(...)</c> and <c>WithHeader(...)</c> narrow.</returns>
        public StubCallsStep Calls(StubIdentifier identifier)
            => new(identifier);

        /// <summary>
        /// Starts this run's observation window on the stub, so later observations only see what
        /// happened after this point.
        /// </summary>
        /// <param name="identifier">The stub identifier.</param>
        /// <remarks>
        /// By default nothing is deleted: the newest logged timestamp is recorded and everything at or
        /// before it is ignored, which is the only safe behaviour on a stub other runs share. Set
        /// <c>ResetMode</c> to <see cref="StubResetMode.ClearServerLog"/> for a stub this run owns.
        /// </remarks>
        public Step<StubCallsResult> Reset(StubIdentifier identifier) => new StubResetStep(identifier);
    }
}
