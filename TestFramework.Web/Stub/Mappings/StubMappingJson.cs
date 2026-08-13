using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TestFramework.Web.Stub.Mappings;

/// <summary>
/// Writes mappings in the JSON format a WireMock server loads.
/// </summary>
/// <remarks>
/// The declaration is turned into data rather than into calls against a server library, so the same
/// mappings can be handed to a stub running in another process, in a container, or on another
/// machine.
/// </remarks>
public static class StubMappingJson
{
    private const string WildcardMatcher = "WildcardMatcher";

    /// <summary>
    /// Writes one mapping as indented JSON.
    /// </summary>
    /// <param name="mapping">The mapping to write.</param>
    public static string Write(StubMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        return ToNode(mapping).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Writes several mappings as one indented JSON array.
    /// </summary>
    /// <param name="mappings">The mappings to write, in declaration order.</param>
    public static string WriteAll(IEnumerable<StubMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        JsonArray array = [];
        foreach (StubMapping mapping in mappings)
            array.Add(ToNode(mapping));

        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject ToNode(StubMapping mapping)
    {
        JsonObject request = new()
        {
            ["Path"] = mapping.Path,
            ["Methods"] = new JsonArray(mapping.Method),
        };

        if (mapping.Headers.Count > 0)
            request["Headers"] = ToMatcherArray(mapping.Headers, ignoreCase: true);

        if (mapping.Query.Count > 0)
            request["Params"] = ToMatcherArray(mapping.Query, ignoreCase: false);

        if (!string.IsNullOrEmpty(mapping.BodyContains))
        {
            request["Body"] = new JsonObject
            {
                ["Matchers"] = new JsonArray(Matcher($"*{mapping.BodyContains}*", ignoreCase: false)),
            };
        }

        JsonObject response = new()
        {
            ["StatusCode"] = mapping.Response.StatusCode,
        };

        if (mapping.Response.Headers.Count > 0)
        {
            JsonObject headers = [];
            foreach ((string name, string value) in mapping.Response.Headers)
                headers[name] = value;

            response["Headers"] = headers;
        }

        if (mapping.Response.BodyAsJson is { } bodyAsJson)
            response["BodyAsJson"] = bodyAsJson.DeepClone();
        else if (mapping.Response.Body is { } body)
            response["Body"] = body;

        if (mapping.Response.UseTemplating)
            response["UseTransformer"] = true;

        if (mapping.Response.Delay is { } delay)
            response["Delay"] = (int)delay.TotalMilliseconds;

        return new JsonObject
        {
            ["Priority"] = mapping.Priority,
            ["Title"] = $"{mapping.Method} {mapping.Path}",
            ["Request"] = request,
            ["Response"] = response,
        };
    }

    private static JsonArray ToMatcherArray(IReadOnlyDictionary<string, string> values, bool ignoreCase)
    {
        JsonArray array = [];
        foreach ((string name, string pattern) in values)
        {
            array.Add(new JsonObject
            {
                ["Name"] = name,
                ["Matchers"] = new JsonArray(Matcher(pattern, ignoreCase)),
            });
        }

        return array;
    }

    private static JsonObject Matcher(string pattern, bool ignoreCase) => new()
    {
        ["Name"] = WildcardMatcher,
        ["Pattern"] = pattern,
        ["IgnoreCase"] = ignoreCase,
    };

    /// <summary>
    /// Returns the file name a mapping is written to inside a stub server's mapping folder.
    /// </summary>
    /// <param name="identifier">The stub identifier.</param>
    /// <param name="index">The declaration index.</param>
    public static string FileName(string identifier, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"{identifier}-{index.ToString("D3", CultureInfo.InvariantCulture)}.json";
    }
}
