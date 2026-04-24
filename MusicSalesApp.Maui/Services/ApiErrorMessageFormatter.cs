using System.Net;
using System.Text.Json;

namespace MusicSalesApp.Maui.Services;

internal static class ApiErrorMessageFormatter
{
    private const int MaxMessageLength = 300;

    internal static async Task<string> ReadDisplayMessageAsync(HttpResponseMessage response)
    {
        var rawBody = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync();

        return FormatStatusMessage(response.StatusCode, rawBody);
    }

    internal static string FormatStatusMessage(HttpStatusCode statusCode, string? rawBody)
    {
        var extractedMessage = TryExtractMessage(rawBody);
        if (!string.IsNullOrWhiteSpace(extractedMessage))
        {
            return extractedMessage;
        }

        var sanitizedBody = Sanitize(rawBody);
        return string.IsNullOrWhiteSpace(sanitizedBody)
            ? $"Request failed ({(int)statusCode})."
            : $"Request failed ({(int)statusCode}): {sanitizedBody}";
    }

    internal static string FormatRequestFailure(Uri? baseAddress, string requestPath, HttpStatusCode statusCode, string? rawBody)
    {
        var target = BuildRequestTarget(baseAddress, requestPath);
        var extractedMessage = TryExtractMessage(rawBody) ?? Sanitize(rawBody);

        return string.IsNullOrWhiteSpace(extractedMessage)
            ? $"Request to {target} failed ({(int)statusCode})."
            : $"Request to {target} failed ({(int)statusCode}): {extractedMessage}";
    }

    internal static string FormatException(Uri? baseAddress, string requestPath, Exception exception)
    {
        var target = BuildRequestTarget(baseAddress, requestPath);
        return $"Unable to load data from {target}: {exception.Message}";
    }

    private static string? TryExtractMessage(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (TryGetString(root, "message", out var message) ||
                TryGetString(root, "error", out message) ||
                TryGetString(root, "title", out message))
            {
                return Sanitize(message);
            }

            if (root.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errorsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var value = Sanitize(item.GetString());
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    return value;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string BuildRequestTarget(Uri? baseAddress, string requestPath)
    {
        if (baseAddress is null)
        {
            return requestPath;
        }

        return new Uri(baseAddress, requestPath).ToString();
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        if (compact.Length <= MaxMessageLength)
        {
            return compact;
        }

        return compact[..MaxMessageLength] + "...";
    }
}