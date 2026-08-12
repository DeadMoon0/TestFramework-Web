using System;
using System.Collections.Generic;
using System.Net;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Exceptions;

/// <summary>
/// Thrown when a response body cannot be read as the requested type.
/// </summary>
public sealed class ApiResponseFormatException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception describing a body that could not be deserialized.
    /// </summary>
    /// <param name="targetType">The type the body was being read as.</param>
    /// <param name="statusCode">The status code of the response.</param>
    /// <param name="contentType">The content type reported by the response.</param>
    /// <param name="bodyExcerpt">A truncated excerpt of the response body.</param>
    /// <param name="innerException">The underlying deserialization exception, when any.</param>
    /// <returns>The exception describing the format mismatch.</returns>
    public static ApiResponseFormatException CannotDeserialize(Type targetType, HttpStatusCode statusCode, string? contentType, string? bodyExcerpt, Exception? innerException = null)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        return new ApiResponseFormatException(
            $"The response body could not be read as '{targetType.Name}'. Status was {(int)statusCode} {statusCode}, content type was '{contentType ?? "(none)"}'.",
            [
                "Assert the status code before reading the body; error responses rarely use the success schema.",
                $"Confirm the API returns JSON that maps to '{targetType.Name}'.",
                "Use Body to inspect the raw payload when the shape is unexpected.",
            ],
            bodyExcerpt is null ? null : [bodyExcerpt],
            innerException);
    }

    private ApiResponseFormatException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions, Exception? innerException)
        : base(friendlyMessage, recoverySteps, availableOptions, innerException)
    {
    }
}
