using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SolutionComponentComparer.Core
{
    /// <summary>Cell styles available in <see cref="XlsxWriter"/>.</summary>
    public enum XlsxStyle
    {
        Default = 0,
        Header = 1,
        Red = 2,
        Blue = 3,
        Amber = 4,
        Green = 5,
        Bold = 6
    }

    public class XlsxSheet
    {
        public XlsxSheet(string name)
        {
            Name = name;
            Headers = new List<string>();
            Rows = new List<object[]>();
            RowStyles = new List<XlsxStyle>();
            ColumnWidths = new List<double>();
        }

        public string Name { get; set; }
        public List<string> Headers { get; private set; }
        public List<object[]> Rows { get; private set; }
        public List<XlsxStyle> RowStyles { get; private set; }
        public List<double> ColumnWidths { get; private set; }
        public bool AutoFilter { get; set; }

        public void AddRow(XlsxStyle style, params object[] values)
        {
            Rows.Add(values);
            RowStyles.Add(style);
        }
    }

    /// <summary>
    /// Minimal dependency-free .xlsx writer (inline strings, a few fills, frozen header, autofilter).
    /// Keeps the plugin a single DLL, so it can be dropped into the XrmToolBox Plugins folder.
    /// </summary>
    public static class XlsxWriter
    {
        public static void Write(string path, IList<XlsxSheet> sheets)
        {
            // Write to a temporary file first, so a failure never leaves a broken workbook behind.
            var tempPath = path + ".tmp";
            try
            {
                WriteCore(tempPath, sheets);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private static void WriteCore(string path, IList<XlsxSheet> sheets)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var stream = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                AddEntry(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
                AddEntry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                AddEntry(zip, "xl/workbook.xml", Workbook(sheets));
                AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
                AddEntry(zip, "xl/styles.xml", Styles());
                for (int i = 0; i < sheets.Count; i++)
                {
                    AddEntry(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", Sheet(sheets[i]));
                }
            }
        }

        private static void AddEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static string ContentTypes(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 1; i <= sheetCount; i++)
            {
                sb.Append("<Override PartName=\"/xl/worksheets/sheet" + i + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string Workbook(IList<XlsxSheet> sheets)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<sheets>");
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();
            for (int i = 0; i < sheets.Count; i++)
            {
                var name = SafeSheetName(sheets[i].Name, i, usedNames);
                names.Add(name);
                sb.Append("<sheet name=\"" + Esc(name) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>");
            }
            sb.Append("</sheets>");

            var defined = new StringBuilder();
            for (int i = 0; i < sheets.Count; i++)
            {
                var s = sheets[i];
                if (!s.AutoFilter || s.Headers.Count == 0) continue;
                defined.Append("<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"" + i + "\" hidden=\"1\">'" +
                    Esc(names[i].Replace("'", "''")) + "'!$A$1:$" + ColumnName(s.Headers.Count - 1) + "$" + (s.Rows.Count + 1) + "</definedName>");
            }
            if (defined.Length > 0) sb.Append("<definedNames>").Append(defined.ToString()).Append("</definedNames>");
            sb.Append("</workbook>");
            return sb.ToString();
        }

        private static string WorkbookRels(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 1; i <= sheetCount; i++)
            {
                sb.Append("<Relationship Id=\"rId" + i + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + i + ".xml\"/>");
            }
            sb.Append("<Relationship Id=\"rId" + (sheetCount + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string Styles()
        {
            // fonts: 0 normal, 1 bold
            // fills: 0 none, 1 gray125 (both required), 2 header gray, 3 red, 4 blue, 5 amber, 6 green
            // cellXfs index == XlsxStyle value
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"2\">" +
                "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
                "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
                "</fonts>" +
                "<fills count=\"7\">" +
                "<fill><patternFill patternType=\"none\"/></fill>" +
                "<fill><patternFill patternType=\"gray125\"/></fill>" +
                Fill("FFD9D9D9") + Fill("FFF8D7DA") + Fill("FFD6E9FF") + Fill("FFFFE8B3") + Fill("FFE2F0D9") +
                "</fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"7\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"4\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"5\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"6\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                "</cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                "</styleSheet>";
        }

        private static string Fill(string argb)
        {
            return "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"" + argb + "\"/><bgColor indexed=\"64\"/></patternFill></fill>";
        }

        private static string Sheet(XlsxSheet sheet)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");

            if (sheet.Headers.Count > 0)
            {
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            }

            if (sheet.ColumnWidths.Count > 0)
            {
                sb.Append("<cols>");
                for (int i = 0; i < sheet.ColumnWidths.Count; i++)
                {
                    sb.Append("<col min=\"" + (i + 1) + "\" max=\"" + (i + 1) + "\" width=\"" +
                        sheet.ColumnWidths[i].ToString("0.##", CultureInfo.InvariantCulture) + "\" customWidth=\"1\"/>");
                }
                sb.Append("</cols>");
            }

            sb.Append("<sheetData>");
            int rowIndex = 1;
            if (sheet.Headers.Count > 0)
            {
                sb.Append("<row r=\"1\">");
                for (int c = 0; c < sheet.Headers.Count; c++)
                {
                    AppendCell(sb, c, rowIndex, sheet.Headers[c], XlsxStyle.Header);
                }
                sb.Append("</row>");
                rowIndex++;
            }

            for (int r = 0; r < sheet.Rows.Count; r++, rowIndex++)
            {
                var values = sheet.Rows[r];
                var style = r < sheet.RowStyles.Count ? sheet.RowStyles[r] : XlsxStyle.Default;
                sb.Append("<row r=\"" + rowIndex + "\">");
                for (int c = 0; c < values.Length; c++)
                {
                    AppendCell(sb, c, rowIndex, values[c], style);
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");

            if (sheet.AutoFilter && sheet.Headers.Count > 0)
            {
                sb.Append("<autoFilter ref=\"A1:" + ColumnName(sheet.Headers.Count - 1) + (sheet.Rows.Count + 1) + "\"/>");
            }

            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static void AppendCell(StringBuilder sb, int col, int row, object value, XlsxStyle style)
        {
            var reference = ColumnName(col) + row;
            var s = (int)style;
            if (value == null)
            {
                if (s != 0) sb.Append("<c r=\"" + reference + "\" s=\"" + s + "\"/>");
                return;
            }

            if (value is int || value is long || value is double || value is decimal || value is float)
            {
                sb.Append("<c r=\"" + reference + "\" s=\"" + s + "\"><v>" +
                    Convert.ToString(value, CultureInfo.InvariantCulture) + "</v></c>");
                return;
            }

            string text;
            if (value is DateTime) text = ((DateTime)value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            else if (value is bool) text = (bool)value ? "Yes" : "No";
            else text = Convert.ToString(value, CultureInfo.InvariantCulture);

            if (text.Length > 32000) text = text.Substring(0, 32000) + "...";
            sb.Append("<c r=\"" + reference + "\" s=\"" + s + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + Esc(text) + "</t></is></c>");
        }

        public static string ColumnName(int index)
        {
            var name = string.Empty;
            index++;
            while (index > 0)
            {
                int mod = (index - 1) % 26;
                name = (char)('A' + mod) + name;
                index = (index - mod - 1) / 26;
            }
            return name;
        }

        private static string SafeSheetName(string name, int index, HashSet<string> used)
        {
            var sb = new StringBuilder();
            foreach (var ch in name ?? string.Empty)
            {
                sb.Append("[]:*?/\\".IndexOf(ch) >= 0 ? '_' : ch);
            }
            var result = sb.ToString().Trim();
            if (result.Length == 0) result = "Sheet" + (index + 1);
            if (result.Length > 31) result = result.Substring(0, 31);
            var candidate = result;
            int n = 2;
            while (!used.Add(candidate))
            {
                var suffix = " (" + n++ + ")";
                candidate = (result.Length + suffix.Length > 31 ? result.Substring(0, 31 - suffix.Length) : result) + suffix;
            }
            return candidate;
        }

        private static string Esc(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default:
                        // Drop characters that are not valid in XML 1.0.
                        if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD) || char.IsSurrogate(ch))
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
