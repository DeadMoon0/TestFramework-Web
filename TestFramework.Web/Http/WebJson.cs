using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace TestFramework.Web.Http;

/// <summary>
/// How this package reads and writes JSON on the wire.
/// </summary>
/// <remarks>
/// <para>
/// One settings object, because a request body serialised one way and a response body read another way is
/// a bug that only shows up in whichever test round-trips a value.
/// </para>
/// <para>
/// These settings reproduce what <c>JsonSerializerDefaults.Web</c> gave: camelCase property names on the
/// way out, and case-insensitive matching plus numbers-from-strings on the way in - the last two are
/// Newtonsoft's own defaults, so only the naming needs saying. The family serialises with Newtonsoft
/// everywhere (ARCHITECTURE.md section 4); two libraries mean two notions of what null means and values
/// that survive one round trip but not the other.
/// </para>
/// </remarks>
internal static class WebJson
{
    /// <summary>
    /// The settings for an API request or response body.
    /// </summary>
    internal static JsonSerializerSettings Wire { get; } = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    /// <summary>
    /// The serializer for turning a caller's object into a JSON tree.
    /// </summary>
    internal static JsonSerializer WireSerializer { get; } = JsonSerializer.Create(Wire);

}
