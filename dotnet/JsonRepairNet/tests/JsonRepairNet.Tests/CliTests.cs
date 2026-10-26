using JsonRepairNet.Cli;

namespace JsonRepairNet.Tests;

public class CliTests
{
    [Fact]
    public void HelpReturnsUsage()
    {
        var code = CliRunner.Run(["--help"], TextReader.Null, Out(out var output), Out(out var error));
        Assert.Equal(0, code);
        Assert.Contains("json-repair", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--indent", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void ReadsStdinAndWritesPrettyJson()
    {
        var code = CliRunner.Run(
            ["--indent", "2"],
            new StringReader("{a: 1,}"),
            Out(out var output),
            Out(out var error));

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("\"a\": 1", output.ToString(), StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsFileAndWritesOutput()
    {
        var directory = Path.Combine(Path.GetTempPath(), "json-repair-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "in.json");
            var output = Path.Combine(directory, "out.json");
            File.WriteAllText(input, "```json\n{'ok': True}\n```");

            var code = CliRunner.Run([input, "--output", output, "--indent", "4"], TextReader.Null, Out(out _), Out(out var error));

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            var text = File.ReadAllText(output);
            Assert.Contains("\"ok\": true", text, StringComparison.Ordinal);
            Assert.Contains("\n    \"ok\"", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingFileAndBadInputFail()
    {
        var missing = CliRunner.Run(["no-such-file.json"], TextReader.Null, Out(out _), Out(out var missingError));
        Assert.Equal(1, missing);
        Assert.Contains("file not found", missingError.ToString(), StringComparison.Ordinal);

        var unknown = CliRunner.Run(["--nope"], TextReader.Null, Out(out _), Out(out var unknownError));
        Assert.Equal(1, unknown);
        Assert.Contains("unknown option", unknownError.ToString(), StringComparison.Ordinal);

        var prose = CliRunner.Run([], new StringReader("not json"), Out(out _), Out(out var proseError));
        Assert.Equal(1, prose);
        Assert.Contains("unable to repair", proseError.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static StringWriter Out(out StringWriter writer)
    {
        writer = new StringWriter();
        return writer;
    }
}
