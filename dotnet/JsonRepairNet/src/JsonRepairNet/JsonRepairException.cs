namespace JsonRepairNet;

/// <summary>
/// Thrown when <see cref="JsonRepair.Repair(string?, JsonRepairOptions?)"/> cannot turn the input into JSON,
/// or when repaired JSON does not match a requested <see cref="System.Text.Json.Nodes.JsonNode"/> type.
/// </summary>
public sealed class JsonRepairException : Exception
{
    /// <summary>
    /// Creates an exception with the given message.
    /// </summary>
    /// <param name="message">What failed.</param>
    public JsonRepairException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with the given message and inner exception.
    /// </summary>
    /// <param name="message">What failed.</param>
    /// <param name="innerException">The underlying failure.</param>
    public JsonRepairException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
