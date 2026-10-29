using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace JsonRepairNet.Internal;

/// <summary>
/// Repairs the JSON shapes LLMs usually break: quotes, commas, brackets, comments,
/// markdown fences, and values cut off mid-stream.
/// </summary>
internal sealed class RepairParser
{
    private const int MaxDepth = 256;
    private static readonly string[] LiteralWords = ["true", "false", "null", "none"];

    private readonly string _text;
    private readonly int _length;
    private readonly List<ContextKind> _stack = [];
    private int _index;

    private RepairParser(string text)
    {
        _text = text;
        _length = text.Length;
    }

    public static JVal? Parse(string input)
    {
        if (TryFences(input, out var fenced))
        {
            return fenced;
        }

        if (TryMissingOpeningBrace(input, out var braced))
        {
            return braced;
        }

        return new RepairParser(input).ParseTop();
    }

    private static bool TryFences(string input, out JVal? value)
    {
        value = null;
        if (input.IndexOf("```", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        var values = new List<JVal>();
        var cursor = 0;
        var foundFence = false;
        while (cursor < input.Length)
        {
            var open = input.IndexOf("```", cursor, StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            var content = open + 3;
            var close = input.IndexOf("```", content, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            foundFence = true;
            var body = ExtractFenceBody(input, content, close);
            var parsed = ParseFenceBody(body);
            if (parsed is not null)
            {
                values.Add(parsed);
            }

            cursor = close + 3;
        }

        if (!foundFence || values.Count == 0)
        {
            return false;
        }

        if (values.Count == 1)
        {
            value = values[0];
            return true;
        }

        var array = new JArr();
        array.Items.AddRange(values);
        value = array;
        return true;
    }

    private static JVal? ParseFenceBody(string body)
    {
        if (TryMissingOpeningBrace(body, out var braced))
        {
            return braced;
        }

        return new RepairParser(body).ParseTop();
    }

    private static string ExtractFenceBody(string input, int content, int close)
    {
        var line = input.IndexOf('\n', content);
        if (line >= 0 && line < close)
        {
            return input[(line + 1)..close];
        }

        var inline = input[content..close];
        if (inline.Length > 0 && char.IsLetter(inline[0]))
        {
            var end = 1;
            while (end < inline.Length && (char.IsLetterOrDigit(inline[end]) || inline[end] is '_' or '-' or '+'))
            {
                end++;
            }

            if (end < inline.Length && (char.IsWhiteSpace(inline[end]) || inline[end] is '{' or '[' or '"' or '\''))
            {
                return inline[end..];
            }
        }

        return inline;
    }

    private static bool TryMissingOpeningBrace(string input, out JVal? value)
    {
        value = null;
        var trimmed = input.Trim();
        if (trimmed.Length < 2 || !IsQuote(trimmed[0]) || trimmed[^1] != '}')
        {
            return false;
        }

        var candidate = "{" + trimmed;
        if (!JsonValidity.IsValid(candidate))
        {
            return false;
        }

        var node = JsonNode.Parse(candidate, documentOptions: JsonValidity.DocumentOptions);
        if (JsonText.FromNode(node) is not JObj obj)
        {
            return false;
        }

        value = obj;
        return true;
    }

    private JVal? ParseTop()
    {
        var found = new List<JVal>();
        var comma = false;
        var guard = 0;
        while (_index < _length && guard++ < _length + 2)
        {
            SkipTrivia();
            if (_index >= _length)
            {
                break;
            }

            if (_text[_index] == ',')
            {
                _index++;
                comma = true;
                continue;
            }

            if (StartsRootValue())
            {
                var mark = _index;
                var value = ParseRootValue();
                if (value is not null && _index > mark)
                {
                    if (found.Count == 0)
                    {
                        found.Add(value);
                    }
                    else if (IsTruthy(value))
                    {
                        if (!comma && IsSameStructure(found[^1], value))
                        {
                            found.RemoveAt(found.Count - 1);
                        }
                        else if (!IsTruthy(found[^1]))
                        {
                            found.RemoveAt(found.Count - 1);
                        }

                        found.Add(value);
                    }

                    comma = false;
                    continue;
                }

                if (_index == mark)
                {
                    _index++;
                }

                continue;
            }

            _index++;
        }

        if (found.Count == 0)
        {
            return null;
        }

        if (found.Count == 1)
        {
            return found[0];
        }

        var array = new JArr();
        array.Items.AddRange(found);
        return array;
    }

    private bool StartsRootValue()
    {
        var current = _text[_index];
        if (_index > 0 && (char.IsLetterOrDigit(current) || current == '_'))
        {
            var previous = _text[_index - 1];
            if (char.IsLetterOrDigit(previous) || previous == '_')
            {
                return false;
            }
        }

        return current is '{' or '['
            || (current == '(' && ParenCanStart())
            || (IsQuote(current) && RootQuotedStandsAlone())
            || IsNumberStart(_index)
            || CanMatchLiteral(_index);
    }

    private JVal? ParseRootValue()
    {
        var current = _text[_index];
        if (current == '{')
        {
            return ParseObject(0);
        }

        if (current == '[')
        {
            return ParseArray(0);
        }

        if (current == '(')
        {
            return ParseParen(0);
        }

        if (IsQuote(current))
        {
            return new JStr(ReadQuoted(current, StringRole.Other));
        }

        if (IsNumberStart(_index))
        {
            return ParseNumber();
        }

        if (TryConsumeLiteral(out var literal))
        {
            return literal;
        }

        return null;
    }

    private bool RootQuotedStandsAlone()
    {
        if (!TryScanQuoted(_index, out var after))
        {
            for (var cursor = _index + 1; cursor < _length; cursor++)
            {
                if (_text[cursor] is '{' or '[')
                {
                    return false;
                }
            }

            return true;
        }

        return SkipTriviaIndex(after) >= _length;
    }

    private JVal ParseObject(int depth)
    {
        EnsureDepth(depth);
        var innerStart = _index + 1;
        _index++;
        Push(ContextKind.Object);
        try
        {
            var obj = ParseObjectContents(depth);
            if (obj.Props.Count == 0 && _index > innerStart + 1)
            {
                _index = innerStart;
                var array = new JArr();
                array.Items.AddRange(ParseSequence(depth, '}'));
                return array;
            }

            return obj;
        }
        finally
        {
            Pop();
        }
    }

    private JObj ParseImplicitObject(int depth)
    {
        EnsureDepth(depth);
        Push(ContextKind.Object);
        try
        {
            return ParseObjectContents(depth);
        }
        finally
        {
            Pop();
        }
    }

    private JObj ParseObjectContents(int depth)
    {
        var obj = new JObj();
        var guard = 0;
        while (_index < _length && guard++ < _length + 2)
        {
            var before = _index;
            SkipTrivia();
            if (_index >= _length)
            {
                break;
            }

            var current = _text[_index];
            if (current == '}')
            {
                _index++;
                SkipTrivia();
                if (_index < _length && _text[_index] == '}' && ParentIs(ContextKind.Array))
                {
                    _index++;
                }

                if (TryContinueTopLevelMembers())
                {
                    continue;
                }

                break;
            }

            if (current is ']' or ')')
            {
                break;
            }

            if (current == ',')
            {
                _index++;
                continue;
            }

            if (current == ':')
            {
                _index++;
                continue;
            }

            if (current == '[' && TryMergeArrayContinuation(obj, depth))
            {
                continue;
            }

            if (IsStrayQuote())
            {
                _index++;
                continue;
            }

            if (!TryReadMember(obj, depth))
            {
                if (_index == before)
                {
                    _index++;
                }
            }
        }

        return obj;
    }

    private bool TryReadMember(JObj obj, int depth)
    {
        if (!CanStartKey(_index))
        {
            return false;
        }

        var quoted = IsQuote(_text[_index]);
        var key = ReadKey();
        if (key is null)
        {
            return true;
        }

        SkipTrivia();
        var hadColon = _index < _length && _text[_index] == ':';
        if (hadColon)
        {
            _index++;
            SkipTrivia();
            while (_index < _length && _text[_index] == ':')
            {
                _index++;
                SkipTrivia();
            }
        }
        else if (_index < _length && _text[_index] == ',')
        {
            var afterComma = SkipTriviaIndex(_index + 1);
            if (afterComma < _length && IsNumberStart(afterComma))
            {
                _index++;
                SkipTrivia();
            }
            else
            {
                return true;
            }
        }
        else if (!quoted && LooksLikeKeyAt(_index))
        {
            return true;
        }
        else if (_index < _length && _text[_index] == '}')
        {
            return true;
        }
        else if (!quoted && obj.Props.Count > 0 && !LooksLikeValueStart(_index))
        {
            return true;
        }

        SkipTrivia();
        JVal value;
        if (_index >= _length || _text[_index] is ',' or '}' or ']' or ')')
        {
            value = JStr.Empty;
        }
        else
        {
            var mark = _index;
            Push(ContextKind.ObjectValue);
            try
            {
                value = ParseValue(depth + 1) ?? JStr.Empty;
            }
            finally
            {
                Pop();
            }

            if (_index == mark)
            {
                _index++;
            }

            SkipTrivia();
            while (_index < _length && IsStrayQuote())
            {
                _index++;
                SkipTrivia();
            }
        }

        obj.Set(key, value);
        SkipNonMemberGarbage();
        return true;
    }

    private JArr ParseArray(int depth)
    {
        EnsureDepth(depth);
        _index++;
        Push(ContextKind.Array);
        try
        {
            var array = new JArr();
            array.Items.AddRange(ParseSequence(depth, ']'));
            return array;
        }
        finally
        {
            Pop();
        }
    }

    private JVal ParseParen(int depth)
    {
        EnsureDepth(depth);
        var tuple = IsExplicitTuple(_index);
        _index++;
        Push(ContextKind.Paren);
        List<JVal> items;
        try
        {
            items = ParseSequence(depth, ')');
        }
        finally
        {
            Pop();
        }

        if (!tuple && items.Count == 1)
        {
            return items[0];
        }

        var array = new JArr();
        array.Items.AddRange(items);
        return array;
    }

    private List<JVal> ParseSequence(int depth, char closer)
    {
        var items = new List<JVal>();
        var guard = 0;
        while (_index < _length && guard++ < _length + 2)
        {
            var before = _index;
            SkipTrivia();
            if (_index >= _length)
            {
                break;
            }

            var current = _text[_index];
            if (current == closer)
            {
                _index++;
                break;
            }

            if (closer == ']' && current == '}')
            {
                break;
            }

            if (closer == ']' && current == ')')
            {
                _index++;
                break;
            }

            if (closer == ')' && current == ']')
            {
                _index++;
                break;
            }

            if (current == ',')
            {
                _index++;
                continue;
            }

            if (MatchAt(_index, "..."))
            {
                _index += 3;
                continue;
            }

            if (LooksLikeKeyAt(_index))
            {
                if (ShouldYieldToParentObject(items))
                {
                    break;
                }

                items.Add(ParseImplicitObject(depth + 1));
                continue;
            }

            var value = ParseValue(depth + 1);
            if (value is null)
            {
                if (_index == before)
                {
                    _index++;
                }

                continue;
            }

            if (IsStrictlyEmpty(value))
            {
                SkipTrivia();
                var atDelimiter = _index < _length && (_text[_index] == ',' || _text[_index] == closer);
                if (!atDelimiter || (value is JObj && closer == ']' && _index < _length && _text[_index] == ']'))
                {
                    continue;
                }
            }

            items.Add(value);
            if (value is JArr)
            {
                SkipTrailingArrayGarbage(closer);
            }
        }

        return items;
    }

    private bool ShouldYieldToParentObject(List<JVal> items)
    {
        if (items.Count == 0 || !ParentIs(ContextKind.ObjectValue))
        {
            return false;
        }

        foreach (var item in items)
        {
            if (item is JArr or JObj)
            {
                return false;
            }
        }

        return true;
    }

    private JVal? ParseValue(int depth)
    {
        EnsureDepth(depth);
        SkipTrivia();
        if (_index >= _length)
        {
            return null;
        }

        var current = _text[_index];
        if (current == '{')
        {
            return ParseObject(depth);
        }

        if (current == '[')
        {
            return ParseArray(depth);
        }

        if (current == '(' && ParenCanStart())
        {
            return ParseParen(depth);
        }

        if (IsQuote(current))
        {
            return new JStr(ReadQuoted(current, CurrentStringRole()));
        }

        if (current == '.' && MatchAt(_index, "..."))
        {
            return null;
        }

        if (IsNumberStart(_index))
        {
            return ParseNumber();
        }

        if (TryConsumeLiteral(out var literal))
        {
            return literal;
        }

        if (IsIdentStart(current))
        {
            return new JStr(ReadBare());
        }

        return null;
    }

    private string ReadQuoted(char open, StringRole role)
    {
        _index++;
        if (role != StringRole.Key && TryConsumeRedundantDoubledQuote(open, out var simple))
        {
            return simple;
        }

        var builder = new StringBuilder();
        while (_index < _length)
        {
            var current = _text[_index];
            if (current == '\\')
            {
                AppendEscape(builder);
                continue;
            }

            if (role == StringRole.ObjectValue && current == ',' && CommaStartsNextMember(_index))
            {
                return builder.ToString().TrimEnd();
            }

            if (role != StringRole.Key && current is '}' or ']' && BracketEndsString(_index))
            {
                return builder.ToString().TrimEnd();
            }

            if (role == StringRole.Key && current == ':' && IsPlainIdentifier(builder) && KeyColonEndsUnclosedKey())
            {
                return builder.ToString().TrimEnd();
            }

            if (IsMatchingClose(open, current))
            {
                if (role == StringRole.Key)
                {
                    if (builder.Length == 0)
                    {
                        var afterEmpty = SkipTriviaIndex(_index + 1);
                        if (afterEmpty < _length && _text[afterEmpty] == ':')
                        {
                            _index++;
                            return string.Empty;
                        }

                        _index++;
                        continue;
                    }

                    if (ShouldEndKey())
                    {
                        _index++;
                        return builder.ToString().TrimEnd();
                    }

                    builder.Append(current);
                    _index++;
                    continue;
                }

                if (NextQuotedKey(_index + 1))
                {
                    _index++;
                    return builder.ToString();
                }

                if (IsImmediateWordContinuation(_index + 1))
                {
                    builder.Append(current);
                    _index++;
                    continue;
                }

                if (ClosesBeforeNextElement(_index, role, builder.ToString().Contains('"')))
                {
                    _index++;
                    return builder.ToString();
                }

                var next = SkipTriviaIndex(_index + 1);
                var structural = next >= _length || _text[next] is ',' or '}' or ']'
                    || (_text[next] == ')' && _stack.Contains(ContextKind.Paren));
                if (!structural)
                {
                    builder.Append(current);
                    _index++;
                    continue;
                }

                var odd = CountDelimiter(builder, open) % 2 == 1;
                var atEnd = next >= _length;
                var atBrace = !atEnd && _text[next] is '}' or ']';
                if (odd && (atEnd || atBrace))
                {
                    builder.Append(current);
                    _index++;
                    return builder.ToString();
                }

                _index++;
                return builder.ToString();
            }

            builder.Append(current);
            _index++;
        }

        return builder.ToString().TrimEnd();
    }

    private bool TryContinueTopLevelMembers()
    {
        if (_stack.Count != 1 || _stack[0] != ContextKind.Object)
        {
            return false;
        }

        var mark = _index;
        SkipTrivia();
        if (_index >= _length || _text[_index] != ',')
        {
            _index = mark;
            return false;
        }

        _index++;
        SkipTrivia();
        if (_index < _length && IsQuote(_text[_index]) && CanStartKey(_index))
        {
            return true;
        }

        _index = mark;
        return false;
    }

    private bool TryReadBalancedContainer(out string container)
    {
        container = string.Empty;
        if (_index >= _length)
        {
            return false;
        }

        var open = _text[_index];
        var close = open switch
        {
            '{' => '}',
            '[' => ']',
            _ => '\0',
        };
        if (close == '\0')
        {
            return false;
        }

        var depth = 0;
        var cursor = _index;
        while (cursor < _length)
        {
            var current = _text[cursor];
            if (current == open)
            {
                depth++;
            }
            else if (current == close)
            {
                depth--;
                if (depth == 0)
                {
                    container = _text[_index..(cursor + 1)];
                    _index = cursor + 1;
                    return true;
                }
            }

            cursor++;
        }

        return false;
    }

    private bool HashCommentEndsAt(char current)
    {
        if (current is '\n' or '\r')
        {
            return true;
        }

        if (current == '}' && _stack.Contains(ContextKind.ObjectValue))
        {
            return true;
        }

        if (current == ']' && _stack.Contains(ContextKind.Array))
        {
            return true;
        }

        return current == ':' && _stack.Contains(ContextKind.Object) && !_stack.Contains(ContextKind.ObjectValue);
    }

    private bool KeyColonEndsUnclosedKey()
    {
        if (_index + 1 < _length && IsQuote(_text[_index + 1]))
        {
            return true;
        }

        var cursor = _index + 1;
        while (cursor < _length)
        {
            var current = _text[cursor];
            if (IsQuote(current))
            {
                return false;
            }

            if (current == '}')
            {
                return true;
            }

            cursor++;
        }

        return true;
    }

    private bool TryConsumeRedundantDoubledQuote(char open, out string simple)
    {
        simple = string.Empty;
        if (_index >= _length || !IsMatchingClose(open, _text[_index]))
        {
            return false;
        }

        var middleStart = _index + 1;
        var cursor = middleStart;
        while (cursor < _length && _text[cursor] != '\\' && !IsMatchingClose(open, _text[cursor]))
        {
            cursor++;
        }

        if (cursor >= _length || cursor == middleStart || !IsMatchingClose(open, _text[cursor]))
        {
            return false;
        }

        var after = SkipTriviaIndex(cursor + 1);
        if (after < _length && _text[after] is not (',' or '}' or ']'))
        {
            return false;
        }

        simple = _text[middleStart..cursor];
        _index = cursor + 1;
        return true;
    }

    private bool ShouldEndKey()
    {
        var next = SkipTriviaIndex(_index + 1);
        return next >= _length || _text[next] is ':' or ',' or '}' or ']';
    }

    private bool ClosesBeforeNextElement(int quoteIndex, StringRole role, bool hasInnerQuote)
    {
        var cursor = quoteIndex + 1;
        if (cursor >= _length || !char.IsWhiteSpace(_text[cursor]))
        {
            return false;
        }

        var next = SkipTriviaIndex(cursor);
        if (next >= _length)
        {
            return false;
        }

        if (IsQuote(_text[next]) || _text[next] is '{' or '[' || IsNumberStart(next) || CanMatchLiteral(next))
        {
            return true;
        }

        if (role == StringRole.Array && !hasInnerQuote && IsIdentStart(_text[next]))
        {
            return true;
        }

        if (role == StringRole.ObjectValue && IsIdentStart(_text[next]))
        {
            var scan = next;
            while (scan < _length && (char.IsWhiteSpace(_text[scan]) || IsIdentStart(_text[scan]) || IsIdentPart(_text[scan])))
            {
                scan++;
            }

            return scan >= _length || _text[scan] == '}';
        }

        return false;
    }

    private bool IsImmediateWordContinuation(int index) =>
        index < _length && (char.IsLetterOrDigit(_text[index]) || _text[index] == '_');

    private bool CommaStartsNextMember(int commaIndex)
    {
        var next = SkipTriviaIndex(commaIndex + 1);
        if (next >= _length || _text[next] is '}' or ']')
        {
            return true;
        }

        return LooksLikeKeyAt(next);
    }

    private bool BracketEndsString(int bracketIndex)
    {
        var cursor = bracketIndex + 1;
        while (cursor < _length && (char.IsWhiteSpace(_text[cursor]) || _text[cursor] == '`'))
        {
            cursor++;
        }

        return cursor >= _length
            || _text[cursor] is ',' or '}' or ']' or '`'
            || (_text[cursor] == ')' && _stack.Contains(ContextKind.Paren));
    }

    private string? ReadKey()
    {
        SkipTrivia();
        if (_index >= _length)
        {
            return null;
        }

        var current = _text[_index];
        if (IsQuote(current))
        {
            return ReadQuoted(current, StringRole.Key);
        }

        if (!IsIdentStart(current) && !char.IsDigit(current) && current is not ('-' or '.'))
        {
            return null;
        }

        var start = _index;
        while (_index < _length)
        {
            var ch = _text[_index];
            if (char.IsWhiteSpace(ch) || ch is ':' or ',' or '}' or ']' or ')' or '{' or '[')
            {
                break;
            }

            if (IsCommentStart(_index))
            {
                break;
            }

            _index++;
        }

        var key = _text[start.._index].Trim();
        if (key.Length > 1 && IsQuote(key[^1]) && !HasQuote(key.AsSpan(0, key.Length - 1)))
        {
            // A bare key with only a trailing quote (b": 2): the opening quote went
            // missing, so treat it as the unquoted key b. Only when a colon follows.
            var colon = SkipTriviaIndex(_index);
            if (colon < _length && _text[colon] == ':')
            {
                key = key[..^1];
            }
        }

        return key.Length == 0 ? null : key;
    }

    private static bool HasQuote(ReadOnlySpan<char> text)
    {
        foreach (var ch in text)
        {
            if (IsQuote(ch))
            {
                return true;
            }
        }

        return false;
    }

    private string ReadBare()
    {
        var builder = new StringBuilder();
        while (_index < _length)
        {
            var current = _text[_index];
            if (current is '}' or ']' or ')')
            {
                break;
            }

            if ((current is '{' or '[') && TryReadBalancedContainer(out var container))
            {
                builder.Append(container);
                continue;
            }

            if (IsCommentStart(_index) && (builder.Length == 0 || char.IsWhiteSpace(builder[^1])))
            {
                break;
            }

            if (current == ',' && CommaEndsBare(_index))
            {
                break;
            }

            if (current == '`')
            {
                break;
            }

            if (IsQuote(current))
            {
                if (_stack.Count > 0 && _stack[^1] == ContextKind.ObjectValue)
                {
                    break;
                }

                if (QuoteEndsBare(_index) || LooksLikeKeyAt(_index))
                {
                    if (QuoteEndsBare(_index))
                    {
                        _index++;
                    }

                    break;
                }

                builder.Append(current);
                _index++;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                var next = SkipTriviaIndex(_index);
                if (next >= _length || _text[next] is '}' or ']' or ')')
                {
                    break;
                }

                if (LooksLikeKeyAt(next) || IsCommentStart(next))
                {
                    break;
                }

                if (_text[next] == ',' && CommaEndsBare(next))
                {
                    break;
                }

                builder.Append(current);
                _index++;
                continue;
            }

            builder.Append(current);
            _index++;
        }

        return builder.ToString().Trim();
    }

    private bool CommaEndsBare(int commaIndex)
    {
        var next = SkipTriviaIndex(commaIndex + 1);
        if (next >= _length || _text[next] is '}' or ']' or ')')
        {
            return true;
        }

        return LooksLikeKeyAt(next);
    }

    private bool QuoteEndsBare(int quoteIndex)
    {
        var next = SkipTriviaIndex(quoteIndex + 1);
        return next >= _length || _text[next] is ',' or '}' or ']' or ')';
    }

    private JVal ParseNumber()
    {
        var start = _index;
        if (_text[_index] == '+')
        {
            _index++;
        }

        if (_index < _length && _text[_index] == '-')
        {
            _index++;
        }

        var exponent = false;
        while (_index < _length)
        {
            var current = _text[_index];
            if (char.IsDigit(current) || current == '_')
            {
                _index++;
                continue;
            }

            if (current is '.' or '/')
            {
                _index++;
                continue;
            }

            if (current is 'e' or 'E' && !exponent)
            {
                exponent = true;
                _index++;
                continue;
            }

            if (current is '+' or '-' && exponent && _index > start && _text[_index - 1] is 'e' or 'E')
            {
                _index++;
                if (_index >= _length || !char.IsDigit(_text[_index]))
                {
                    _index--;
                    break;
                }

                continue;
            }

            if (current == ',' && _stack.Count > 0 && _stack[^1] == ContextKind.ObjectValue)
            {
                _index++;
                continue;
            }

            break;
        }

        if (_index < _length && (char.IsLetter(_text[_index]) || _text[_index] == '_'))
        {
            while (_index < _length && !IsValueDelimiter(_text[_index]) && !char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }

            return new JStr(_text[start.._index]);
        }

        if (_index < _length && _text[_index] == '-' && _index + 1 < _length && char.IsDigit(_text[_index + 1]))
        {
            while (_index < _length && !IsValueDelimiter(_text[_index]) && !char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }

            return new JStr(_text[start.._index]);
        }

        var token = _text[start.._index];
        var strippedSign = false;
        while (token.Length > 0 && token[^1] is ',' or '+' or '-' or 'e' or 'E' or '/')
        {
            if (token[^1] is 'e' or 'E')
            {
                strippedSign |= _index < _length && _text[_index] is '+' or '-';
                if (!strippedSign)
                {
                    // Upstream: a bare trailing exponent marker is dropped ("1e" -> 1),
                    // but "1e+" / "1e-" keep "1e" as a string.
                    token = token[..^1];
                    _index--;
                }

                break;
            }

            strippedSign |= token[^1] is '+' or '-';

            token = token[..^1];
            _index--;
        }

        if (token.Contains(','))
        {
            return new JStr(token);
        }

        return TryNormalizeNumber(token, out var literal) ? new JNum(literal) : new JStr(token);
    }

    private bool TryConsumeLiteral(out JVal value)
    {
        if (!MatchLiteral(_index, out var length, out value))
        {
            return false;
        }

        _index += length;
        return true;
    }

    private bool CanMatchLiteral(int index) => MatchLiteral(index, out _, out _);

    private bool MatchLiteral(int index, out int length, out JVal value)
    {
        foreach (var word in LiteralWords)
        {
            if (index + word.Length > _length)
            {
                continue;
            }

            var matches = true;
            for (var offset = 0; offset < word.Length; offset++)
            {
                if (char.ToLowerInvariant(_text[index + offset]) != word[offset])
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
            {
                continue;
            }

            var after = index + word.Length;
            if (after < _length && (char.IsLetterOrDigit(_text[after]) || _text[after] == '_'))
            {
                continue;
            }

            length = word.Length;
            value = word switch
            {
                "true" => JBool.True,
                "false" => JBool.False,
                _ => JNull.Instance,
            };
            return true;
        }

        length = 0;
        value = JNull.Instance;
        return false;
    }

    private bool NextQuotedKey(int index)
    {
        index = SkipTriviaIndex(index);
        return index < _length && IsQuote(_text[index]) && LooksLikeKeyAt(index);
    }

    private bool LooksLikeKeyAt(int index)
    {
        index = SkipTriviaIndex(index);
        if (index >= _length)
        {
            return false;
        }

        var current = _text[index];
        if (IsQuote(current))
        {
            if (!TryScanQuoted(index, out var after))
            {
                return false;
            }

            after = SkipTriviaIndex(after);
            return after < _length && _text[after] == ':';
        }

        if (!IsIdentStart(current) && !char.IsDigit(current))
        {
            return false;
        }

        var cursor = index + 1;
        while (cursor < _length && IsIdentPart(_text[cursor]))
        {
            cursor++;
        }

        cursor = SkipTriviaIndex(cursor);
        return cursor < _length && _text[cursor] == ':';
    }

    private bool LooksLikeValueStart(int index)
    {
        index = SkipTriviaIndex(index);
        if (index >= _length)
        {
            return false;
        }

        var current = _text[index];
        return current is '{' or '[' or '('
            || IsQuote(current)
            || IsNumberStart(index)
            || CanMatchLiteral(index)
            || IsIdentStart(current);
    }

    private bool ParenCanStart()
    {
        var next = SkipTriviaIndex(_index + 1);
        if (next >= _length)
        {
            return false;
        }

        var current = _text[next];
        return current is '{' or '[' or '(' or ')'
            || IsQuote(current)
            || char.IsDigit(current)
            || current is '-' or '.'
            || CanMatchLiteral(next);
    }

    private bool IsExplicitTuple(int openIndex)
    {
        var cursor = openIndex + 1;
        var parentheses = 0;
        var squares = 0;
        var braces = 0;
        char? quote = null;
        var escaped = false;
        var sawContent = false;
        while (cursor < _length)
        {
            var current = _text[cursor];
            if (quote is not null)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (IsMatchingClose(quote.Value, current))
                {
                    quote = null;
                }

                cursor++;
                continue;
            }

            if (current == '\\')
            {
                cursor++;
                continue;
            }

            if (IsQuote(current))
            {
                quote = current;
                sawContent = parentheses == 0 && squares == 0 && braces == 0;
                cursor++;
                continue;
            }

            if (!char.IsWhiteSpace(current) && current is not (',' or ')') && parentheses == 0 && squares == 0 && braces == 0)
            {
                sawContent = true;
            }

            switch (current)
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    if (parentheses == 0 && squares == 0 && braces == 0)
                    {
                        return !sawContent;
                    }

                    if (parentheses > 0)
                    {
                        parentheses--;
                    }

                    break;
                case '[':
                    squares++;
                    break;
                case ']':
                    if (squares > 0)
                    {
                        squares--;
                    }

                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    if (braces > 0)
                    {
                        braces--;
                    }

                    break;
                case ',' when parentheses == 0 && squares == 0 && braces == 0:
                    return true;
            }

            cursor++;
        }

        return !sawContent;
    }

    private bool IsStrayQuote()
    {
        if (_index >= _length || !IsQuote(_text[_index]))
        {
            return false;
        }

        if (LooksLikeKeyAt(_index))
        {
            return false;
        }

        var next = SkipTriviaIndex(_index + 1);
        return next >= _length || _text[next] is ',' or '}' or ']';
    }

    private bool CanStartKey(int index)
    {
        if (index >= _length)
        {
            return false;
        }

        var current = _text[index];
        return IsQuote(current) || IsIdentStart(current) || char.IsDigit(current) || current is '-' or '.';
    }

    private bool IsNumberStart(int index)
    {
        if (index >= _length)
        {
            return false;
        }

        var current = _text[index];
        if (char.IsDigit(current) || current == '.')
        {
            return true;
        }

        return current is '+' or '-' && index + 1 < _length && (char.IsDigit(_text[index + 1]) || _text[index + 1] == '.');
    }

    private bool TryScanQuoted(int index, out int after)
    {
        var open = _text[index];
        var cursor = index + 1;
        while (cursor < _length)
        {
            if (_text[cursor] == '\\')
            {
                cursor = Math.Min(cursor + 2, _length);
                continue;
            }

            if (IsMatchingClose(open, _text[cursor]))
            {
                after = cursor + 1;
                return true;
            }

            cursor++;
        }

        after = _length;
        return false;
    }

    private void AppendEscape(StringBuilder builder)
    {
        _index++;
        if (_index >= _length)
        {
            builder.Append('\\');
            return;
        }

        var current = _text[_index++];
        switch (current)
        {
            case '"' or '\'' or '\\' or '/':
                builder.Append(current);
                break;
            case 'b':
                builder.Append('\b');
                break;
            case 'f':
                builder.Append('\f');
                break;
            case 'n':
                builder.Append('\n');
                break;
            case 'r':
                builder.Append('\r');
                break;
            case 't':
                builder.Append('\t');
                break;
            case 'u':
                if (TryReadHex(out var hex))
                {
                    builder.Append(hex);
                }
                else
                {
                    builder.Append("\\u");
                }

                break;
            default:
                builder.Append('\\');
                builder.Append(current);
                break;
        }
    }

    private bool TryReadHex(out char value)
    {
        if (_index + 4 > _length)
        {
            value = '\0';
            return false;
        }

        var hex = _text.AsSpan(_index, 4);
        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
        {
            value = '\0';
            return false;
        }

        _index += 4;
        value = (char)code;
        return true;
    }

    private void SkipTrivia() => _index = SkipTriviaIndex(_index);

    private int SkipTriviaIndex(int index)
    {
        while (index < _length)
        {
            var current = _text[index];
            if (char.IsWhiteSpace(current) || current == '`')
            {
                index++;
                continue;
            }

            if (current == '#')
            {
                index++;
                while (index < _length && !HashCommentEndsAt(_text[index]))
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < _length && _text[index + 1] == '/')
            {
                index += 2;
                while (index < _length && _text[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < _length && _text[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < _length && !(_text[index] == '*' && _text[index + 1] == '/'))
                {
                    index++;
                }

                index = Math.Min(index + 2, _length);
                continue;
            }

            break;
        }

        return index;
    }

    private bool IsCommentStart(int index)
    {
        if (index >= _length)
        {
            return false;
        }

        if (_text[index] == '#')
        {
            return true;
        }

        return _text[index] == '/' && index + 1 < _length && _text[index + 1] is '/' or '*';
    }

    private bool MatchAt(int index, string value)
    {
        if (index + value.Length > _length)
        {
            return false;
        }

        for (var offset = 0; offset < value.Length; offset++)
        {
            if (_text[index + offset] != value[offset])
            {
                return false;
            }
        }

        return true;
    }

    private StringRole CurrentStringRole()
    {
        if (_stack.Count > 0 && _stack[^1] == ContextKind.ObjectValue)
        {
            return StringRole.ObjectValue;
        }

        return _stack.Contains(ContextKind.Array) ? StringRole.Array : StringRole.Other;
    }

    private void Push(ContextKind kind) => _stack.Add(kind);

    private void Pop() => _stack.RemoveAt(_stack.Count - 1);

    private bool ParentIs(ContextKind kind) => _stack.Count >= 2 && _stack[^2] == kind;

    private static void EnsureDepth(int depth)
    {
        if (depth > MaxDepth)
        {
            throw new JsonRepairException("Input nesting exceeds the supported parser recursion depth.");
        }
    }

    private static bool TryNormalizeNumber(string token, out string literal)
    {
        literal = string.Empty;
        if (token.Length == 0 || token.Contains('/'))
        {
            return false;
        }

        var text = token.Replace("_", "", StringComparison.Ordinal);
        if (text.StartsWith('+'))
        {
            text = text[1..];
        }

        if (text.Length == 0)
        {
            return false;
        }

        var exponent = text.LastIndexOfAny(['e', 'E']);
        if (exponent >= 0 && (exponent == text.Length - 1 || (exponent == text.Length - 2 && text[^1] is '+' or '-')))
        {
            return false;
        }

        if (text.EndsWith('.'))
        {
            text += "0";
        }

        if (text.StartsWith('.'))
        {
            text = "0" + text;
        }
        else if (text.StartsWith("-."))
        {
            text = "-0" + text[1..];
        }

        if (text.Length == 0 || !IsJsonNumber(text))
        {
            return false;
        }

        literal = text;
        return true;
    }

    private static bool IsJsonNumber(string text)
    {
        var index = 0;
        if (text[index] == '-')
        {
            index++;
            if (index >= text.Length)
            {
                return false;
            }
        }

        if (text[index] == '0')
        {
            index++;
        }
        else if (char.IsDigit(text[index]) && text[index] != '0')
        {
            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }
        }
        else
        {
            return false;
        }

        if (index < text.Length && text[index] == '.')
        {
            index++;
            if (index >= text.Length || !char.IsDigit(text[index]))
            {
                return false;
            }

            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }
        }

        if (index < text.Length && text[index] is 'e' or 'E')
        {
            index++;
            if (index < text.Length && text[index] is '+' or '-')
            {
                index++;
            }

            if (index >= text.Length || !char.IsDigit(text[index]))
            {
                return false;
            }

            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }
        }

        return index == text.Length;
    }

    private static int CountDelimiter(StringBuilder builder, char open)
    {
        var count = 0;
        for (var index = 0; index < builder.Length; index++)
        {
            if (builder[index] == open || IsMatchingClose(open, builder[index]))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsPlainIdentifier(StringBuilder builder)
    {
        if (builder.Length == 0 || (!char.IsLetter(builder[0]) && builder[0] != '_'))
        {
            return false;
        }

        for (var index = 1; index < builder.Length; index++)
        {
            if (!char.IsLetterOrDigit(builder[index]) && builder[index] != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValueDelimiter(char value) =>
        value is ',' or '}' or ']' or ')' or '{' or '[' or ':' or '"' or '\'' or '`';

    private static bool IsQuote(char value) =>
        value is '"' or '\'' or '\u201C' or '\u201D' or '\u2018' or '\u2019';

    private static bool IsMatchingClose(char open, char value) => open switch
    {
        '"' => value == '"',
        '\'' => value == '\'',
        '\u201C' or '\u201D' => value is '\u201D' or '\u201C' or '"',
        '\u2018' or '\u2019' => value is '\u2019' or '\u2018' or '\'',
        _ => false,
    };

    private bool TryMergeArrayContinuation(JObj obj, int depth)
    {
        if (obj.Props.Count == 0 || obj.Props[^1].Value is not JArr previous)
        {
            return false;
        }

        var incoming = ParseArray(depth + 1);
        MergeArrayContinuation(previous, incoming);
        SkipTrivia();
        if (_index < _length && _text[_index] == ',')
        {
            _index++;
        }

        return true;
    }

    private static void MergeArrayContinuation(JArr previous, JArr incoming)
    {
        var nestedLength = 0;
        var nestedCount = 0;
        var nestedAgree = true;
        foreach (var item in previous.Items)
        {
            if (item is not JArr nested)
            {
                continue;
            }

            if (nestedCount == 0)
            {
                nestedLength = nested.Items.Count;
            }
            else if (nested.Items.Count != nestedLength)
            {
                nestedAgree = false;
            }

            nestedCount++;
        }

        if (nestedCount > 0 && nestedAgree && nestedLength > 0)
        {
            var tail = new List<JVal>();
            while (previous.Items.Count > 0 && previous.Items[^1] is not JArr)
            {
                tail.Add(previous.Items[^1]);
                previous.Items.RemoveAt(previous.Items.Count - 1);
            }

            tail.Reverse();
            if (tail.Count > 0 && tail.Count % nestedLength == 0)
            {
                for (var offset = 0; offset < tail.Count; offset += nestedLength)
                {
                    var row = new JArr();
                    for (var i = 0; i < nestedLength; i++)
                    {
                        row.Items.Add(tail[offset + i]);
                    }

                    previous.Items.Add(row);
                }
            }
            else
            {
                previous.Items.AddRange(tail);
            }

            if (incoming.Items.Count == 0)
            {
                return;
            }

            var incomingAllArrays = true;
            foreach (var item in incoming.Items)
            {
                if (item is not JArr)
                {
                    incomingAllArrays = false;
                    break;
                }
            }

            if (incomingAllArrays)
            {
                previous.Items.AddRange(incoming.Items);
            }
            else
            {
                previous.Items.Add(incoming);
            }

            return;
        }

        if (incoming.Items.Count == 1 && incoming.Items[0] is JArr only)
        {
            previous.Items.AddRange(only.Items);
            return;
        }

        previous.Items.AddRange(incoming.Items);
    }

    private void SkipTrailingArrayGarbage(char closer)
    {
        SkipTrivia();
        if (_index >= _length || _text[_index] is ',' || _text[_index] == closer)
        {
            return;
        }

        if (!IsIdentStart(_text[_index]))
        {
            return;
        }

        var cursor = _index;
        while (cursor < _length)
        {
            var current = _text[cursor];
            if (current == closer || current == ',')
            {
                break;
            }

            if (!char.IsWhiteSpace(current) && !IsIdentStart(current) && !IsIdentPart(current))
            {
                return;
            }

            cursor++;
        }

        _index = cursor;
    }

    private void SkipNonMemberGarbage()
    {
        SkipTrivia();
        if (_index >= _length || _text[_index] is ',' or '}' or ']' or ')' || LooksLikeKeyAt(_index) || _text[_index] == '[')
        {
            return;
        }

        while (_index < _length)
        {
            var current = _text[_index];
            if (current is ',' or '}' or ']' or ')')
            {
                break;
            }

            if (LooksLikeKeyAt(_index) || current == '[')
            {
                break;
            }

            _index++;
        }
    }

    private static bool IsTruthy(JVal value) => value switch
    {
        JNull => false,
        JBool flag => flag.Value,
        JNum number => IsNonZeroNumber(number.Literal),
        JStr text => text.Value.Length > 0,
        JArr array => array.Items.Count > 0,
        JObj obj => obj.Props.Count > 0,
        _ => true,
    };

    private static bool IsNonZeroNumber(string literal)
    {
        if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return literal.Length > 0;
        }

        return number != 0;
    }

    private static bool IsStrictlyEmpty(JVal value) => value switch
    {
        JStr text => text.Value.Length == 0,
        JArr array => array.Items.Count == 0,
        JObj obj => obj.Props.Count == 0,
        _ => false,
    };

    private static bool IsSameStructure(JVal left, JVal right)
    {
        if (left.GetType() != right.GetType())
        {
            return false;
        }

        if (left is JObj leftObject && right is JObj rightObject)
        {
            if (leftObject.Props.Count != rightObject.Props.Count)
            {
                return false;
            }

            foreach (var pair in leftObject.Props)
            {
                var found = false;
                foreach (var other in rightObject.Props)
                {
                    if (other.Key != pair.Key)
                    {
                        continue;
                    }

                    if (!IsSameStructure(pair.Value, other.Value))
                    {
                        return false;
                    }

                    found = true;
                    break;
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        if (left is JArr leftArray && right is JArr rightArray)
        {
            if (leftArray.Items.Count != rightArray.Items.Count)
            {
                return false;
            }

            for (var i = 0; i < leftArray.Items.Count; i++)
            {
                if (!IsSameStructure(leftArray.Items[i], rightArray.Items[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return true;
    }

    private static bool IsIdentStart(char value) => char.IsLetter(value) || value is '_' or '$';

    private static bool IsIdentPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '$' or '-';

    private enum ContextKind
    {
        Object,
        ObjectValue,
        Array,
        Paren,
    }

    private enum StringRole
    {
        Key,
        ObjectValue,
        Array,
        Other,
    }
}
