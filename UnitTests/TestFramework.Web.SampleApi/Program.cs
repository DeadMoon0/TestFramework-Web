using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using TestFramework.Web.SampleApi;

// The sample API is normally started by the test fixture on an ephemeral port. Running it directly
// is useful when exploring the endpoints by hand.
await using WebApplication app = SampleApiHost.Create();
await app.RunAsync().ConfigureAwait(false);
