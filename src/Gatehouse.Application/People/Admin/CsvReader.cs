using System.Text;

namespace Gatehouse.Application.People.Admin;

/// <summary>Minimal RFC 4180 reader (quoted fields, doubled quotes, CRLF/LF). Returns rows with their 1-based line numbers.</summary>
public static class CsvReader
{
    public static List<(int Line, List<string> Cells)> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<(int, List<string>)>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var rowLine = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    cell.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when cell.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    AddRow(rows, rowLine, cells);
                    cells = [];
                    line++;
                    rowLine = line;
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            AddRow(rows, rowLine, cells);
        }

        return rows;
    }

    private static void AddRow(List<(int, List<string>)> rows, int line, List<string> cells)
    {
        if (cells.Count == 1 && cells[0].Length == 0)
        {
            return; // blank line
        }

        rows.Add((line, cells));
    }
}
