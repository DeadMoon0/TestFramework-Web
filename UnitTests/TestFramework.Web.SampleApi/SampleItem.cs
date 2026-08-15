namespace TestFramework.Web.SampleApi;

/// <summary>
/// Item resource served by the sample API.
/// </summary>
/// <param name="Id">The item identifier.</param>
/// <param name="Name">The item name.</param>
/// <param name="Quantity">The item quantity.</param>
public sealed record SampleItem(string Id, string Name, int Quantity);

/// <summary>
/// Payload accepted when creating an item.
/// </summary>
/// <param name="Name">The item name.</param>
/// <param name="Quantity">The item quantity.</param>
public sealed record CreateSampleItem(string Name, int Quantity);

/// <summary>
/// Reports which cookies reached the sample API on a request.
/// </summary>
/// <param name="SessionCookie">Value of the session cookie, or <c>null</c> when it was not sent.</param>
/// <param name="HadCookieHeader">Whether any <c>Cookie</c> header arrived at all.</param>
public sealed record CookieEchoResponse(string? SessionCookie, bool HadCookieHeader);
