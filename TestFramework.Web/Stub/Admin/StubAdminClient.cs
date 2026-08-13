using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Web.Stub.Exceptions;

namespace TestFramework.Web.Stub.Admin;

/// <summary>
/// One request a stub received.
/// </summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path that was requested.</param>
/// <param name="Query">The raw query string, without the leading question mark.</param>
/// <param name="Body">The request body as text, when there was one.</param>
/// <param name="Headers">The request headers.</param>
/// <param name="ReceivedAt">When the stub received it.</param>
/// <param name="Matched">Whether a mapping answered it. An unmatched call is a call the test never declared.</param>
public sealed record StubCall(
    string Method,
    string Path,
    string? Query,
    string? Body,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset? ReceivedAt,
    bool Matched)
{
    /// <summary>
    /// Returns a readable description of the call.
    /// </summary>
    public override string ToString() => $"{Method} {Path}{(Matched ? string.Empty : " (unmatched)")}";
}

/// <summary>
/// Reads and clears a stub server's request log.
/// </summary>
/// <remarks>
/// Verification goes through the server's own administration surface rather than through a callback,
/// so it works the same whether the stub runs in this process or somewhere else entirely.
/// </remarks>
public sealed class StubAdminClient(HttpClient client, string adminPath)
{
    /// <summary>
    /// Returns every request the stub has received, oldest first.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the running step.</param>
    /// <exception cref="StubAdminException">The administration surface could not be read.</exception>
    public async Task<IReadOnlyList<StubCall>> GetCallsAsync(CancellationToken cancellationToken)
    {
        string payload = await ReadAsync($"{Trimmed}/requests", cancellationToken).ConfigureAwait(false);

        JsonNode? node = Parse(payload);
        if (node is not JsonArray entries)
            return [];

        List<StubCall> calls = [.. entries.Select(ToCall).Where(call => call is not null).Select(call => call!)];
        calls.Sort((left, right) => Nullable.Compare(left.ReceivedAt, right.ReceivedAt));
        return calls;
    }

    /// <summary>
    /// Returns the mappings the stub actually loaded.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the running step.</param>
    /// <remarks>
    /// Worth reading when a stub "does not work": a mapping the server rejected is simply absent.
    /// </remarks>
    /// <exception cref="StubAdminException">The administration surface could not be read.</exception>
    public async Task<int> GetMappingCountAsync(CancellationToken cancellationToken)
    {
        string payload = await ReadAsync($"{Trimmed}/mappings", cancellationToken).ConfigureAwait(false);
        return Parse(payload) is JsonArray mappings ? mappings.Count : 0;
    }

    /// <summary>
    /// Clears the request log.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the running step.</param>
    /// <exception cref="StubAdminException">The administration surface could not be reached.</exception>
    public async Task ResetCallsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.DeleteAsync(new Uri($"{Trimmed}/requests", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw StubAdminException.UnexpectedStatus(client.BaseAddress, $"{Trimmed}/requests", (int)response.StatusCode);
        }
        catch (HttpRequestException exception)
        {
            throw StubAdminException.Unreachable(client.BaseAddress, exception);
        }
    }

    private string Trimmed => adminPath.TrimEnd('/').TrimStart('/');

    private async Task<string> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw StubAdminException.UnexpectedStatus(client.BaseAddress, path, (int)response.StatusCode);

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw StubAdminException.Unreachable(client.BaseAddress, exception);
        }
    }

    private static JsonNode? Parse(string payload)
    {
        try
        {
            return JsonNode.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw StubAdminException.UnreadablePayload(exception);
        }
    }

    private static StubCall? ToCall(JsonNode? entry)
    {
        if (entry is not JsonObject logEntry || logEntry["Request"] is not JsonObject request)
            return null;

        return new StubCall(
            ReadString(request, "Method") ?? "GET",
            ReadString(request, "Path") ?? "/",
            ReadString(request, "Query") ?? ReadString(request, "RawQuery"),
            ReadBody(request),
            ReadHeaders(request),
            ReadTimestamp(request),
            ReadMatched(logEntry));
    }

    private static bool ReadMatched(JsonObject logEntry)
    {
        // A log entry that names no mapping is a request nothing answered.
        if (logEntry["MappingGuid"] is JsonValue mapping && mapping.TryGetValue(out string? guid) && !string.IsNullOrWhiteSpace(guid))
            return true;

        if (logEntry["RequestMatchResult"] is JsonObject match
            && match["IsPerfectMatch"] is JsonValue flag
            && flag.TryGetValue(out bool isPerfectMatch))
        {
            return isPerfectMatch;
        }

        return false;
    }

    private static string? ReadString(JsonObject source, string name)
        => source[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static string? ReadBody(JsonObject request)
        => ReadString(request, "Body") ?? request["BodyAsJson"]?.ToJsonString();

    private static DateTimeOffset? ReadTimestamp(JsonObject request)
    {
        string? raw = ReadString(request, "DateTime");
        return DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out DateTimeOffset parsed) ? parsed : null;
    }

    private static IReadOnlyDictionary<string, string> ReadHeaders(JsonObject request)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (request["Headers"] is not JsonObject source)
            return headers;

        foreach ((string name, JsonNode? value) in source)
        {
            headers[name] = value switch
            {
                JsonArray array => string.Join(", ", array.Select(item => item?.ToString() ?? string.Empty)),
                null => string.Empty,
                _ => value.ToString(),
            };
        }

        return headers;
    }
}
