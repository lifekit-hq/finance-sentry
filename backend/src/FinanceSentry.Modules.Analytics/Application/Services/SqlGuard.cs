namespace FinanceSentry.Modules.Analytics.Application.Services;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Single-<c>SELECT</c> validator (FR-005). Strategy: strip comments and string/identifier literals so
/// keywords inside them can't trigger false positives or hide an injection, then require the statement
/// to (a) be exactly one statement, (b) start with <c>SELECT</c> or <c>WITH</c>, (c) contain no
/// write/DDL/transaction-control keyword anywhere (which also blocks data-modifying CTEs and
/// <c>SELECT … FOR UPDATE</c>), and (d) call only allowlisted functions — so the query can't reach
/// <c>set_config</c>/<c>current_setting</c> (the owner-scope setting) or any <c>pg_*</c> admin function.
/// This is the second layer; the read-only role is the first.
/// </summary>
public sealed partial class SqlGuard : ISqlGuard
{
    private const string RejectReason =
        "only a single read-only SELECT over the curated analytics views is allowed";

    // Anything that writes, changes schema, controls transactions, or otherwise isn't a pure read.
    private static readonly IReadOnlySet<string> ForbiddenKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "UPSERT",
        "DROP", "ALTER", "CREATE", "TRUNCATE", "RENAME", "COMMENT",
        "GRANT", "REVOKE", "COPY", "IMPORT",
        "CALL", "DO", "EXECUTE", "PREPARE", "DEALLOCATE",
        "VACUUM", "ANALYZE", "REINDEX", "REFRESH", "CLUSTER", "LOCK",
        "SET", "RESET", "SHOW",
        "BEGIN", "COMMIT", "ROLLBACK", "SAVEPOINT", "START",
        "LISTEN", "NOTIFY", "UNLISTEN", "DECLARE", "FETCH", "MOVE",
    };

    // The only functions a query may call: pure aggregate, window, conditional, math, date/time, text
    // and array helpers. Anything else — set_config, current_setting, pg_* admin/file/sleep functions,
    // dblink, lo_* — is rejected by omission.
    private static readonly IReadOnlySet<string> AllowedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Aggregates.
        "count", "sum", "avg", "min", "max", "every", "bool_and", "bool_or",
        "stddev", "stddev_pop", "stddev_samp", "variance", "var_pop", "var_samp",
        "string_agg", "array_agg", "percentile_cont", "percentile_disc", "mode",
        "corr", "covar_pop", "covar_samp", "regr_slope", "regr_intercept", "regr_r2",
        // Window functions.
        "row_number", "rank", "dense_rank", "percent_rank", "cume_dist", "ntile",
        "lag", "lead", "first_value", "last_value", "nth_value",
        // Conditional expressions and special-syntax forms.
        "coalesce", "nullif", "greatest", "least", "cast", "extract", "overlay", "position", "substring", "trim",
        // Math.
        "abs", "round", "ceil", "ceiling", "floor", "trunc", "sqrt", "cbrt", "power", "pow",
        "exp", "ln", "log", "log10", "sign", "mod", "div", "width_bucket",
        // Date/time.
        "now", "date_trunc", "date_part", "date_bin", "age", "make_date", "make_interval", "make_timestamp",
        "to_char", "to_date", "to_timestamp", "to_number", "justify_days", "justify_hours", "justify_interval",
        "isfinite",
        // Text.
        "lower", "upper", "initcap", "length", "char_length", "character_length", "substr", "strpos",
        "btrim", "ltrim", "rtrim", "lpad", "rpad", "left", "right", "concat", "concat_ws", "replace",
        "split_part", "reverse", "starts_with", "translate", "regexp_replace", "regexp_match", "format",
        // Arrays and set-returning helpers.
        "array_length", "cardinality", "array_to_string", "unnest", "generate_series",
    };

    // SQL keywords that may be followed by "(" without being a function call.
    private static readonly IReadOnlySet<string> ParenthesizedSyntaxKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "EXISTS", "ANY", "ALL", "SOME",
        "AS", "ON", "USING", "OVER", "FILTER", "GROUP", "BY", "JOIN", "LATERAL",
        "CASE", "WHEN", "THEN", "ELSE", "UNION", "INTERSECT", "EXCEPT", "DISTINCT",
        "HAVING", "LIMIT", "OFFSET", "BETWEEN", "SYMMETRIC", "LIKE", "ILIKE", "IS",
        "ROW", "ARRAY", "VALUES", "GROUPING", "SETS", "CUBE", "ROLLUP", "MATERIALIZED",
    };

    public SqlGuardResult Validate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlGuardResult.Invalid(RejectReason);
        }

        var sanitized = StripLiteralsAndComments(sql).Trim();

        // Allow a single trailing semicolon; reject any interior one (statement chaining).
        sanitized = sanitized.TrimEnd(';', ' ', '\t', '\r', '\n');
        if (sanitized.Contains(';'))
        {
            return SqlGuardResult.Invalid(RejectReason);
        }

        if (sanitized.Length == 0)
        {
            return SqlGuardResult.Invalid(RejectReason);
        }

        // First token must be SELECT or WITH.
        var firstToken = FirstWordRegex().Match(sanitized).Value;
        if (!firstToken.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
            && !firstToken.Equals("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return SqlGuardResult.Invalid(RejectReason);
        }

        foreach (Match word in WordRegex().Matches(sanitized))
        {
            if (ForbiddenKeywords.Contains(word.Value))
            {
                return SqlGuardResult.Invalid(RejectReason);
            }
        }

        var disallowedFunction = FindDisallowedFunctionCall(sanitized);
        if (disallowedFunction is not null)
        {
            return SqlGuardResult.Invalid(
                $"function '{disallowedFunction}' is not allowed — use standard aggregate, window, math, "
                + "date and text functions only");
        }

        return SqlGuardResult.Valid;
    }

    /// <summary>
    /// Returns the name of the first function call that is not allowlisted, or <c>null</c>. A call is
    /// any identifier followed by <c>(</c>, except a syntax keyword, a type modifier or alias column
    /// list (<c>::numeric(10,2)</c>, <c>AS t(a, b)</c>) and a first CTE's column list
    /// (<c>WITH t(a) AS</c>). Schema-qualified calls are always rejected, so an allowlisted name can't
    /// resolve to a same-named function elsewhere. Unrecognised shapes (including
    /// a double-quoted function name, which sanitizes to a placeholder) fail closed.
    /// </summary>
    private static string? FindDisallowedFunctionCall(string sanitized)
    {
        var tokens = TokenRegex().Matches(sanitized).Select(m => m.Value).ToList();
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            var name = tokens[i];
            if (tokens[i + 1] != "(" || !IdentifierRegex().IsMatch(name))
            {
                continue;
            }

            var previous = i > 0 ? tokens[i - 1] : null;
            if (ParenthesizedSyntaxKeywords.Contains(name))
            {
                continue;
            }

            if (previous is not null
                && (previous == "::"
                    || previous.Equals("AS", StringComparison.OrdinalIgnoreCase)
                    || previous.Equals("WITH", StringComparison.OrdinalIgnoreCase)
                    || previous.Equals("RECURSIVE", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (previous == "." || !AllowedFunctions.Contains(name))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// Replaces comments (including nested <c>/* */</c>) and the contents of string literals
    /// (<c>'…'</c>, <c>E'…'</c> with backslash escapes, <c>$tag$…$tag$</c>) and double-quoted
    /// identifiers with spaces/placeholders, so downstream scanning sees only bare SQL — lexed the way
    /// PostgreSQL lexes it, so no literal can hide SQL from the scan.
    /// </summary>
    private static string StripLiteralsAndComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];

            // Line comment.
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n' && sql[i] != '\r')
                {
                    i++;
                }
                continue;
            }

            // Block comment — PostgreSQL block comments nest.
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var depth = 1;
                i += 2;
                while (i < sql.Length && depth > 0)
                {
                    if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }
                sb.Append(' ');
                continue;
            }

            // Dollar-quoted string literal ($$…$$ or $tag$…$tag$).
            if (c == '$' && (i == 0 || !IsIdentifierChar(sql[i - 1])))
            {
                var tag = DollarQuoteTagRegex().Match(sql, i);
                if (tag.Success && tag.Index == i)
                {
                    var close = sql.IndexOf(tag.Value, i + tag.Length, StringComparison.Ordinal);
                    i = close < 0 ? sql.Length : close + tag.Length;
                    sb.Append(" '' ");
                    continue;
                }
            }

            // Single-quoted string literal (handles '' escape, and \ escapes in E'…' strings).
            if (c == '\'')
            {
                var backslashEscapes = i > 0
                    && (sql[i - 1] == 'E' || sql[i - 1] == 'e')
                    && (i < 2 || !IsIdentifierChar(sql[i - 2]));
                i++;
                while (i < sql.Length)
                {
                    if (backslashEscapes && sql[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '\'')
                    {
                        i++;
                        break;
                    }
                    i++;
                }
                sb.Append(" '' ");
                continue;
            }

            // Double-quoted identifier (handles "" escape).
            if (c == '"')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '"' && i + 1 < sql.Length && sql[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '"')
                    {
                        i++;
                        break;
                    }
                    i++;
                }
                sb.Append(" id ");
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex FirstWordRegex();

    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_]*\b")]
    private static partial Regex WordRegex();

    // PostgreSQL identifiers: a letter (any script) or underscore, then letters, digits, _ or $.
    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{N}_$]*$")]
    private static partial Regex IdentifierRegex();

    // Identifiers, bare numbers, the :: cast operator, or any other single non-space character.
    [GeneratedRegex(@"[\p{L}_][\p{L}\p{N}_$]*|\p{N}+|::|\S")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\$([\p{L}_][\p{L}\p{N}_]*)?\$")]
    private static partial Regex DollarQuoteTagRegex();
}
