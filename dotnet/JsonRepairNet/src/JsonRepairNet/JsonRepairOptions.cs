namespace JsonRepairNet;

/// <summary>
/// Controls how repaired JSON text is written.
/// Valid JSON is returned unchanged unless <see cref="WriteIndented"/> is set.
/// </summary>
public sealed class JsonRepairOptions
{
    /// <summary>
    /// Shared options that write compact JSON and preserve valid input text.
    /// </summary>
    public static JsonRepairOptions Default { get; } = new();

    /// <summary>
    /// When <see langword="true"/>, write indented JSON.
    /// Valid input is pretty-printed instead of being returned unchanged.
    /// </summary>
    public bool WriteIndented { get; init; }

    /// <summary>
    /// Spaces per indent level when <see cref="WriteIndented"/> is <see langword="true"/>.
    /// Must be between 0 and 16. Zero writes compact JSON.
    /// </summary>
    public int IndentSize { get; init; } = 2;

    /// <summary>
    /// Options that pretty-print with <paramref name="indentSize"/> spaces.
    /// </summary>
    /// <param name="indentSize">Spaces per level. Defaults to 2.</param>
    /// <returns>Indented repair options.</returns>
    public static JsonRepairOptions Indented(int indentSize = 2) => new()
    {
        WriteIndented = true,
        IndentSize = indentSize,
    };
}
