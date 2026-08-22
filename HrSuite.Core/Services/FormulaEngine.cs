using System.Globalization;
using System.Text.RegularExpressions;

namespace HrSuite.Core.Services;

/// <summary>
/// The evaluator for computed screen fields.
///
/// Grammar, deliberately tiny:
///   value      : number | {fieldKey}
///   operators  : + - * /  and unary -
///   comparison : &gt; &lt; &gt;= &lt;= == != &lt;&gt;   (yield 1 or 0, useful inside IF)
///   grouping   : ( )
///   functions  : MIN(a,b) MAX(a,b) ROUND(a,n) IF(condition,a,b)
///
/// A hand-written tokeniser and shunting-yard parser, not a script host: there is no eval
/// and no compilation, so nothing outside this grammar can execute. A formula is authored by
/// an administrator and stored as data, which makes it exactly the kind of input that must
/// not be handed to an interpreter.
///
/// The browser carries the same grammar so a user watches the number appear as they type.
/// This side runs again on every save and its answer is the one stored — the client copy is
/// for immediacy, never for truth.
///
/// A missing reference counts as zero and is reported rather than failing the whole
/// evaluation, so a half-filled form still shows a number instead of an error.
/// </summary>
public static class FormulaEngine
{
    private static readonly Regex RefPattern = new(@"\{\s*([A-Za-z_][A-Za-z0-9_]{0,79})\s*\}", RegexOptions.Compiled);

    private static readonly Regex KeyShape = new("^[A-Za-z_][A-Za-z0-9_]{0,79}$", RegexOptions.Compiled);

    private static readonly Dictionary<string, int> Functions = new(StringComparer.Ordinal)
    {
        ["MIN"] = 2,
        ["MAX"] = 2,
        ["ROUND"] = 2,
        ["IF"] = 3
    };

    private static readonly Dictionary<string, int> Precedence = new(StringComparer.Ordinal)
    {
        ["u-"] = 4,
        ["*"] = 3,
        ["/"] = 3,
        ["+"] = 2,
        ["-"] = 2,
        [">"] = 1,
        ["<"] = 1,
        [">="] = 1,
        ["<="] = 1,
        ["=="] = 1,
        ["!="] = 1
    };

    public sealed record Result(bool Ok, decimal Value, string? Error, IReadOnlyList<string> Refs, IReadOnlyList<string> Missing);

    /// <summary>The field keys a formula reads, in first-seen order and without duplicates.</summary>
    public static IReadOnlyList<string> ExtractRefs(string? formula)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(formula)) return found;

        foreach (Match m in RefPattern.Matches(formula))
        {
            var key = m.Groups[1].Value;
            if (!found.Any(r => string.Equals(r, key, StringComparison.OrdinalIgnoreCase))) found.Add(key);
        }

        return found;
    }

    /// <summary>
    /// Parses without evaluating. Used to reject a formula at save time, so a broken one is
    /// never stored to fail later on somebody else's record.
    /// </summary>
    public static Result Validate(string? formula)
        => Evaluate(formula, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), null);

    /// <summary>
    /// Runs a formula against a form's values.
    /// </summary>
    /// <param name="formula">the authored text</param>
    /// <param name="values">form values by field key; lookup is case-insensitive</param>
    /// <param name="roundTo">decimal places for the result, or null to leave it alone</param>
    public static Result Evaluate(string? formula, IReadOnlyDictionary<string, string?> values, int? roundTo)
    {
        var refs = ExtractRefs(formula);

        if (string.IsNullOrWhiteSpace(formula))
            return new Result(false, 0m, "This field has no formula.", refs, Array.Empty<string>());

        var (tokens, tokenError) = Tokenise(formula);
        if (tokenError is not null) return new Result(false, 0m, tokenError, refs, Array.Empty<string>());

        var (rpn, parseError) = ToRpn(tokens!);
        if (parseError is not null) return new Result(false, 0m, parseError, refs, Array.Empty<string>());

        var stack = new Stack<decimal>();
        var missing = new List<string>();

        foreach (var token in rpn!)
        {
            switch (token.Type)
            {
                case TokenType.Number:
                    stack.Push(token.Number);
                    break;

                case TokenType.Ref:
                {
                    var number = ToNumber(Lookup(values, token.Text));
                    if (number is null)
                    {
                        missing.Add(token.Text);
                        stack.Push(0m);
                    }
                    else
                    {
                        stack.Push(number.Value);
                    }
                    break;
                }

                case TokenType.Operator:
                {
                    if (token.Text == "u-")
                    {
                        if (stack.Count < 1) return Incomplete(refs);
                        stack.Push(-stack.Pop());
                        break;
                    }

                    if (stack.Count < 2) return Incomplete(refs);
                    var right = stack.Pop();
                    var left = stack.Pop();

                    switch (token.Text)
                    {
                        case "+": stack.Push(left + right); break;
                        case "-": stack.Push(left - right); break;
                        case "*": stack.Push(left * right); break;
                        case "/":
                            if (right == 0m) return new Result(false, 0m, "Division by zero.", refs, missing);
                            stack.Push(left / right);
                            break;
                        case ">": stack.Push(left > right ? 1m : 0m); break;
                        case "<": stack.Push(left < right ? 1m : 0m); break;
                        case ">=": stack.Push(left >= right ? 1m : 0m); break;
                        case "<=": stack.Push(left <= right ? 1m : 0m); break;
                        case "==": stack.Push(left == right ? 1m : 0m); break;
                        case "!=": stack.Push(left != right ? 1m : 0m); break;
                        default: return new Result(false, 0m, $"Unsupported operator '{token.Text}'.", refs, missing);
                    }
                    break;
                }

                case TokenType.Function:
                {
                    if (stack.Count < token.Args)
                        return new Result(false, 0m, $"{token.Text} is missing arguments.", refs, missing);

                    var args = new decimal[token.Args];
                    for (var i = token.Args - 1; i >= 0; i--) args[i] = stack.Pop();

                    switch (token.Text)
                    {
                        case "MIN": stack.Push(Math.Min(args[0], args[1])); break;
                        case "MAX": stack.Push(Math.Max(args[0], args[1])); break;
                        case "ROUND": stack.Push(Round(args[0], (int)args[1])); break;
                        case "IF": stack.Push(args[0] != 0m ? args[1] : args[2]); break;
                        default: return new Result(false, 0m, $"Unknown function '{token.Text}'.", refs, missing);
                    }
                    break;
                }

                default:
                    return new Result(false, 0m, "The formula could not be read.", refs, missing);
            }
        }

        if (stack.Count != 1) return Incomplete(refs, missing);

        var value = roundTo is null ? stack.Pop() : Round(stack.Pop(), roundTo.Value);
        return new Result(true, value, null, refs, missing);

        static Result Incomplete(IReadOnlyList<string> refs, IReadOnlyList<string>? missing = null)
            => new(false, 0m, "The formula is incomplete.", refs, missing ?? Array.Empty<string>());
    }

    /// <summary>
    /// Orders computed fields so each is evaluated after the fields it reads.
    ///
    /// A field caught in a cycle comes out last rather than being dropped: the service
    /// refuses to store one, but configuration written by an earlier version might still
    /// contain it, and silently omitting the field would leave a form with a blank nobody
    /// could explain.
    /// </summary>
    public static IReadOnlyList<T> InEvaluationOrder<T>(
        IReadOnlyList<T> fields, Func<T, string> keyOf, Func<T, IEnumerable<string>> refsOf)
    {
        var byKey = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields) byKey[keyOf(field)] = field;

        var ordered = new List<T>(fields.Count);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(T field)
        {
            var key = keyOf(field);
            if (done.Contains(key) || visiting.Contains(key)) return;

            visiting.Add(key);
            foreach (var reference in refsOf(field))
            {
                if (byKey.TryGetValue(reference, out var dependency)) Walk(dependency);
            }
            visiting.Remove(key);

            done.Add(key);
            ordered.Add(field);
        }

        foreach (var field in fields) Walk(field);
        return ordered;
    }

    /// <summary>True when a formula reads its own key, directly or through another field.</summary>
    public static bool HasCycle(string fieldKey, string? formula, Func<string, string?> formulaOf)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Walk(string? text, string owner)
        {
            foreach (var reference in ExtractRefs(text))
            {
                if (string.Equals(reference, fieldKey, StringComparison.OrdinalIgnoreCase)) return true;
                if (!seen.Add(reference)) continue;
                if (Walk(formulaOf(reference), reference)) return true;
            }
            return false;
        }

        return Walk(formula, fieldKey);
    }

    /// <summary>Null, not zero, when there is nothing numeric to read — empty and zero differ.</summary>
    public static decimal? ToNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim();
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return 1m;
        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return 0m;

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string?> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Half away from zero, matching what the browser copy does.</summary>
    private static decimal Round(decimal value, int scale)
    {
        if (scale < 0 || scale > 6) return value;
        return Math.Round(value, scale, MidpointRounding.AwayFromZero);
    }

    // -----------------------------------------------------------------
    // Tokeniser
    // -----------------------------------------------------------------

    private enum TokenType { Number, Ref, Operator, Function, LParen, RParen, Comma }

    private sealed class Token
    {
        public TokenType Type { get; init; }
        public string Text { get; init; } = string.Empty;
        public decimal Number { get; init; }
        public int Args { get; set; }
    }

    private static (List<Token>? Tokens, string? Error) Tokenise(string expression)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < expression.Length)
        {
            var c = expression[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '{')
            {
                var end = expression.IndexOf('}', i);
                if (end < 0) return (null, "A field reference is missing its closing brace.");

                var key = expression[(i + 1)..end].Trim();
                if (!KeyShape.IsMatch(key)) return (null, $"'{key}' is not a field key.");

                tokens.Add(new Token { Type = TokenType.Ref, Text = key });
                i = end + 1;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < expression.Length && char.IsDigit(expression[i + 1])))
            {
                var j = i;
                while (j < expression.Length && (char.IsDigit(expression[j]) || expression[j] == '.')) j++;

                var text = expression[i..j];
                if (text.Count(ch => ch == '.') > 1) return (null, $"'{text}' is not a number.");

                tokens.Add(new Token
                {
                    Type = TokenType.Number,
                    Number = decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture)
                });
                i = j;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var j = i;
                while (j < expression.Length && (char.IsLetterOrDigit(expression[j]) || expression[j] == '_')) j++;

                var word = expression[i..j].ToUpperInvariant();
                if (!Functions.ContainsKey(word))
                    return (null, $"'{expression[i..j]}' is not a function. Reference a field as {{fieldKey}}.");

                tokens.Add(new Token { Type = TokenType.Function, Text = word });
                i = j;
                continue;
            }

            if (i + 1 < expression.Length)
            {
                var two = expression.Substring(i, 2);
                if (two is ">=" or "<=" or "==" or "!=" or "<>")
                {
                    tokens.Add(new Token { Type = TokenType.Operator, Text = two == "<>" ? "!=" : two });
                    i += 2;
                    continue;
                }
            }

            if ("+-*/><".Contains(c)) { tokens.Add(new Token { Type = TokenType.Operator, Text = c.ToString() }); i++; continue; }
            if (c == '(') { tokens.Add(new Token { Type = TokenType.LParen }); i++; continue; }
            if (c == ')') { tokens.Add(new Token { Type = TokenType.RParen }); i++; continue; }
            if (c == ',') { tokens.Add(new Token { Type = TokenType.Comma }); i++; continue; }
            if (c == '=') return (null, "Use '==' to compare two values.");

            return (null, $"'{c}' cannot be used in a formula.");
        }

        return (tokens, null);
    }

    // -----------------------------------------------------------------
    // Shunting-yard
    // -----------------------------------------------------------------

    private static (List<Token>? Rpn, string? Error) ToRpn(List<Token> tokens)
    {
        var output = new List<Token>();
        var stack = new Stack<Token>();
        var argCount = new Stack<int>();
        Token? previous = null;

        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case TokenType.Number:
                case TokenType.Ref:
                    output.Add(token);
                    break;

                case TokenType.Function:
                    stack.Push(token);
                    argCount.Push(1);
                    break;

                case TokenType.Comma:
                {
                    while (stack.Count > 0 && stack.Peek().Type != TokenType.LParen) output.Add(stack.Pop());
                    if (stack.Count == 0) return (null, "Misplaced comma — check the brackets.");
                    if (argCount.Count > 0) argCount.Push(argCount.Pop() + 1);
                    break;
                }

                case TokenType.Operator:
                {
                    // A '-' at the start, or straight after another operator, an open bracket
                    // or a comma, is a sign rather than a subtraction.
                    var unary = token.Text == "-" &&
                                (previous is null ||
                                 previous.Type == TokenType.Operator ||
                                 previous.Type == TokenType.LParen ||
                                 previous.Type == TokenType.Comma);

                    var op = unary ? "u-" : token.Text;

                    while (stack.Count > 0)
                    {
                        var top = stack.Peek();
                        if (top.Type != TokenType.Operator) break;

                        var higher = Precedence[top.Text] > Precedence[op];
                        var equal = Precedence[top.Text] == Precedence[op] && op != "u-";
                        if (!higher && !equal) break;

                        output.Add(stack.Pop());
                    }

                    stack.Push(new Token { Type = TokenType.Operator, Text = op });
                    break;
                }

                case TokenType.LParen:
                    stack.Push(token);
                    break;

                case TokenType.RParen:
                {
                    while (stack.Count > 0 && stack.Peek().Type != TokenType.LParen) output.Add(stack.Pop());
                    if (stack.Count == 0) return (null, "A closing bracket has no opening bracket.");
                    stack.Pop();

                    if (stack.Count > 0 && stack.Peek().Type == TokenType.Function)
                    {
                        var fn = stack.Pop();
                        fn.Args = argCount.Count > 0 ? argCount.Pop() : 0;

                        if (fn.Args != Functions[fn.Text])
                            return (null, $"{fn.Text} takes {Functions[fn.Text]} arguments, not {fn.Args}.");

                        output.Add(fn);
                    }
                    break;
                }

                default:
                    return (null, "The formula could not be read.");
            }

            previous = token;
        }

        while (stack.Count > 0)
        {
            var top = stack.Pop();
            if (top.Type == TokenType.LParen) return (null, "An opening bracket was never closed.");
            output.Add(top);
        }

        return (output, null);
    }
}
