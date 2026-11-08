using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace SolutionComponentComparer.Core
{
    /// <summary>Builds the Excel and HTML reports from a comparison result.</summary>
    public static class ReportBuilder
    {
        public static readonly ComparisonStatus[] StatusOrder =
        {
            ComparisonStatus.MissingInTarget,
            ComparisonStatus.BehaviorDiffers,
            ComparisonStatus.OnlyInTarget,
            ComparisonStatus.CoveredByTargetTable,
            ComparisonStatus.CoveredBySourceTable,
            ComparisonStatus.InBoth,
            ComparisonStatus.Listed
        };

        public static string StatusDescription(ComparisonStatus status)
        {
            switch (status)
            {
                case ComparisonStatus.MissingInTarget: return "In a source solution, in none of the target solutions. Check 'Also in other solutions': if empty, nothing else holds it.";
                case ComparisonStatus.BehaviorDiffers: return "In both, but the source carries more of it (e.g. table with all subcomponents vs. table without subcomponents).";
                case ComparisonStatus.OnlyInTarget: return "In a target solution, in none of the source solutions.";
                case ComparisonStatus.CoveredByTargetTable: return "Not listed in targets by itself, but its table is in a target with 'include all subcomponents'.";
                case ComparisonStatus.CoveredBySourceTable: return "Not listed in sources by itself, but its table is in a source with 'include all subcomponents'.";
                case ComparisonStatus.InBoth: return "In at least one source and at least one target.";
                case ComparisonStatus.Listed: return "No target selected: plain component list.";
                default: return string.Empty;
            }
        }

        public static XlsxStyle StyleFor(ComparisonStatus status)
        {
            switch (status)
            {
                case ComparisonStatus.MissingInTarget: return XlsxStyle.Red;
                case ComparisonStatus.BehaviorDiffers: return XlsxStyle.Amber;
                case ComparisonStatus.OnlyInTarget: return XlsxStyle.Blue;
                case ComparisonStatus.CoveredByTargetTable:
                case ComparisonStatus.CoveredBySourceTable: return XlsxStyle.Green;
                default: return XlsxStyle.Default;
            }
        }

        public static string HtmlColor(ComparisonStatus status)
        {
            switch (status)
            {
                case ComparisonStatus.MissingInTarget: return "#f8d7da";
                case ComparisonStatus.BehaviorDiffers: return "#ffe8b3";
                case ComparisonStatus.OnlyInTarget: return "#d6e9ff";
                case ComparisonStatus.CoveredByTargetTable:
                case ComparisonStatus.CoveredBySourceTable: return "#e2f0d9";
                default: return "#ffffff";
            }
        }

        // ------------------------------------------------------------------ Excel

        public static IList<XlsxSheet> BuildWorkbook(ComparisonResult result, IList<ComparisonRow> rows, IList<DuplicateRow> duplicates)
        {
            rows = rows ?? result.Rows;
            duplicates = duplicates ?? result.Duplicates;

            var summary = new XlsxSheet("Summary");
            summary.ColumnWidths.AddRange(new double[] { 32, 14, 110 });
            summary.AddRow(XlsxStyle.Bold, "Solution Component Comparer report");
            summary.AddRow(XlsxStyle.Default, "Environment", null, result.EnvironmentName ?? string.Empty);
            summary.AddRow(XlsxStyle.Default, "Generated", null, result.GeneratedOn);
            summary.AddRow(XlsxStyle.Default, "Source solutions", result.SourceSolutions.Count, string.Join(", ", result.SourceSolutions.Select(s => s.UniqueName)));
            summary.AddRow(XlsxStyle.Default, "Target solutions", result.TargetSolutions.Count, string.Join(", ", result.TargetSolutions.Select(s => s.UniqueName)));
            summary.AddRow(XlsxStyle.Default);
            summary.AddRow(XlsxStyle.Header, "Status", "Components", "Meaning");
            foreach (var status in StatusOrder)
            {
                int n = result.Count(status);
                if (n == 0 && status == ComparisonStatus.Listed) continue;
                summary.AddRow(StyleFor(status), ComparisonEngine.StatusText(status), n, StatusDescription(status));
            }
            summary.AddRow(XlsxStyle.Bold, "Total", result.Rows.Count, null);
            summary.AddRow(XlsxStyle.Default, "Components in 2+ selected solutions", result.Duplicates.Count, null);

            var missingByType = result.Rows
                .Where(r => r.Status == ComparisonStatus.MissingInTarget)
                .GroupBy(r => r.TypeName)
                .OrderByDescending(g => g.Count())
                .ToList();
            if (missingByType.Count > 0)
            {
                summary.AddRow(XlsxStyle.Default);
                summary.AddRow(XlsxStyle.Header, "Missing in target, by type", "Components", null);
                foreach (var g in missingByType) summary.AddRow(XlsxStyle.Red, g.Key, g.Count(), null);
            }

            var comparison = new XlsxSheet("Comparison") { AutoFilter = true };
            comparison.Headers.AddRange(new[]
            {
                "Status", "Component type", "Name", "Label", "Table", "Source #", "Source solutions",
                "Target #", "Target solutions", "Source behavior", "Target behavior", "Also in other solutions (not selected)", "Object id", "Type code"
            });
            comparison.ColumnWidths.AddRange(new double[] { 24, 24, 40, 28, 22, 9, 45, 9, 45, 20, 20, 40, 38, 10 });
            foreach (var r in rows)
            {
                comparison.AddRow(StyleFor(r.Status), r.StatusText, r.TypeName, r.Name, r.Label, r.ParentTable,
                    r.SourceCount, r.SourceSolutions, r.TargetCount, r.TargetSolutions, r.SourceBehavior, r.TargetBehavior,
                    r.OtherSolutions, r.ObjectId.ToString(), r.ComponentType);
            }

            var dups = new XlsxSheet("Duplicates") { AutoFilter = true };
            dups.Headers.AddRange(new[]
            {
                "Component type", "Name", "Label", "Table", "Solutions #", "In sources #", "In targets #", "Solutions ([S]=source, [T]=target)", "Object id", "Type code"
            });
            dups.ColumnWidths.AddRange(new double[] { 24, 40, 28, 22, 11, 12, 12, 70, 38, 10 });
            foreach (var d in duplicates)
            {
                dups.AddRow(d.TargetCount > 1 ? XlsxStyle.Amber : XlsxStyle.Default, d.TypeName, d.Name, d.Label, d.ParentTable,
                    d.SolutionCount, d.SourceCount, d.TargetCount, d.Solutions, d.ObjectId.ToString(), d.ComponentType);
            }

            var solutions = new XlsxSheet("Solutions") { AutoFilter = true };
            solutions.Headers.AddRange(new[] { "Role", "Unique name", "Display name", "Version", "Managed" });
            solutions.ColumnWidths.AddRange(new double[] { 10, 40, 40, 14, 10 });
            foreach (var s in result.SourceSolutions.Concat(result.TargetSolutions))
            {
                solutions.AddRow(XlsxStyle.Default, s.Role.ToString(), s.UniqueName, s.FriendlyName, s.Version, s.IsManaged);
            }

            return new List<XlsxSheet> { summary, comparison, dups, solutions };
        }

        // ------------------------------------------------------------------ HTML

        public static string BuildHtml(ComparisonResult result, IList<ComparisonRow> rows, IList<DuplicateRow> duplicates)
        {
            rows = rows ?? result.Rows;
            duplicates = duplicates ?? result.Duplicates;
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>Solution Component Comparer report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;font-size:13px;margin:24px;color:#222}");
            sb.AppendLine("h1{font-size:20px;margin:0 0 4px}h2{font-size:16px;margin:28px 0 8px}");
            sb.AppendLine(".meta{color:#666;margin-bottom:16px}");
            sb.AppendLine("table{border-collapse:collapse;width:100%}th,td{border:1px solid #ddd;padding:4px 6px;text-align:left;vertical-align:top}");
            sb.AppendLine("th{background:#f0f0f0;position:sticky;top:0;cursor:pointer}");
            sb.AppendLine(".summary{width:auto}.summary td{white-space:nowrap}.summary td.desc{white-space:normal;color:#555}");
            sb.AppendLine(".mono{font-family:Consolas,monospace;font-size:12px;color:#555}");
            sb.AppendLine(".bar{display:flex;gap:14px;flex-wrap:wrap;align-items:center;margin:8px 0;padding:8px;background:#fafafa;border:1px solid #eee}");
            sb.AppendLine(".bar label{white-space:nowrap}.chip{display:inline-block;width:12px;height:12px;border:1px solid #bbb;vertical-align:middle;margin-right:4px}");
            sb.AppendLine("input[type=search]{padding:4px 6px;width:280px}");
            sb.AppendLine(".sol{max-width:420px}");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine("<h1>Solution Component Comparer report</h1>");
            sb.AppendLine("<div class=\"meta\">Environment: <b>" + H(result.EnvironmentName) + "</b> &middot; Generated: " +
                H(result.GeneratedOn.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) + "</div>");

            sb.AppendLine("<p><b>Source solutions (" + result.SourceSolutions.Count + "):</b> " + H(string.Join(", ", result.SourceSolutions.Select(s => s.UniqueName))) + "</p>");
            sb.AppendLine("<p><b>Target solutions (" + result.TargetSolutions.Count + "):</b> " + H(string.Join(", ", result.TargetSolutions.Select(s => s.UniqueName))) + "</p>");

            sb.AppendLine("<h2>Summary</h2><table class=\"summary\"><tr><th>Status</th><th>Components</th><th>Meaning</th></tr>");
            foreach (var status in StatusOrder)
            {
                int n = result.Count(status);
                if (n == 0 && status == ComparisonStatus.Listed) continue;
                sb.AppendLine("<tr style=\"background:" + HtmlColor(status) + "\"><td>" + H(ComparisonEngine.StatusText(status)) + "</td><td>" + n +
                    "</td><td class=\"desc\">" + H(StatusDescription(status)) + "</td></tr>");
            }
            sb.AppendLine("<tr><td><b>Total</b></td><td><b>" + result.Rows.Count + "</b></td><td></td></tr>");
            sb.AppendLine("<tr><td>Components in 2+ selected solutions</td><td>" + result.Duplicates.Count + "</td><td></td></tr>");
            sb.AppendLine("</table>");

            sb.AppendLine("<h2>Comparison (" + rows.Count + " rows)</h2>");
            sb.AppendLine("<div class=\"bar\" id=\"statusBar\">");
            foreach (var status in StatusOrder)
            {
                if (!rows.Any(r => r.Status == status)) continue;
                sb.AppendLine("<label><input type=\"checkbox\" checked data-status=\"" + (int)status + "\"><span class=\"chip\" style=\"background:" +
                    HtmlColor(status) + "\"></span>" + H(ComparisonEngine.StatusText(status)) + "</label>");
            }
            sb.AppendLine("<input type=\"search\" id=\"search\" placeholder=\"Search name, table, solution, id...\"><span id=\"visible\"></span></div>");

            sb.AppendLine("<table id=\"cmp\"><thead><tr><th>Status</th><th>Type</th><th>Name</th><th>Label</th><th>Table</th><th>Source solutions</th><th>Target solutions</th><th>Source behavior</th><th>Target behavior</th><th>Also in other solutions</th><th>Object id</th></tr></thead><tbody>");
            foreach (var r in rows)
            {
                sb.Append("<tr data-status=\"" + (int)r.Status + "\" style=\"background:" + HtmlColor(r.Status) + "\">");
                sb.Append("<td>" + H(r.StatusText) + "</td><td>" + H(r.TypeName) + "</td><td>" + H(r.Name) + "</td><td>" + H(r.Label) +
                    "</td><td>" + H(r.ParentTable) + "</td><td class=\"sol\">" + H(r.SourceSolutions) + "</td><td class=\"sol\">" + H(r.TargetSolutions) +
                    "</td><td>" + H(r.SourceBehavior) + "</td><td>" + H(r.TargetBehavior) + "</td><td class=\"sol\">" + H(r.OtherSolutions) + "</td><td class=\"mono\">" + r.ObjectId + "</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");

            sb.AppendLine("<h2>Components in two or more selected solutions (" + duplicates.Count + ")</h2>");
            sb.AppendLine("<p class=\"meta\">[S] = source solution, [T] = target solution. Highlighted rows sit in more than one target solution.</p>");
            sb.AppendLine("<table id=\"dup\"><thead><tr><th>Type</th><th>Name</th><th>Label</th><th>Table</th><th>Solutions #</th><th>In targets #</th><th>Solutions</th><th>Object id</th></tr></thead><tbody>");
            foreach (var d in duplicates)
            {
                sb.Append("<tr" + (d.TargetCount > 1 ? " style=\"background:#ffe8b3\"" : string.Empty) + ">");
                sb.Append("<td>" + H(d.TypeName) + "</td><td>" + H(d.Name) + "</td><td>" + H(d.Label) + "</td><td>" + H(d.ParentTable) +
                    "</td><td>" + d.SolutionCount + "</td><td>" + d.TargetCount + "</td><td>" + H(d.Solutions) + "</td><td class=\"mono\">" + d.ObjectId + "</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");

            sb.AppendLine("<script>");
            sb.AppendLine("(function(){");
            sb.AppendLine("var rows=[].slice.call(document.querySelectorAll('#cmp tbody tr'));");
            sb.AppendLine("var boxes=[].slice.call(document.querySelectorAll('#statusBar input[type=checkbox]'));");
            sb.AppendLine("var search=document.getElementById('search');var counter=document.getElementById('visible');");
            sb.AppendLine("function apply(){var on={};boxes.forEach(function(b){on[b.getAttribute('data-status')]=b.checked;});");
            sb.AppendLine("var q=search.value.toLowerCase();var n=0;rows.forEach(function(r){var ok=on[r.getAttribute('data-status')]&&(!q||r.textContent.toLowerCase().indexOf(q)>=0);r.style.display=ok?'':'none';if(ok)n++;});");
            sb.AppendLine("counter.textContent=n+' visible';}");
            sb.AppendLine("boxes.forEach(function(b){b.addEventListener('change',apply);});search.addEventListener('input',apply);apply();");
            sb.AppendLine("[].slice.call(document.querySelectorAll('table')).forEach(function(t){var ths=t.querySelectorAll('th');[].forEach.call(ths,function(th,i){th.addEventListener('click',function(){");
            sb.AppendLine("var b=t.tBodies[0];if(!b)return;var asc=th.getAttribute('data-asc')!=='1';th.setAttribute('data-asc',asc?'1':'0');");
            sb.AppendLine("var rs=[].slice.call(b.rows);rs.sort(function(x,y){var a=x.cells[i].textContent,c=y.cells[i].textContent;var na=parseFloat(a),nc=parseFloat(c);");
            sb.AppendLine("var r=(!isNaN(na)&&!isNaN(nc)&&String(na)===a&&String(nc)===c)?na-nc:a.localeCompare(c);return asc?r:-r;});rs.forEach(function(r){b.appendChild(r);});});});});");
            sb.AppendLine("})();");
            sb.AppendLine("</script>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string H(string text)
        {
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }
    }
}
