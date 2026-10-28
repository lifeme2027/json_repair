using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonRepairNet.Tests;

public class UpstreamParityTests
{
    public static IEnumerable<object[]> AgreeCases()
    {
        yield return Case("missing_quote_value", """{"a": "hello, "b": 2}""", """{"a": "hello", "b": 2}""");
        yield return Case("unquoted_keys", """{name: "Ada", age: 30}""", """{"name": "Ada", "age": 30}""");
        yield return Case("missing_comma_obj", """{"a": 1 "b": 2}""", """{"a": 1, "b": 2}""");
        yield return Case("missing_comma_arr", "[1 2 3]", "[1, 2, 3]");
        yield return Case("missing_comma_str_arr", """["a" "b" "c"]""", """["a", "b", "c"]""");
        yield return Case("single_quotes", "{'a': 'b', 'c': [1, 'x']}", """{"a": "b", "c": [1, "x"]}""");
        yield return Case("line_comment", "{\"a\": 1, // note\n \"b\": 2}", """{"a": 1, "b": 2}""");
        yield return Case("md_fence", "```json\n{\"a\": 1}\n```", """{"a": 1}""");
        yield return Case("prose_before_after", "Here is the result:\n{\"a\": [1, 2]}\nHope that helps!", """{"a": [1, 2]}""");
        yield return Case("trunc_string", """{"a": "hello wor""", """{"a": "hello wor"}""");
        yield return Case("trunc_array", "[1, 2, 3", "[1, 2, 3]");
        yield return Case("trunc_object", """{"a": 1, "b": 2""", """{"a": 1, "b": 2}""");
        yield return Case("trunc_after_colon", """{"a": 1, "b":""", """{"a": 1, "b": ""}""");
        yield return Case("py_literals", """{"a": True, "b": False, "c": None}""", """{"a": true, "b": false, "c": null}""");
        yield return Case("trailing_comma_obj", """{"a": 1, "b": 2,}""", """{"a": 1, "b": 2}""");
        yield return Case("trailing_comma_arr", "[1, 2, 3,]", "[1, 2, 3]");
        yield return Case("unicode_literal", """{"a": "héllo ☃ 日本語"}""", """{"a": "héllo ☃ 日本語"}""");
        yield return Case("unicode_escape", """{"a": "\u00e9\u2603 \n tab\t"}""", """{"a": "é☃ \n tab\t"}""");
        yield return Case("nested_trunc", """{"a": {"b": [1, {"c": "d""", """{"a": {"b": [1, {"c": "d"}]}}""");
        yield return Case("num_exp", """{"a": 1e5, "b": 1E-3}""", """{"a": 100000.0, "b": 0.001}""");
        yield return Case("num_neg_zero", "[-0, 0, -0.0]", "[0, 0, -0.0]");
        yield return Case("num_underscore", """{"pop": 8_400_000}""", """{"pop": 8400000}""");
        yield return Case("arr_in_obj_unclosed", """{"a": [1, 2, {"b": 3}""", """{"a": [1, 2, {"b": 3}]}""");
        yield return Case("bare_word_value", """{"a": hello world, "b": 2}""", """{"a": "hello world", "b": 2}""");
        yield return Case("scalar_number", "42", "42");
        yield return Case("escaped_quote_inside", """{"a": "he said \"hi\""}""", """{"a": "he said \"hi\""}""");
        yield return Case("hash_comment", "{\"a\": 1, # c\n \"b\": 2}", """{"a": 1, "b": 2}""");
        yield return Case("null_true_lower_trunc", """{"a": tru""", """{"a": "tru"}""");
        yield return Case("extra_closer", """{"a": 1}}""", """{"a": 1}""");
        yield return Case("two_objects", """{"a": 1}{"b": 2}""", """[{"a": 1}, {"b": 2}]""");
        yield return Case("missing_open_brace", "\"a\": 1, \"b\": 2}", """{"a": 1, "b": 2}""");
    }

    [Theory]
    [MemberData(nameof(AgreeCases))]
    public void AgreesWithUpstream(string name, string input, string expected)
    {
        _ = name;
        var actual = JsonRepair.Repair(input);
        AssertJsonEqual(expected, actual);
    }

    public static IEnumerable<object[]> RepeatedColonCases()
    {
        yield return Case("double_colon", """{"a":: 1, "b": 2}""", """{"a": 1, "b": 2}""");
        yield return Case("spaced_extra_colon", """{"a": : 1}""", """{"a": 1}""");
        yield return Case("triple_colon", """{"a"::: 1, "b": 2}""", """{"a": 1, "b": 2}""");
    }

    [Theory]
    [MemberData(nameof(RepeatedColonCases))]
    public void FixedToMatchUpstream(string name, string input, string expected)
    {
        _ = name;
        var actual = JsonRepair.Repair(input);
        AssertJsonEqual(expected, actual);
    }

    // Upstream quirk: a block comment between the colon and a number is treated as
    // the value, so {"a": /* c */ 1} becomes {"a":""}. .NET skips the comment and
    // keeps the number.
    [Fact]
    public void DocumentedDifferencesFromUpstream_BlockCommentMidValue()
    {
        var actual = JsonRepair.Repair("""{"a": /* c */ 1, "b": 2}""");
        AssertJsonEqual("""{"a":1,"b":2}""", actual);
    }

    // Upstream drops a dangling truncated key ({"a": 1, "b -> {"a":1}).
    // .NET closes the unfinished member with an empty string.
    [Fact]
    public void DocumentedDifferencesFromUpstream_TruncKey()
    {
        var actual = JsonRepair.Repair("""{"a": 1, "b""");
        AssertJsonEqual("""{"a":1,"b":""}""", actual);
    }

    // Upstream emits non-JSON NaN/Infinity tokens. .NET keeps a single valid JSON
    // string so the result can be parsed.
    [Fact]
    public void DocumentedDifferencesFromUpstream_NanInf()
    {
        var actual = JsonRepair.Repair("[NaN, Infinity]");
        Assert.Equal("""["NaN, Infinity"]""", Canonical(actual));
        AssertJsonEqual("""["NaN, Infinity"]""", actual);
    }

    // Upstream coerces 007 to 7 and 1.50 to 1.5. .NET treats a leading-zero integer
    // as a string and keeps the decimal as written.
    [Fact]
    public void DocumentedDifferencesFromUpstream_LeadingZero()
    {
        var actual = JsonRepair.Repair("[007, 1.50]");
        AssertJsonEqual("""["007", 1.50]""", actual);
    }

    // Upstream returns empty output for an unclosed quoted scalar. .NET closes the
    // string so Repair still yields valid JSON.
    [Fact]
    public void DocumentedDifferencesFromUpstream_LoneQuoteString()
    {
        var actual = JsonRepair.Repair("\"abc");
        Assert.Equal("\"abc\"", actual);
        AssertJsonEqual("\"abc\"", actual);
    }

    public static IEnumerable<object[]> TrailingQuoteKeyCases()
    {
        yield return Case("trailing_quote_key", """{"a": 1, b": 2}""", """{"a": 1, "b": 2}""");
        yield return Case("trailing_quote_first_key", """{a": 1}""", """{"a": 1}""");
        yield return Case("trailing_quote_many_keys", """{"a": 1, b": 2, c": 3}""", """{"a": 1, "b": 2, "c": 3}""");
        yield return Case("trailing_quote_key_unclosed", """{"a": 1, b": 2""", """{"a": 1, "b": 2}""");
        yield return Case("trailing_quote_array_item", """["x", y"]""", """["x", "y"]""");
    }

    // A key with only a trailing quote (no opening quote) is treated as the unquoted
    // key, so {"a": 1, b": 2} repairs to {"a":1,"b":2} as Python json_repair does.
    [Theory]
    [MemberData(nameof(TrailingQuoteKeyCases))]
    public void FixedToMatchUpstream_TrailingQuoteKey(string name, string input, string expected)
    {
        _ = name;
        var actual = JsonRepair.Repair(input);
        AssertJsonEqual(expected, actual);
    }

    // Upstream returns an empty string for blank, prose, or bare-word top-level
    // input. .NET always requires valid JSON, so Repair throws and TryRepair fails.
    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    [InlineData("just some text")]
    [InlineData("hello")]
    public void DocumentedDifferencesFromUpstream_UnrepairableInput(string input)
    {
        Assert.False(JsonRepair.TryRepair(input, out string? json));
        Assert.Null(json);
        Assert.Throws<JsonRepairException>(() => JsonRepair.Repair(input));
    }

    private static object[] Case(string name, string input, string expected) => [name, input, expected];

    private static string Canonical(string json)
    {
        var node = JsonNode.Parse(json);
        return node?.ToJsonString() ?? "null";
    }

    private static void AssertJsonEqual(string expected, string actual)
    {
        var expectedNode = JsonNode.Parse(expected);
        var actualNode = JsonNode.Parse(actual);
        Assert.True(
            JsonValuesEqual(expectedNode, actualNode),
            $"Expected {expected} to match {actual} semantically.");
    }

    private static bool JsonValuesEqual(JsonNode? left, JsonNode? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is JsonObject leftObject && right is JsonObject rightObject)
        {
            if (leftObject.Count != rightObject.Count)
            {
                return false;
            }

            foreach (var (key, leftValue) in leftObject)
            {
                if (!rightObject.TryGetPropertyValue(key, out var rightValue))
                {
                    return false;
                }

                if (!JsonValuesEqual(leftValue, rightValue))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is JsonArray leftArray && right is JsonArray rightArray)
        {
            if (leftArray.Count != rightArray.Count)
            {
                return false;
            }

            for (var i = 0; i < leftArray.Count; i++)
            {
                if (!JsonValuesEqual(leftArray[i], rightArray[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is JsonValue leftValueNode && right is JsonValue rightValueNode)
        {
            if (TryGetNumber(leftValueNode, out var leftNumber) && TryGetNumber(rightValueNode, out var rightNumber))
            {
                return leftNumber.Equals(rightNumber);
            }

            return JsonNode.DeepEquals(left, right);
        }

        return JsonNode.DeepEquals(left, right);
    }

    private static bool TryGetNumber(JsonValue value, out double number)
    {
        if (value.TryGetValue<double>(out number))
        {
            return true;
        }

        if (value.TryGetValue<decimal>(out var asDecimal))
        {
            number = (double)asDecimal;
            return true;
        }

        if (value.TryGetValue<long>(out var asLong))
        {
            number = asLong;
            return true;
        }

        if (value.GetValueKind() == JsonValueKind.Number)
        {
            return double.TryParse(
                value.ToJsonString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number);
        }

        number = default;
        return false;
    }
}
