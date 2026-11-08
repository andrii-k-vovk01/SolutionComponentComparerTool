using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolutionComponentComparer.Core;

namespace SolutionComponentComparer.Tests
{
    /// <summary>
    /// Small self-checking test harness for the comparison engine and report writers.
    /// Run: build as a console app (net48) and execute; exit code 0 = all passed.
    /// </summary>
    internal static class EngineTests
    {
        private class FakeNames : IComponentNameProvider
        {
            public readonly Dictionary<Guid, ComponentInfo> Map = new Dictionary<Guid, ComponentInfo>();

            public ComponentInfo Describe(int componentType, Guid objectId, string typeLabelFromServer)
            {
                ComponentInfo info;
                return Map.TryGetValue(objectId, out info) ? info : null;
            }
        }

        private static int _failures;

        private static void Check(bool condition, string message)
        {
            Console.WriteLine((condition ? "PASS  " : "FAIL  ") + message);
            if (!condition) _failures++;
        }

        public static int Run(string[] args)
        {
            var outDir = args.Length > 0 ? args[0] : Path.GetTempPath();

            var oldA = new SolutionRef { Id = Guid.NewGuid(), UniqueName = "Old_A", FriendlyName = "Old A", Role = SolutionRole.Source, Version = "1.0" };
            var oldB = new SolutionRef { Id = Guid.NewGuid(), UniqueName = "Old_B", FriendlyName = "Old B", Role = SolutionRole.Source, Version = "1.0" };
            var core = new SolutionRef { Id = Guid.NewGuid(), UniqueName = "Core", FriendlyName = "Core", Role = SolutionRole.Target, Version = "2.0" };
            var web = new SolutionRef { Id = Guid.NewGuid(), UniqueName = "WebResources", FriendlyName = "Web", Role = SolutionRole.Target, Version = "2.0" };
            var ignored = new SolutionRef { Id = Guid.NewGuid(), UniqueName = "Other", Role = SolutionRole.None };

            var names = new FakeNames();
            Func<string, ComponentInfo> info = n => new ComponentInfo { Name = n };

            var account = Guid.NewGuid();      // table, full in Old_A, metadata only in Core -> BehaviorDiffers
            var contact = Guid.NewGuid();      // table, full in Core
            var attrOnContact = Guid.NewGuid();// column of contact, only in Old_B -> Covered by target table
            var attrOnLead = Guid.NewGuid();   // column of lead, only in Old_B -> Missing
            var js = Guid.NewGuid();           // web resource in Old_A, Old_B, WebResources and Core -> InBoth + duplicate in 2 targets
            var step = Guid.NewGuid();         // plugin step only in Old_A -> Missing
            var envVar = Guid.NewGuid();       // only in Core -> OnlyInTarget
            var leadForm = Guid.NewGuid();     // form only in Core, lead full in Old_B -> CoveredBySourceTable
            var lead = Guid.NewGuid();         // lead table full in Old_B and core shell -> BehaviorDiffers
            var otherOnly = Guid.NewGuid();    // in ignored solution only -> must not appear

            names.Map[account] = new ComponentInfo { Name = "account", Label = "Account", EntityLogicalName = "account" };
            names.Map[contact] = new ComponentInfo { Name = "contact", Label = "Contact", EntityLogicalName = "contact" };
            names.Map[lead] = new ComponentInfo { Name = "lead", Label = "Lead", EntityLogicalName = "lead" };
            var c1 = info("new_score"); c1.ParentEntities.Add("contact"); names.Map[attrOnContact] = c1;
            var c2 = info("new_rating"); c2.ParentEntities.Add("lead"); names.Map[attrOnLead] = c2;
            var f1 = info("Lead main form"); f1.ParentEntities.Add("lead"); names.Map[leadForm] = f1;
            names.Map[js] = info("new_/scripts/account.js");
            names.Map[step] = info("Account Create step");
            names.Map[envVar] = info("new_ApiUrl");

            var m = new List<ComponentMembership>
            {
                M(oldA, 1, account, 0), M(core, 1, account, 1),
                M(core, 1, contact, 0),
                M(oldB, 2, attrOnContact, null),
                M(oldB, 2, attrOnLead, null),
                M(oldA, 61, js, null), M(oldB, 61, js, null), M(web, 61, js, null), M(core, 61, js, null),
                M(oldA, 92, step, null),
                M(core, 380, envVar, null),
                M(core, 60, leadForm, null),
                M(oldB, 1, lead, 0), M(core, 1, lead, 2),
                M(ignored, 61, otherOnly, null)
            };

            var result = ComparisonEngine.Compare(new List<SolutionRef> { oldA, oldB, core, web, ignored }, m, names);
            Func<Guid, ComparisonRow> row = id => result.Rows.Single(r => r.ObjectId == id);

            Check(result.SourceSolutions.Count == 2 && result.TargetSolutions.Count == 2, "roles split into 2 sources / 2 targets");
            Check(result.Rows.All(r => r.ObjectId != otherOnly), "components of unselected solutions are ignored");
            Check(row(account).Status == ComparisonStatus.BehaviorDiffers, "account: all subcomponents in source, none in target -> BehaviorDiffers");
            Check(row(contact).Status == ComparisonStatus.OnlyInTarget, "contact: only in target");
            Check(row(attrOnContact).Status == ComparisonStatus.CoveredByTargetTable, "contact column: covered by target table with all subcomponents");
            Check(row(attrOnLead).Status == ComparisonStatus.MissingInTarget, "lead column: missing in target (lead is only shell in target)");
            Check(row(js).Status == ComparisonStatus.InBoth, "web resource: in both");
            Check(row(js).SourceCount == 2 && row(js).TargetCount == 2, "web resource: counts 2/2");
            Check(row(js).TargetSolutions == "Core, WebResources", "web resource: target list sorted");
            Check(row(step).Status == ComparisonStatus.MissingInTarget, "plugin step: missing in target");
            Check(row(envVar).Status == ComparisonStatus.OnlyInTarget, "env variable: only in target");
            Check(row(leadForm).Status == ComparisonStatus.CoveredBySourceTable, "lead form: covered by source table");
            Check(row(lead).Status == ComparisonStatus.BehaviorDiffers, "lead table: all subcomponents vs shell -> BehaviorDiffers");
            Check(result.Rows.First().Status == ComparisonStatus.MissingInTarget, "rows sorted with missing first");
            Check(result.Count(ComparisonStatus.MissingInTarget) == 2, "2 missing");
            Check(row(step).TypeName == "Plugin Step", "type name from catalog");

            var dupJs = result.Duplicates.SingleOrDefault(d => d.ObjectId == js);
            Check(dupJs != null && dupJs.SolutionCount == 4 && dupJs.TargetCount == 2, "duplicate: web resource in 4 solutions, 2 targets");
            Check(dupJs != null && dupJs.Solutions == "Old_A [S], Old_B [S], Core [T], WebResources [T]", "duplicate: solution list " + (dupJs == null ? "" : dupJs.Solutions));
            Check(result.Duplicates.Count == 3, "3 duplicates (account, lead, js), got " + result.Duplicates.Count);
            Check(result.Duplicates.First().ObjectId == js, "duplicates sorted with multi-target first");

            // No targets -> plain list
            var listOnly = ComparisonEngine.Compare(new List<SolutionRef> { oldA }, m, names);
            Check(listOnly.Rows.All(r => r.Status == ComparisonStatus.Listed) && listOnly.Rows.Count == 3, "no targets -> 3 listed components");

            // Unknown name
            var unknown = Guid.NewGuid();
            var r2 = ComparisonEngine.Compare(new List<SolutionRef> { oldA, core }, new List<ComponentMembership> { M(oldA, 10099, unknown, null, "Connection Reference") }, names);
            Check(r2.Rows[0].Name == "(unresolved)" && r2.Rows[0].TypeName == "Connection Reference", "unknown component falls back to server type label");

            // Reports
            result.EnvironmentName = "Test & <env>";
            var xlsx = Path.Combine(outDir, "scc-test.xlsx");
            XlsxWriter.Write(xlsx, ReportBuilder.BuildWorkbook(result, null, null));
            Check(File.Exists(xlsx) && new FileInfo(xlsx).Length > 1000, "xlsx written: " + xlsx);
            var html = Path.Combine(outDir, "scc-test.html");
            File.WriteAllText(html, ReportBuilder.BuildHtml(result, null, null));
            Check(File.ReadAllText(html).Contains("Test &amp; &lt;env&gt;"), "html escapes environment name");
            Check(XlsxWriter.ColumnName(0) == "A" && XlsxWriter.ColumnName(25) == "Z" && XlsxWriter.ColumnName(26) == "AA" && XlsxWriter.ColumnName(701) == "ZZ", "column names");

            Console.WriteLine(_failures == 0 ? "ALL TESTS PASSED" : _failures + " TEST(S) FAILED");
            return _failures == 0 ? 0 : 1;
        }

        private static ComponentMembership M(SolutionRef s, int type, Guid id, int? behavior, string label = null)
        {
            return new ComponentMembership { SolutionId = s.Id, ComponentType = type, ObjectId = id, RootBehavior = behavior, TypeLabel = label };
        }
    }
}
