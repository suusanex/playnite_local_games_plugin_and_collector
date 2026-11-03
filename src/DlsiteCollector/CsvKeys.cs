using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DlsiteCollector;

internal sealed class CsvTable
{
    public CsvTable(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Header = header;
        Rows = rows;
    }

    public IReadOnlyList<string> Header { get; }

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    public static CsvTable Read(string path)
    {
        string text;
        using (var stream = File.OpenRead(path))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        var rows = Parse(text);
        if (rows.Count == 0 || rows[0].Count == 0)
        {
            throw new CollectorInputException("CSV header is missing.");
        }

        return new CsvTable(rows[0], rows.Skip(1).ToList());
    }

    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
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

            if (ch == '"')
            {
                inQuotes = true;
                continue;
            }

            if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (ch == '\r')
            {
                continue;
            }

            if (ch == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                if (row.Count != 1 || row[0].Length != 0)
                {
                    rows.Add(row);
                }

                row = new List<string>();
                continue;
            }

            field.Append(ch);
        }

        if (inQuotes)
        {
            throw new CollectorInputException("CSV has an unfinished quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}

internal sealed class CollectorInputException : Exception
{
    public CollectorInputException(string message)
        : base(message)
    {
    }
}
