using System.Text.RegularExpressions;

namespace XtraSearch;

/// <summary>Compiles expressions once and tracks Boolean terms across an entire file.</summary>
public sealed class ExpressionMatcher
{
    private readonly List<Func<string, bool>> terms = [];
    private readonly Func<bool[], bool> evaluate;
    private readonly bool lineByLine;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public ExpressionMatcher(string expression, string mode, bool matchCase, bool wholeWord = false, bool lineByLine = false)
    {
        this.lineByLine = lineByLine;
        Func<string, string, bool, Func<string, bool>> compile = (term, type, quoted) =>
        {
            if (type == "Plain text" && !wholeWord)
            {
                var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                return text => text.Contains(term, comparison);
            }

            var pattern = type switch
            {
                "Regex" => term,
                "Wildcard" => WildcardPattern(term),
                "Boolean" when !quoted => Regex.Escape(term).Replace("\\*", "\\w*").Replace("\\?", "\\w"),
                _ => Regex.Escape(term)
            };
            var regex = CreateRegex(pattern, matchCase, wholeWord);
            return regex.IsMatch;
        };

        if (mode is not ("Boolean" or "Boolean Regex"))
        {
            terms.Add(compile(expression, mode, false));
            evaluate = values => values[0];
            return;
        }

        var tokens = Regex.Matches(expression, "\"[^\"]*\"|\\(|\\)|[^\\s()]+").Select(x => x.Value).ToArray();
        var position = 0;
        Func<bool[], bool> Atom()
        {
            if (position >= tokens.Length) throw new ArgumentException("Expected a search term.");
            var token = tokens[position++];
            if (token == "NOT") { var operand = Atom(); return values => !operand(values); }
            if (token == "(")
            {
                var group = Or();
                if (position >= tokens.Length || tokens[position++] != ")") throw new ArgumentException("Missing closing parenthesis.");
                return group;
            }
            if (token is "AND" or "OR" or ")") throw new ArgumentException("Expected a search term.");
            var type = mode == "Boolean Regex" ? "Regex" : "Boolean";
            if (token == "REGEX")
            {
                type = "Regex";
                if (position >= tokens.Length) throw new ArgumentException("REGEX requires a pattern.");
                token = tokens[position++];
            }
            if (token is "LIKE" or "FILELIST" || token.StartsWith("NEAR") || token.StartsWith("LINES:"))
                throw new ArgumentException("Use AND, OR, NOT, REGEX, quoted phrases and parentheses in Boolean mode.");
            var quoted = token.StartsWith('"');
            var index = terms.Count;
            terms.Add(compile(quoted ? token[1..^1] : token, type, quoted));
            return values => values[index];
        }

        Func<bool[], bool> And()
        {
            var left = Atom();
            while (position < tokens.Length && tokens[position] is not ("OR" or ")"))
            {
                if (tokens[position] == "AND") position++;
                var previous = left;
                var right = Atom();
                left = values => previous(values) && right(values);
            }
            return left;
        }

        Func<bool[], bool> Or()
        {
            var left = And();
            while (position < tokens.Length && tokens[position] == "OR")
            {
                position++;
                var previous = left;
                var right = And();
                left = values => previous(values) || right(values);
            }
            return left;
        }

        evaluate = Or();
        if (position != tokens.Length) throw new ArgumentException("Unexpected closing parenthesis.");
    }

    public bool[] CreateState() => new bool[terms.Count];

    /// <summary>Creates a regex with the matching rules shared by search and the editor.</summary>
    internal static Regex CreateRegex(string pattern, bool matchCase, bool wholeWord, bool compile = true)
    {
        if (wholeWord) pattern = "(?<!\\w)(?:" + pattern + ")(?!\\w)";
        var options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        if (compile) options |= RegexOptions.Compiled;
        return new Regex(pattern, options, RegexTimeout);
    }

    public bool Feed(string line, bool[] state)
    {
        if (!lineByLine)
        {
            var any = false;
            for (var i = 0; i < terms.Count; i++)
            {
                var matched = terms[i](line);
                state[i] |= matched;
                any |= matched;
            }
            return any;
        }
        var current = new bool[terms.Count];
        for (var i = 0; i < terms.Count; i++) { current[i] = terms[i](line); state[i] |= current[i]; }
        return lineByLine ? evaluate(current) : current.Any(value => value);
    }

    public bool Accept(bool[] state, long matchingLines) => lineByLine ? matchingLines > 0 : evaluate(state);

    public bool IsMatch(string text)
    {
        var state = CreateState();
        Feed(text, state);
        return evaluate(state);
    }

    private static string WildcardPattern(string expression)
    {
        var patterns = expression.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join("|", patterns.Select(term =>
        {
            if (term is "*" or "*.*") return "^.*$";
            var explicitStart = term.StartsWith('<');
            var explicitEnd = term.EndsWith('>');
            term = term.TrimStart('<').TrimEnd('>');
            var wildcard = term.Contains('*') || term.Contains('?');
            return (wildcard || explicitStart ? "^" : "") + Regex.Escape(term).Replace("\\*", ".*").Replace("\\?", ".") +
                   (wildcard || explicitEnd || term.Contains('.') ? "$" : "");
        }));
    }
}
