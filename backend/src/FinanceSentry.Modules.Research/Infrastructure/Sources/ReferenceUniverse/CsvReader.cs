namespace FinanceSentry.Modules.Research.Infrastructure.Sources.ReferenceUniverse;

using System.Text;

/// <summary>
/// Minimal streaming RFC 4180 reader: quoted fields, doubled quotes, newlines inside quotes, a leading
/// byte-order mark. The FinanceDatabase company summaries carry commas, quotes and line breaks, so a
/// naive split is not an option; no CSV library is in the solution.
/// </summary>
public static class CsvReader
{
    private const char Quote = '"';

    public static IEnumerable<List<string>> ReadRows(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var first = true;
        var any = false;

        int c;
        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;
            if (first)
            {
                first = false;
                if (ch == '﻿')
                {
                    continue;
                }
            }

            if (inQuotes)
            {
                if (ch == Quote)
                {
                    if (reader.Peek() == Quote)
                    {
                        reader.Read();
                        field.Append(Quote);
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case Quote when field.Length == 0:
                    inQuotes = true;
                    any = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    any = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (any || field.Length > 0)
                    {
                        row.Add(field.ToString());
                        yield return row;
                    }

                    row = [];
                    field.Clear();
                    any = false;
                    break;
                default:
                    field.Append(ch);
                    any = true;
                    break;
            }
        }

        if (any || field.Length > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }

    /// <summary>
    /// Reads the header row and maps each requested column name (case-insensitive) to its index. Throws
    /// <see cref="Application.Services.ReferenceUniverse.ReferenceFeedException"/> naming the missing column.
    /// </summary>
    public static Dictionary<string, int> MapHeader(IReadOnlyList<string> header, params string[] required)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            map.TryAdd(header[i].Trim(), i);
        }

        foreach (var name in required)
        {
            if (!map.ContainsKey(name))
            {
                throw new Application.Services.ReferenceUniverse.ReferenceFeedException(
                    $"expected column '{name}' is missing - the upstream layout may have changed");
            }
        }

        return map;
    }

    public static string Cell(IReadOnlyList<string> row, Dictionary<string, int> map, string column) =>
        map.TryGetValue(column, out var i) && i < row.Count ? row[i].Trim() : string.Empty;
}
