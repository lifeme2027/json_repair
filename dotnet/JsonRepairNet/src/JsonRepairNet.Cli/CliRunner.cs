using JsonRepairNet;

namespace JsonRepairNet.Cli;

/// <summary>
/// Entry point for the <c>json-repair</c> command. Exposed so tests can drive stdin, stdout, and stderr.
/// </summary>
public static class CliRunner
{
    /// <summary>
    /// Runs the CLI with the given arguments and streams. Returns the process exit code.
    /// </summary>
    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        try
        {
            if (!TryParse(args, out var parsed, out var message))
            {
                error.WriteLine(message);
                return 1;
            }

            if (parsed.Help)
            {
                output.WriteLine(HelpText);
                return 0;
            }

            if (parsed.Inline && parsed.InputPath is null)
            {
                error.WriteLine("Error: Inline mode requires a filename");
                return 1;
            }

            if (parsed.Inline && parsed.OutputPath is not null)
            {
                error.WriteLine("Error: You cannot pass both --inline and --output");
                return 1;
            }

            string text;
            if (parsed.InputPath is null)
            {
                text = input.ReadToEnd();
            }
            else if (!File.Exists(parsed.InputPath))
            {
                error.WriteLine($"Error: file not found: {parsed.InputPath}");
                return 1;
            }
            else
            {
                text = File.ReadAllText(parsed.InputPath);
            }

            JsonRepairOptions? options = parsed.Indent is null
                ? null
                : JsonRepairOptions.Indented(parsed.Indent.Value);
            if (!JsonRepair.TryRepair(text, options, out string? json))
            {
                error.WriteLine("Error: unable to repair the input into JSON.");
                return 1;
            }

            var payload = json + Environment.NewLine;
            var destination = parsed.Inline ? parsed.InputPath : parsed.OutputPath;
            if (destination is null)
            {
                output.Write(payload);
            }
            else
            {
                File.WriteAllText(destination, payload);
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonRepairException)
        {
            error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static bool TryParse(string[] args, out ParsedArgs parsed, out string message)
    {
        parsed = default;
        message = string.Empty;
        string? inputPath = null;
        string? outputPath = null;
        int? indent = null;
        var help = false;
        var inline = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "-h" or "--help")
            {
                help = true;
                continue;
            }

            if (arg is "-i" or "--inline")
            {
                inline = true;
                continue;
            }

            if (arg is "-o" or "--output")
            {
                if (!TryTakeValue(args, ref index, out var path))
                {
                    message = "Error: --output requires a file path.";
                    return false;
                }

                outputPath = path;
                continue;
            }

            if (arg.StartsWith("--output=", StringComparison.Ordinal))
            {
                outputPath = arg["--output=".Length..];
                if (outputPath.Length == 0)
                {
                    message = "Error: --output requires a file path.";
                    return false;
                }

                continue;
            }

            if (arg == "--indent")
            {
                indent = 2;
                if (index + 1 < args.Length && int.TryParse(args[index + 1], out var size) && size >= 0)
                {
                    index++;
                    indent = size;
                }

                continue;
            }

            if (arg.StartsWith("--indent=", StringComparison.Ordinal))
            {
                if (!int.TryParse(arg["--indent=".Length..], out var size) || size < 0)
                {
                    message = "Error: --indent expects a non-negative integer.";
                    return false;
                }

                indent = size;
                continue;
            }

            if (arg.StartsWith('-'))
            {
                message = $"Error: unknown option '{arg}'.";
                return false;
            }

            if (inputPath is not null)
            {
                message = "Error: only one input file is accepted.";
                return false;
            }

            inputPath = arg;
        }

        if (indent is > 16)
        {
            message = "Error: --indent must be between 0 and 16.";
            return false;
        }

        parsed = new ParsedArgs(help, inline, inputPath, outputPath, indent);
        return true;
    }

    private static bool TryTakeValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            value = string.Empty;
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private const string HelpText = """
        json-repair — repair malformed JSON

        Usage:
          json-repair [file] [--output <file>] [--inline] [--indent [n]]

        With no file, JSON is read from stdin.
        --output, -o   Write the repaired JSON to a file.
        --inline, -i   Replace the input file with the repaired JSON.
        --indent       Pretty-print. Optional n is the number of spaces (default 2).
        """;

    private readonly record struct ParsedArgs(bool Help, bool Inline, string? InputPath, string? OutputPath, int? Indent);
}
