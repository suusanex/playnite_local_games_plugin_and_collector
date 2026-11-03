using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LocalGameCatalog.Core
{
    public static class CatalogJsonl
    {
        public static IReadOnlyList<CatalogGame> Read(string path)
        {
            var games = new List<CatalogGame>();
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                CatalogGame game;
                try
                {
                    game = CatalogJson.ReadGame(line);
                    CatalogValidation.EnsureValid(game);
                }
                catch (Exception exception) when (exception is CatalogValidationException || exception is InvalidDataException || exception is FormatException)
                {
                    throw new InvalidDataException("Invalid catalog line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": " + exception.Message, exception);
                }

                games.Add(game);
            }

            CatalogValidation.EnsureUniqueIds(games);
            return games;
        }

        public static void WriteAtomic(string path, IReadOnlyList<CatalogGame> games)
        {
            CatalogValidation.EnsureUniqueIds(games);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidDataException("Output directory could not be resolved.");
            }

            Directory.CreateDirectory(directory);
            var temporary = path + ".tmp";
            var builder = new StringBuilder();
            foreach (var game in games)
            {
                builder.Append(CatalogJson.WriteGame(game));
                builder.Append('\n');
            }

            File.WriteAllText(temporary, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
    }

    public static class CatalogMerge
    {
        public static IReadOnlyList<CatalogGame> PreserveFailures(
            IReadOnlyList<CatalogGame> previous,
            IReadOnlyDictionary<string, CatalogGame> succeededById,
            IReadOnlyList<string> requestedIds,
            ISet<string> failedIds)
        {
            var previousById = new Dictionary<string, CatalogGame>(StringComparer.Ordinal);
            foreach (var game in previous)
            {
                previousById[game.Id] = game;
            }

            var merged = new List<CatalogGame>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in requestedIds)
            {
                if (!seen.Add(id))
                {
                    continue;
                }

                if (succeededById.TryGetValue(id, out var succeeded))
                {
                    merged.Add(succeeded);
                    continue;
                }

                if (failedIds.Contains(id) && previousById.TryGetValue(id, out var kept))
                {
                    merged.Add(kept);
                }
            }

            CatalogValidation.EnsureUniqueIds(merged);
            return merged;
        }
    }

    internal static class CatalogJson
    {
        public static string WriteGame(CatalogGame game)
        {
            var builder = new StringBuilder();
            builder.Append('{');
            AppendProperty(builder, "schemaVersion", game.SchemaVersion.ToString(CultureInfo.InvariantCulture), first: true, quote: false);
            AppendProperty(builder, "source", game.Source, first: false, quote: true);
            AppendProperty(builder, "sourceId", game.SourceId, first: false, quote: true);
            AppendProperty(builder, "id", game.Id, first: false, quote: true);
            AppendProperty(builder, "name", game.Name, first: false, quote: true);
            AppendProperty(builder, "productUrl", game.ProductUrl, first: false, quote: true);
            AppendProperty(builder, "coverImage", game.CoverImage ?? "", first: false, quote: true);
            if (!string.IsNullOrWhiteSpace(game.Developer))
            {
                AppendProperty(builder, "developer", game.Developer!, first: false, quote: true);
            }

            if (!string.IsNullOrWhiteSpace(game.ReleaseDate))
            {
                AppendProperty(builder, "releaseDate", game.ReleaseDate!, first: false, quote: true);
            }

            builder.Append('}');
            return builder.ToString();
        }

        public static CatalogGame ReadGame(string line)
        {
            var parser = new JsonObjectReader(line);
            var values = parser.ReadFlatObject();
            var game = new CatalogGame
            {
                SchemaVersion = RequireInt(values, "schemaVersion"),
                Source = RequireString(values, "source"),
                SourceId = RequireString(values, "sourceId"),
                Id = RequireString(values, "id"),
                Name = RequireString(values, "name"),
                ProductUrl = RequireString(values, "productUrl"),
                CoverImage = OptionalString(values, "coverImage") ?? "",
                Developer = OptionalString(values, "developer"),
                ReleaseDate = OptionalString(values, "releaseDate")
            };
            return game;
        }

        private static void AppendProperty(StringBuilder builder, string name, string value, bool first, bool quote)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(name).Append("\":");
            if (quote)
            {
                builder.Append(Quote(value));
            }
            else
            {
                builder.Append(value);
            }
        }

        private static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (ch < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(ch);
                        }

                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static string RequireString(IReadOnlyDictionary<string, string> values, string name)
        {
            if (!values.TryGetValue(name, out var value))
            {
                throw new InvalidDataException("Missing " + name + ".");
            }

            return value;
        }

        private static string? OptionalString(IReadOnlyDictionary<string, string> values, string name)
        {
            return values.TryGetValue(name, out var value) ? value : null;
        }

        private static int RequireInt(IReadOnlyDictionary<string, string> values, string name)
        {
            var text = RequireString(values, name);
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                throw new InvalidDataException(name + " must be an integer.");
            }

            return number;
        }
    }

    internal sealed class JsonObjectReader
    {
        private readonly string text;
        private int index;

        public JsonObjectReader(string text)
        {
            this.text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public Dictionary<string, string> ReadFlatObject()
        {
            SkipWhite();
            Expect('{');
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            SkipWhite();
            if (Peek() == '}')
            {
                index++;
                return values;
            }

            while (true)
            {
                SkipWhite();
                var name = ReadString();
                SkipWhite();
                Expect(':');
                SkipWhite();
                values[name] = ReadValue();
                SkipWhite();
                var next = Read();
                if (next == '}')
                {
                    break;
                }

                if (next != ',')
                {
                    throw new InvalidDataException("Expected a comma in catalog JSON.");
                }
            }

            SkipWhite();
            if (index != text.Length)
            {
                throw new InvalidDataException("Unexpected trailing data in catalog JSON.");
            }

            return values;
        }

        private string ReadValue()
        {
            var current = Peek();
            if (current == '"')
            {
                return ReadString();
            }

            var start = index;
            while (index < text.Length)
            {
                var ch = text[index];
                if (ch == ',' || ch == '}' || char.IsWhiteSpace(ch))
                {
                    break;
                }

                index++;
            }

            if (start == index)
            {
                throw new InvalidDataException("Missing JSON value.");
            }

            var raw = text.Substring(start, index - start);
            if (raw == "null")
            {
                return "";
            }

            return raw;
        }

        private string ReadString()
        {
            Expect('"');
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                var ch = Read();
                if (ch == '"')
                {
                    return builder.ToString();
                }

                if (ch != '\\')
                {
                    builder.Append(ch);
                    continue;
                }

                if (index >= text.Length)
                {
                    throw new InvalidDataException("Unfinished escape in catalog JSON.");
                }

                var escaped = Read();
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
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
                        builder.Append(ReadHex());
                        break;
                    default:
                        throw new InvalidDataException("Unsupported escape in catalog JSON.");
                }
            }

            throw new InvalidDataException("Unfinished string in catalog JSON.");
        }

        private char ReadHex()
        {
            if (index + 4 > text.Length)
            {
                throw new InvalidDataException("Unfinished unicode escape in catalog JSON.");
            }

            var hex = text.Substring(index, 4);
            index += 4;
            if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidDataException("Invalid unicode escape in catalog JSON.");
            }

            return (char)value;
        }

        private void SkipWhite()
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        private char Peek()
        {
            if (index >= text.Length)
            {
                throw new InvalidDataException("Unexpected end of catalog JSON.");
            }

            return text[index];
        }

        private char Read()
        {
            var ch = Peek();
            index++;
            return ch;
        }

        private void Expect(char expected)
        {
            var actual = Read();
            if (actual != expected)
            {
                throw new InvalidDataException("Expected '" + expected + "' in catalog JSON.");
            }
        }
    }
}
