using JsonRepairNet.Cli;

namespace JsonRepairNet.Tests;

public class CliTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpReturnsUsage(string flag)
    {
        var code = CliRunner.Run([flag], TextReader.Null, Out(out var output), Out(out var error));
        Assert.Equal(0, code);
        Assert.Contains("json-repair", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--indent", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--output", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--inline", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void ReadsStdinAndWritesCompactJson()
    {
        var code = CliRunner.Run(
            [],
            new StringReader("{a: 1,}"),
            Out(out var output),
            Out(out var error));

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal("{\"a\":1}" + Environment.NewLine, output.ToString());
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
    public void IndentEqualsFormAndBareIndentDefaultToTwoSpaces()
    {
        var equals = CliRunner.Run(
            ["--indent=4"],
            new StringReader("{a:1}"),
            Out(out var four),
            Out(out var errorFour));
        Assert.Equal(0, equals);
        Assert.Equal(string.Empty, errorFour.ToString());
        Assert.Contains("\n    \"a\"", four.ToString().Replace("\r\n", "\n"), StringComparison.Ordinal);

        var bare = CliRunner.Run(
            ["--indent"],
            new StringReader("{a:1}"),
            Out(out var two),
            Out(out var errorTwo));
        Assert.Equal(0, bare);
        Assert.Equal(string.Empty, errorTwo.ToString());
        Assert.Contains("\n  \"a\"", two.ToString().Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsFileAndWritesStdout()
    {
        using var workspace = new TempWorkspace();
        var input = workspace.File("in.json", "{a: True}");

        var code = CliRunner.Run([input], TextReader.Null, Out(out var output), Out(out var error));

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal("{\"a\":true}" + Environment.NewLine, output.ToString());
    }

    [Theory]
    [InlineData("--output")]
    [InlineData("-o")]
    public void ReadsFileAndWritesOutputFile(string outputFlag)
    {
        using var workspace = new TempWorkspace();
        var input = workspace.File("in.json", "```json\n{'ok': True}\n```");
        var output = workspace.FilePath("out.json");

        var code = CliRunner.Run(
            [input, outputFlag, output, "--indent", "4"],
            TextReader.Null,
            Out(out var stdout),
            Out(out var error));

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(string.Empty, stdout.ToString());
        var text = File.ReadAllText(output);
        Assert.Contains("\"ok\": true", text, StringComparison.Ordinal);
        Assert.Contains("\n    \"ok\"", text.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void OutputEqualsFormWritesFile()
    {
        using var workspace = new TempWorkspace();
        var input = workspace.File("in.json", "{a:1}");
        var output = workspace.FilePath("out.json");

        var code = CliRunner.Run(
            [input, "--output=" + output],
            TextReader.Null,
            Out(out var stdout),
            Out(out var error));

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal("{\"a\":1}" + Environment.NewLine, File.ReadAllText(output));
    }

    [Theory]
    [InlineData("--inline")]
    [InlineData("-i")]
    public void InlineReplacesInputFile(string inlineFlag)
    {
        using var workspace = new TempWorkspace();
        var input = workspace.File("in.json", "{a: 1,}");

        var code = CliRunner.Run(
            [input, inlineFlag, "--indent", "0"],
            TextReader.Null,
            Out(out var stdout),
            Out(out var error));

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal("{\"a\":1}" + Environment.NewLine, File.ReadAllText(input));
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

    [Fact]
    public void ArgumentErrorsReturnExitCodeOne()
    {
        var twoFiles = CliRunner.Run(["a.json", "b.json"], TextReader.Null, Out(out _), Out(out var twoError));
        Assert.Equal(1, twoFiles);
        Assert.Contains("only one input file", twoError.ToString(), StringComparison.Ordinal);

        var missingOutput = CliRunner.Run(["--output"], TextReader.Null, Out(out _), Out(out var outputError));
        Assert.Equal(1, missingOutput);
        Assert.Contains("--output requires a file path", outputError.ToString(), StringComparison.Ordinal);

        var emptyOutput = CliRunner.Run(["--output="], TextReader.Null, Out(out _), Out(out var emptyOutputError));
        Assert.Equal(1, emptyOutput);
        Assert.Contains("--output requires a file path", emptyOutputError.ToString(), StringComparison.Ordinal);

        var badIndent = CliRunner.Run(["--indent=x"], new StringReader("{}"), Out(out _), Out(out var indentError));
        Assert.Equal(1, badIndent);
        Assert.Contains("--indent expects a non-negative integer", indentError.ToString(), StringComparison.Ordinal);

        var tooWide = CliRunner.Run(["--indent", "17"], new StringReader("{}"), Out(out _), Out(out var wideError));
        Assert.Equal(1, tooWide);
        Assert.Contains("--indent must be between 0 and 16", wideError.ToString(), StringComparison.Ordinal);

        var inlineStdin = CliRunner.Run(["--inline"], new StringReader("{a:1}"), Out(out _), Out(out var inlineError));
        Assert.Equal(1, inlineStdin);
        Assert.Contains("Inline mode requires a filename", inlineError.ToString(), StringComparison.Ordinal);

        var both = CliRunner.Run(
            ["in.json", "--inline", "--output", "out.json"],
            TextReader.Null,
            Out(out _),
            Out(out var bothError));
        Assert.Equal(1, both);
        Assert.Contains("You cannot pass both --inline and --output", bothError.ToString(), StringComparison.Ordinal);
    }

    private static StringWriter Out(out StringWriter writer)
    {
        writer = new StringWriter();
        return writer;
    }

    private sealed class TempWorkspace : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "json-repair-tests-" + Guid.NewGuid().ToString("N"));

        public TempWorkspace() => Directory.CreateDirectory(_directory);

        public string FilePath(string name) => System.IO.Path.Combine(_directory, name);

        public string File(string name, string contents)
        {
            var path = FilePath(name);
            System.IO.File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
