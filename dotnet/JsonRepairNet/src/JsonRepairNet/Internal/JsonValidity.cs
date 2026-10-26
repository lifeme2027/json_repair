using System.Text.Json;

namespace JsonRepairNet.Internal;

internal static class JsonValidity
{
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 1024,
    };

    public static bool IsValid(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input, DocumentOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
