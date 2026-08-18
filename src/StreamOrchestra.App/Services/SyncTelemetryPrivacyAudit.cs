using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StreamOrchestra.App.Services;

public static class SyncTelemetryPrivacyAudit
{
    private static readonly Regex SecretTextPattern = new(
        @"(?i)(?:\bbearer\s+[a-z0-9._~+/=-]+|\bbasic\s+[a-z0-9+/=]+|\beyJ[a-z0-9_-]+\.[a-z0-9_-]+\.[a-z0-9_-]+|[?&](?:token|auth|key|sig|signature|policy|credential|password|secret|x-amz-[a-z0-9_-]+)=)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> FindViolations(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ["empty-json"];
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch
        {
            return ["invalid-json"];
        }

        var violations = new HashSet<string>(StringComparer.Ordinal);
        Inspect(root, "$", violations);
        return violations.Order(StringComparer.Ordinal).ToArray();
    }

    private static void Inspect(JsonNode? node, string path, ISet<string> violations)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    var propertyPath = $"{path}.{property.Key}";
                    if (IsForbiddenProperty(property.Key) &&
                        property.Value?.ToString() is { } value &&
                        !value.Equals("[redacted]", StringComparison.Ordinal))
                    {
                        violations.Add($"forbidden-property:{propertyPath}");
                    }

                    Inspect(property.Value, propertyPath, violations);
                }
                break;
            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    Inspect(jsonArray[index], $"{path}[{index}]", violations);
                }
                break;
            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text):
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    violations.Add($"raw-url:{path}");
                }

                if (SecretTextPattern.IsMatch(text))
                {
                    violations.Add($"secret-pattern:{path}");
                }
                break;
        }
    }

    private static bool IsForbiddenProperty(string propertyName)
    {
        var normalized = new string(propertyName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized is "authorization" or "proxyauthorization" or "cookie" or "cookies" or
                   "setcookie" or "password" or "passwd" or "accesstoken" or "refreshtoken" or
                   "token" or "apikey" or "secret" or "signature" or "signedquery" or "signedurl" or
                   "requestheaders" or "responseheaders" or "headers" or "rawbody" or "requestbody" or
                   "responsebody" or "playlistbody" or "manifesttext" or "originalurl" or "requesturl" ||
               normalized.EndsWith("authorizationheader", StringComparison.Ordinal) ||
               normalized.EndsWith("cookieheader", StringComparison.Ordinal) ||
               normalized.EndsWith("rawbody", StringComparison.Ordinal) ||
               normalized.EndsWith("playlistbody", StringComparison.Ordinal) ||
               normalized.EndsWith("manifesttext", StringComparison.Ordinal);
    }
}
