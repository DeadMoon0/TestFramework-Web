using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;

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
        return ToNode(mapping).ToString(Formatting.Indented);
    }

    /// <summary>
    /// Writes several mappings as one indented JSON array.
    /// </summary>
    /// <param name="mappings">The mappings to write, in declaration order.</param>
    public static string WriteAll(IEnumerable<StubMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        JArray array = [];
        foreach (StubMapping mapping in mappings)
            array.Add(ToNode(mapping));

        return array.ToString(Formatting.Indented);
    }

    private static JObject ToNode(StubMapping mapping)
    {
        JObject request = new()
        {
            ["Path"] = mapping.Path,
            ["Methods"] = new JArray(mapping.Method),
        };

        if (mapping.Headers.Count > 0)
            request["Headers"] = ToMatcherArray(mapping.Headers, ignoreCase: true);

        if (mapping.Query.Count > 0)
            request["Params"] = ToMatcherArray(mapping.Query, ignoreCase: false);

        if (!string.IsNullOrEmpty(mapping.BodyContains))
        {
            request["Body"] = new JObject
            {
                ["Matchers"] = new JArray(Matcher($"*{mapping.BodyContains}*", ignoreCase: false)),
            };
        }

        JObject response = new()
        {
            ["StatusCode"] = mapping.Response.StatusCode,
        };

        if (mapping.Response.Headers.Count > 0)
        {
            JObject headers = [];
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

        return new JObject
        {
            ["Priority"] = mapping.Priority,
            ["Title"] = $"{mapping.Method} {mapping.Path}",
            ["Request"] = request,
            ["Response"] = response,
        };
    }

    private static JArray ToMatcherArray(IReadOnlyDictionary<string, string> values, bool ignoreCase)
    {
        JArray array = [];
        foreach ((string name, string pattern) in values)
        {
            array.Add(new JObject
            {
                ["Name"] = name,
                ["Matchers"] = new JArray(Matcher(pattern, ignoreCase)),
            });
        }

        return array;
    }

    private static JObject Matcher(string pattern, bool ignoreCase) => new()
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
