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
        var found = new List<(JVal Value, bool CommaBefore)>();
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
                    found.Add((value, comma));
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

        return Select(found);
    }

    private static JVal? Select(List<(JVal Value, bool CommaBefore)> found)
    {
        if (found.Count == 0)
        {
            return null;
        }

        var anyContainer = found.Any(item => item.Value is JArr or JObj);
        var chosen = new List<JVal>();
        if (!anyContainer)
        {
            foreach (var item in found)
            {
                chosen.Add(item.Value);
            }
        }
        else
        {
            for (var i = 0; i < found.Count; i++)
            {
                var item = found[i];
                var inCommaSequence = item.CommaBefore || (i + 1 < found.Count && found[i + 1].CommaBefore);
                if (item.Value is JArr or JObj || inCommaSequence)
                {
                    chosen.Add(item.Value);
                }
            }
        }

        if (chosen.Count == 0)
        {
            return null;
        }

        if (chosen.Count == 1)
        {
            return chosen[0];
        }

        var array = new JArr();
        array.Items.AddRange(chosen);
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

    private JObj ParseObject(int depth)
    {
        EnsureDepth(depth);
        _index++;
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
        else if (!quoted && LooksLikeKeyAt(_index))
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
        Push(ContextKind.Array);
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

            items.Add(value);
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

            if (role == StringRole.Key && current == ':' && _index + 1 < _length && IsQuote(_text[_index + 1]) && IsPlainIdentifier(builder))
            {
                return builder.ToString();
            }

            if (IsMatchingClose(open, current))
            {
                if (role == StringRole.Key)
                {
                    if (ShouldEndKey())
                    {
                        _index++;
                        return builder.ToString();
                    }

                    builder.Append(current);
                    _index++;
                    continue;
                }

                if (IsImmediateWordContinuation(_index + 1))
                {
                    builder.Append(current);
                    _index++;
                    continue;
                }

                if (ClosesBeforeNextElement(_index))
                {
                    _index++;
                    return builder.ToString();
                }

                var next = SkipTriviaIndex(_index + 1);
                var structural = next >= _length || _text[next] is ',' or '}' or ']';
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

    private bool ClosesBeforeNextElement(int quoteIndex)
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

        return IsQuote(_text[next]) || _text[next] is '{' or '[' || IsNumberStart(next) || CanMatchLiteral(next);
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

        return cursor >= _length || _text[cursor] is ',' or '}' or ']' or '`';
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
        return key.Length == 0 ? null : key;
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
        return current is '{' or '[' or '('
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
                while (index < _length && _text[index] != '\n')
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
            text = text[..exponent];
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

    private static bool IsIdentStart(char value) => char.IsLetter(value) || value is '_' or '$';

    private static bool IsIdentPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '$' or '-';

    private enum ContextKind
    {
        Object,
        ObjectValue,
        Array,
    }

    private enum StringRole
    {
        Key,
        ObjectValue,
        Array,
        Other,
    }
}
