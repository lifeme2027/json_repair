using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using JsonRepairNet.Internal;

namespace JsonRepairNet;

/// <summary>
/// Repairs malformed JSON text and returns either JSON text or a <see cref="JsonNode"/>.
/// Valid JSON is preserved as written, unless indentation is requested.
/// </summary>
public static class JsonRepair
{
    /// <summary>
    /// Repairs <paramref name="input"/> and returns JSON text.
    /// </summary>
    /// <param name="input">JSON text, or the malformed text to repair.</param>
    /// <param name="options">Writing options. Omit them to keep valid JSON unchanged and write repairs compactly.</param>
    /// <returns>Valid JSON text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Indent size is outside 0..16.</exception>
    /// <exception cref="JsonRepairException">The input cannot be repaired into JSON.</exception>
    public static string Repair(string? input, JsonRepairOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var outcome = RepairCore(input, options ?? JsonRepairOptions.Default);
        if (!outcome.Success || outcome.Json is null)
        {
            throw new JsonRepairException(outcome.Error ?? "Unable to repair the input into JSON.");
        }

        return outcome.Json;
    }

    /// <summary>
    /// Repairs <paramref name="input"/> and returns the result as <typeparamref name="TNode"/>.
    /// JSON <c>null</c> is returned as a null reference.
    /// </summary>
    /// <typeparam name="TNode">A <see cref="JsonNode"/> type such as <see cref="JsonObject"/> or <see cref="JsonArray"/>.</typeparam>
    /// <param name="input">JSON text, or the malformed text to repair.</param>
    /// <param name="options">Writing options. They do not change the node shape.</param>
    /// <returns>The repaired node, or <see langword="null"/> when the JSON value is null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Indent size is outside 0..16.</exception>
    /// <exception cref="JsonRepairException">The input cannot be repaired, or the root value is not a <typeparamref name="TNode"/>.</exception>
    public static TNode? Repair<TNode>(string? input, JsonRepairOptions? options = null)
        where TNode : JsonNode
    {
        ArgumentNullException.ThrowIfNull(input);
        var outcome = RepairCore(input, options ?? JsonRepairOptions.Default);
        if (!outcome.Success)
        {
            throw new JsonRepairException(outcome.Error ?? "Unable to repair the input into JSON.");
        }

        if (outcome.Node is null)
        {
            return null;
        }

        if (outcome.Node is TNode typed)
        {
            return typed;
        }

        throw new JsonRepairException(
            $"Repaired JSON is {outcome.Node.GetType().Name}, which is not a {typeof(TNode).Name}.");
    }

    /// <summary>
    /// Repairs <paramref name="input"/> and returns a <see cref="JsonNode"/>.
    /// This is the node-returning overload of <see cref="Repair(string?, JsonRepairOptions?)"/>.
    /// </summary>
    /// <param name="input">JSON text, or the malformed text to repair.</param>
    /// <param name="options">Writing options. They do not change the node shape.</param>
    /// <returns>The repaired node, or <see langword="null"/> when the JSON value is null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonRepairException">The input cannot be repaired into JSON.</exception>
    public static JsonNode? RepairNode(string? input, JsonRepairOptions? options = null) =>
        Repair<JsonNode>(input, options);

    /// <summary>
    /// Tries to repair <paramref name="input"/> into JSON text.
    /// </summary>
    /// <param name="input">JSON text, or the malformed text to repair. Null and blank input fail.</param>
    /// <param name="json">The repaired JSON text when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a JSON value was produced.</returns>
    public static bool TryRepair(string? input, [NotNullWhen(true)] out string? json) =>
        TryRepair(input, null, out json);

    /// <summary>
    /// Tries to repair <paramref name="input"/> into JSON text using <paramref name="options"/>.
    /// </summary>
    /// <param name="input">JSON text, or the malformed text to repair. Null and blank input fail.</param>
    /// <param name="options">Writing options. Null uses <see cref="JsonRepairOptions.Default"/>.</param>
    /// <param name="json">The repaired JSON text when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a JSON value was produced.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Indent size is outside 0..16.</exception>
    public static bool TryRepair(string? input, JsonRepairOptions? options, [NotNullWhen(true)] out string? json)
    {
        if (input is null || string.IsNullOrWhiteSpace(input))
        {
            json = null;
            return false;
        }

        var outcome = RepairCore(input, options ?? JsonRepairOptions.Default);
        json = outcome.Json;
        return outcome.Success && json is not null;
    }

    /// <summary>
    /// Tries to repair <paramref name="input"/> into <typeparamref name="TNode"/>.
    /// A successful repair of JSON <c>null</c> returns <see langword="true"/> and a null node.
    /// </summary>
    /// <typeparam name="TNode">A <see cref="JsonNode"/> type. Use <see cref="JsonNode"/> for any root.</typeparam>
    /// <param name="input">JSON text, or the malformed text to repair. Null and blank input fail.</param>
    /// <param name="node">The repaired node when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the root value is null or a <typeparamref name="TNode"/>.</returns>
    public static bool TryRepair<TNode>(string? input, out TNode? node)
        where TNode : JsonNode =>
        TryRepair(input, null, out node);

    /// <summary>
    /// Tries to repair <paramref name="input"/> into <typeparamref name="TNode"/> using <paramref name="options"/>.
    /// </summary>
    /// <typeparam name="TNode">A <see cref="JsonNode"/> type. Use <see cref="JsonNode"/> for any root.</typeparam>
    /// <param name="input">JSON text, or the malformed text to repair. Null and blank input fail.</param>
    /// <param name="options">Writing options. Null uses <see cref="JsonRepairOptions.Default"/>.</param>
    /// <param name="node">The repaired node when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the root value is null or a <typeparamref name="TNode"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Indent size is outside 0..16.</exception>
    public static bool TryRepair<TNode>(string? input, JsonRepairOptions? options, out TNode? node)
        where TNode : JsonNode
    {
        node = null;
        if (input is null || string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var outcome = RepairCore(input, options ?? JsonRepairOptions.Default);
        if (!outcome.Success)
        {
            return false;
        }

        if (outcome.Node is null)
        {
            return true;
        }

        if (outcome.Node is not TNode typed)
        {
            return false;
        }

        node = typed;
        return true;
    }

    private static RepairOutcome RepairCore(string input, JsonRepairOptions options)
    {
        if (options.IndentSize is < 0 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "IndentSize must be between 0 and 16.");
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            return RepairOutcome.Fail("Input is empty.");
        }

        if (JsonValidity.IsValid(input))
        {
            var validNode = JsonNode.Parse(input, documentOptions: JsonValidity.DocumentOptions);
            var validJson = options.WriteIndented
                ? JsonText.WriteNode(validNode, options.IndentSize)
                : input;
            return RepairOutcome.Ok(validJson, validNode);
        }

        try
        {
            var value = RepairParser.Parse(input);
            if (value is null)
            {
                return RepairOutcome.Fail("Unable to repair the input into JSON.");
            }

            var json = JsonText.Write(value, options);
            return RepairOutcome.Ok(json, JsonText.ToNode(value));
        }
        catch (JsonRepairException exception)
        {
            return RepairOutcome.Fail(exception.Message);
        }
    }

    private readonly struct RepairOutcome
    {
        public bool Success { get; private init; }

        public string? Json { get; private init; }

        public JsonNode? Node { get; private init; }

        public string? Error { get; private init; }

        public static RepairOutcome Ok(string json, JsonNode? node) => new()
        {
            Success = true,
            Json = json,
            Node = node,
        };

        public static RepairOutcome Fail(string error) => new()
        {
            Success = false,
            Error = error,
        };
    }
}
