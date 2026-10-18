using System;
using System.Collections.Generic;
using TestFramework.Core.Exceptions;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using TestFramework.Config.Configuration;
using TestFramework.Core.Environment.Graph;
using TestFramework.Web.Configuration;

namespace TestFramework.Web.Stub.Configuration;

/// <summary>
/// The <c>Stub</c> section, as the run's resource graph sees it.
/// </summary>
/// <remarks>
/// A stub is a resource like any other as far as the run is concerned: it has an address, and it has a control
/// surface at a path on that address. Reading goes through the package's own provider so the steps and the
/// graph agree on what the entry said.
/// </remarks>
/// <param name="provider">Reads an entry, including whatever validation the caller configured.</param>
internal sealed class StubShape(IStubConfigProvider provider) : ConfigShape<StubConfig>
{
    /// <inheritdoc />
    public override string Section => DefaultStubConfigProvider.StubSelector;

    /// <inheritdoc />
    public override ResourceKind Kind => WebResourceKinds.StubKind;

    /// <inheritdoc />
    public override IReadOnlyList<string> Identifiers(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadAllStubIdentifier(configuration);
    }

    /// <inheritdoc />
    public override StubConfig Read(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadStubConfig(configuration, identifier);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The whole entry, not only its coordinates, because the run is where a step reads its configuration
    /// from now. Only the address is published per viewpoint: a path is a path on whichever address you
    /// reached the stub at, and how long a run is willing to poll does not change with where it stands.
    /// </para>
    /// <para>
    /// Written to be read back by <see cref="Read(IReadOnlyDictionary{string, string}, string)"/>, and the
    /// two are guarded together - a name written here and not read there is a value nothing can ever see.
    /// </para>
    /// </remarks>
    public override IReadOnlyDictionary<ValueKey, string> Values(StubConfig config, string identifier)
    {
        ArgumentNullException.ThrowIfNull(config);

        Dictionary<ValueKey, string> values = [];

        if (config.BaseUrl is { Length: > 0 } baseUrl)
        {
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Host)] = baseUrl;
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Network)] = baseUrl;
        }

        if (config.AdminPath is { Length: > 0 } adminPath)
        {
            values[new ValueKey(WebResourceKinds.AdminPathValue)] = adminPath;
        }

        // Round-trip formats, not display ones: a duration goes out as ISO-8601 and a flag as "true" or
        // "false", so what comes back is what went in whatever the machine's culture is.
        values[new ValueKey(WebResourceKinds.PollIntervalValue)] = config.PollInterval.ToString("c", CultureInfo.InvariantCulture);
        values[new ValueKey(WebResourceKinds.AllowInvalidCertificatesValue)] = config.AllowInvalidCertificates ? "true" : "false";
        values[new ValueKey(WebResourceKinds.ResetModeValue)] = config.ResetMode.ToString();

        return values;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Every value is optional except the address, and an absent one leaves the record's own default in
    /// place - which is what keeps §5 true: the default is documented where the value is introduced, and
    /// this does not restate it.
    /// </para>
    /// <para>
    /// A value that is present and unparseable is a refusal rather than a silent fall back to the default.
    /// Something put it there deliberately, and quietly ignoring it would mean a run behaving differently
    /// from what it was told with nothing to show why.
    /// </para>
    /// </remarks>
    public override StubConfig Read(IReadOnlyDictionary<string, string> values, string identifier)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (!values.TryGetValue(ValueNames.BaseUrl, out string? baseUrl) || string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new FrameworkConfigurationException(
                $"The run has no address for stub '{identifier}'.")
            {
                RecoverySteps = [
                    $"Set 'Stub:{identifier}:BaseUrl', or include the stub definition that starts one.",
                ],
                AvailableOptions = [.. values.Keys],
            };
        }

        StubConfig config = new StubConfig { BaseUrl = baseUrl };

        if (values.TryGetValue(WebResourceKinds.AdminPathValue, out string? adminPath) && adminPath is { Length: > 0 })
        {
            config = config with { AdminPath = adminPath };
        }

        if (values.TryGetValue(WebResourceKinds.PollIntervalValue, out string? pollInterval))
        {
            config = config with { PollInterval = ParseTimeSpan(pollInterval, identifier, WebResourceKinds.PollIntervalValue) };
        }

        if (values.TryGetValue(WebResourceKinds.AllowInvalidCertificatesValue, out string? allowInvalid))
        {
            config = config with { AllowInvalidCertificates = ParseBool(allowInvalid, identifier, WebResourceKinds.AllowInvalidCertificatesValue) };
        }

        if (values.TryGetValue(WebResourceKinds.ResetModeValue, out string? resetMode))
        {
            config = config with { ResetMode = ParseResetMode(resetMode, identifier) };
        }

        return config;
    }

    private static TimeSpan ParseTimeSpan(string value, string identifier, string valueName)
        => ConfigValueRules.PositiveDuration(value, out TimeSpan parsed)
            ? parsed
            : throw Unparseable(value, identifier, valueName, "a positive duration such as '00:00:00.250'");

    private static bool ParseBool(string value, string identifier, string valueName)
        => bool.TryParse(value, out bool parsed)
            ? parsed
            : throw Unparseable(value, identifier, valueName, "'true' or 'false'");

    private static StubResetMode ParseResetMode(string value, string identifier)
        => ConfigValueRules.DefinedEnum(value, out StubResetMode parsed)
            ? parsed
            : throw Unparseable(value, identifier, WebResourceKinds.ResetModeValue, string.Join(" or ", Enum.GetNames<StubResetMode>()));

    private static FrameworkConfigurationException Unparseable(string value, string identifier, string valueName, string expected)
        => new FrameworkConfigurationException(
            $"The run holds '{value}' as {valueName} for stub '{identifier}', which cannot be read as {expected}.")
        {
            RecoverySteps = [
                $"Correct 'Stub:{identifier}:{valueName}', or the environment component that publishes it.",
            ],
        };
}
