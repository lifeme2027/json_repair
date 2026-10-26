using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonRepairNet.Tests;

public class RepairTests
{
    public static IEnumerable<object[]> Cases()
    {
        // Missing quotes.
        yield return Case("""{name: "Ada", "age": 30}""", """{"name":"Ada","age":30}""");
        yield return Case("""{"name": John, "age": 30}""", """{"name":"John","age":30}""");
        yield return Case("""{"city": New York}""", """{"city":"New York"}""");
        yield return Case("{key:value,key2:value2}", """{"key":"value","key2":"value2"}""");
        yield return Case("{foo: [}", """{"foo":[]}""");
        yield return Case("""{"key":value "key2" : "value2" }""", """{"key":"value","key2":"value2"}""");
        yield return Case("""{"words": abcdef", "numbers": 12345", "words2": ghijkl" }""", """{"words":"abcdef","numbers":12345,"words2":"ghijkl"}""");

        // Missing, extra, and trailing commas.
        yield return Case("""{"a":1 "b":2}""", """{"a":1,"b":2}""");
        yield return Case("[1 2 3]", "[1,2,3]");
        yield return Case("""{"a":1,, "b":2}""", """{"a":1,"b":2}""");
        yield return Case("[1,,2]", "[1,2]");
        yield return Case("""{"a":1,}""", """{"a":1}""");
        yield return Case("[1,2,3,]", "[1,2,3]");
        yield return Case("""{"key": , "key2": "value2"}""", """{"key":"","key2":"value2"}""");
        yield return Case("[1, 2, 3, ...]", "[1,2,3]");
        yield return Case("[1, 2, ... , 3]", "[1,2,3]");

        // Missing or mismatched brackets, including a missing opening brace.
        yield return Case("{", "{}");
        yield return Case("[", "[]");
        yield return Case("""{"a":1""", """{"a":1}""");
        yield return Case("{\"k\":\"v\"", """{"k":"v"}""");
        yield return Case("""{"a": [1, 2}""", """{"a":[1,2]}""");
        yield return Case("""{"key1": {"key2": [1, 2, 3""", """{"key1":{"key2":[1,2,3]}}""");
        yield return Case("[1, 2)", "[1,2]");
        yield return Case("""["key":"value"]""", """[{"key":"value"}]""");
        yield return Case("""{"outer": ["a", "b", "next": "value"}""", """{"outer":["a","b"],"next":"value"}""");
        yield return Case(""" "a": 1} """, """{"a":1}""");
        yield return Case("""{"employees":["John", "Anna",""", """{"employees":["John","Anna"]}""");

        // Single quotes and smart quotes.
        yield return Case("{'a': 'b'}", """{"a":"b"}""");
        yield return Case("{'key': 'string', 'key2': false, \"key3\": null}", """{"key":"string","key2":false,"key3":null}""");
        yield return Case("{“slanted_delimiter”: \"value\"}", """{"slanted_delimiter":"value"}""");

        // Line comments, block comments, and hash comments.
        yield return Case("/* c */ {\"a\":1}", """{"a":1}""");
        yield return Case("{ \"a\": 1 // c\n, \"b\": 2}", """{"a":1,"b":2}""");
        yield return Case("{ \"a\": 1 /* c */ , \"b\": 2}", """{"a":1,"b":2}""");
        yield return Case("{ \"a\": 1 # c\n, \"b\": 2}", """{"a":1,"b":2}""");
        yield return Case("[ \"value\", /* comment */ \"value2\" ]", """["value","value2"]""");
        yield return Case("{ \"key\": \"value\" /* comment", """{"key":"value"}""");
        yield return Case("{\"a\": \"hello\" // tail\n}", """{"a":"hello"}""");

        // Markdown fences and surrounding prose.
        yield return Case("```json\n{\"a\": 1}\n```", """{"a":1}""");
        yield return Case("```json { 'a': 'b' } ```", """{"a":"b"}""");
        yield return Case("Here is JSON: {\"a\": 1} thanks", """{"a":1}""");
        yield return Case("Based on the information extracted, here is the filled JSON output: ```json { 'a': 'b' } ```", """{"a":"b"}""");
        yield return Case("lorem ```json {\"key\":\"value\"} ``` ipsum ```json [1,2,3,True] ``` 42", """[{"key":"value"},[1,2,3,true]]""");
        yield return Case("````{ \"key\": \"value\" }```", """{"key":"value"}""");
        yield return Case("{\"key\": \"value}```", """{"key":"value"}""");

        // Truncated strings, arrays, and objects.
        yield return Case("{\"a\": \"New", """{"a":"New"}""");
        yield return Case("[1, 2, 3,", "[1,2,3]");
        yield return Case("{\"city\": \"New York", """{"city":"New York"}""");

        // Python literals and case-insensitive JSON literals.
        yield return Case("""{"a": True, "b": False, "c": None}""", """{"a":true,"b":false,"c":null}""");
        yield return Case("""{"a": TRUE, "b": FALSE, "c": NULL}""", """{"a":true,"b":false,"c":null}""");
        yield return Case("True", "true");
        yield return Case("None", "null");
        yield return Case("(True, False, None)", "[true,false,null]");

        // Numbers and numeric-looking strings.
        yield return Case("""{"value": 82_461_110}""", """{"value":82461110}""");
        yield return Case("""{"key": .25}""", """{"key":0.25}""");
        yield return Case("""{"key": 1/3}""", """{"key":"1/3"}""");
        yield return Case("""{"key": 10-20}""", """{"key":"10-20"}""");
        yield return Case("[1+2]", "[1,2]");
        yield return Case("""{"key": 1notanumber }""", """{"key":"1notanumber"}""");
        yield return Case("""{"key": 1e+10""", """{"key":1e+10}""");
        yield return Case("""{"sku": 123_456ABC}""", """{"sku":"123_456ABC"}""");

        // Interior quotes, doubled quotes, and bare member recovery.
        yield return Case("""{"key": "v"alue"}""", """{"key":"v\"alue\""}""");
        yield return Case("""{"key": ""value"}""", """{"key":"value"}""");
        yield return Case("""{"title": ""hello" world"}""", """{"title":"\"hello\" world"}""");
        yield return Case("""{"a": "first, b: "second"}""", """{"a":"first","b":"second"}""");
        yield return Case("""{"name": "John", "age": 30, "city": "New York, "gender": "male"}""", """{"name":"John","age":30,"city":"New York","gender":"male"}""");
        yield return Case("""{"k"e"y": "value"}""", """{"k\"e\"y":"value"}""");
        yield return Case("""{"key:"value"}""", """{"key":"value"}""");
        yield return Case("""{"key": "Lorem "ipsum" s,"}""", """{"key":"Lorem \"ipsum\" s,"}""");
        yield return Case("""{"lorem": ipsum, sic, datum.",}""", """{"lorem":"ipsum, sic, datum."}""");
        yield return Case("""{ "content": "[LINK]("https://google.com")" }""", """{"content":"[LINK](\"https://google.com\")"}""");

        // Parentheses normalize to arrays, except a single grouped scalar.
        yield return Case("(1, 2)", "[1,2]");
        yield return Case("(1)", "1");
        yield return Case("""{"coords": (1, 2), "ok": true}""", """{"coords":[1,2],"ok":true}""");

        // Several top-level values become one array.
        yield return Case("""{"a":1}{"b":2}""", """[{"a":1},{"b":2}]""");
        yield return Case("""{"key":"value"}, {"key2":"value2"}""", """[{"key":"value"},{"key2":"value2"}]""");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RepairsMalformedJson(string input, string expected)
    {
        var actual = JsonRepair.Repair(input);
        Assert.True(JsonRepair.TryRepair(input, out var repaired));
        Assert.Equal(Canonical(expected), Canonical(actual));
        Assert.Equal(Canonical(expected), Canonical(repaired));
        using var document = JsonDocument.Parse(actual);
        Assert.NotEqual(JsonValueKind.Undefined, document.RootElement.ValueKind);
    }

    [Fact]
    public void PreservesValidObjectText()
    {
        const string original = "{\n  \"name\": \"Ada\",\n  \"ok\": true\n}";
        Assert.Equal(original, JsonRepair.Repair(original));
    }

    [Fact]
    public void PreservesValidArrayText()
    {
        const string original = "[1, 2, 3]";
        Assert.Equal(original, JsonRepair.Repair(original));
    }

    [Fact]
    public void PreservesValidStringEscapes()
    {
        const string original = "{\"note\":\"line\\nnext\",\"path\":\"a\\\\b\"}";
        Assert.Equal(original, JsonRepair.Repair(original));
    }

    [Fact]
    public void PreservesValidLiteralsAndEmptyContainers()
    {
        Assert.Equal("null", JsonRepair.Repair("null"));
        Assert.Equal("true", JsonRepair.Repair("true"));
        Assert.Equal("false", JsonRepair.Repair("false"));
        Assert.Equal("{}", JsonRepair.Repair("{}"));
        Assert.Equal("[]", JsonRepair.Repair("[]"));
    }

    [Fact]
    public void PreservesWhitespaceAroundValidJson()
    {
        const string original = "\n  {\"a\": 1}  \n";
        Assert.Equal(original, JsonRepair.Repair(original));
    }

    [Fact]
    public void KeepsUrlSlashesInsideStringsWhenATrailingCommaIsRepaired()
    {
        var actual = JsonRepair.Repair("""{"url": "https://example.com",}""");
        Assert.Equal("""{"url":"https://example.com"}""", Canonical(actual));
    }

    [Fact]
    public void LastDuplicateKeyWins()
    {
        var actual = JsonRepair.Repair("""{"b": "v1", "b": "v2",}""");
        Assert.Equal("""{"b":"v2"}""", Canonical(actual));
    }

    [Fact]
    public void RepairNodeReturnsObjectArrayAndNull()
    {
        var obj = JsonRepair.Repair<JsonObject>("""{name: "Ada"}""");
        Assert.NotNull(obj);
        Assert.Equal("Ada", obj["name"]?.GetValue<string>());

        var array = JsonRepair.RepairNode("[1, 2,]");
        Assert.IsType<JsonArray>(array);
        Assert.Equal(2, array.AsArray().Count);

        Assert.Null(JsonRepair.Repair<JsonNode>("null"));
        Assert.True(JsonRepair.TryRepair("null", out JsonNode? node));
        Assert.Null(node);
    }

    [Fact]
    public void TryRepairOutNodeMatchesText()
    {
        Assert.True(JsonRepair.TryRepair("{a: 1}", out JsonNode? node));
        Assert.Equal(1, node!["a"]!.GetValue<int>());
        Assert.True(JsonRepair.TryRepair("{a: 1}", out string? json));
        Assert.Equal(Canonical("""{"a":1}"""), Canonical(json));
    }

    [Fact]
    public void RejectsNullBlankAndPlainProse()
    {
        Assert.False(JsonRepair.TryRepair(null, out string? json));
        Assert.Null(json);
        Assert.False(JsonRepair.TryRepair("   ", out JsonNode? node));
        Assert.Null(node);
        Assert.False(JsonRepair.TryRepair("just some words", out _));
        Assert.Throws<JsonRepairException>(() => JsonRepair.Repair("just some words"));
        Assert.Throws<ArgumentNullException>(() => JsonRepair.Repair(null));
    }

    [Fact]
    public void WrongNodeTypeThrows()
    {
        var exception = Assert.Throws<JsonRepairException>(() => JsonRepair.Repair<JsonArray>("{a: 1}"));
        Assert.Contains("JsonArray", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IndentRewritesValidAndRepairedJson()
    {
        var pretty = JsonRepair.Repair("""{"a":1,"b":[2]}""", JsonRepairOptions.Indented(4));
        Assert.Contains("\n    \"a\": 1", pretty, StringComparison.Ordinal);
        Assert.Contains("\n        2", pretty, StringComparison.Ordinal);

        var repaired = JsonRepair.Repair("{a: 1,}", JsonRepairOptions.Indented(2));
        Assert.Contains("\n  \"a\": 1", repaired, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcessiveNestingDoesNotCrash()
    {
        var input = new string('[', 300);
        Assert.False(JsonRepair.TryRepair(input, out string? json));
        Assert.Null(json);
        Assert.Throws<JsonRepairException>(() => JsonRepair.Repair(input));
    }

    private static object[] Case(string input, string expected) => [input, expected];

    private static string Canonical(string json)
    {
        var node = JsonNode.Parse(json);
        return node?.ToJsonString() ?? "null";
    }
}
