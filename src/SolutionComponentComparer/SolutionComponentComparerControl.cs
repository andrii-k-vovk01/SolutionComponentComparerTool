using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using McTools.Xrm.Connection;
using IOrganizationService = Microsoft.Xrm.Sdk.IOrganizationService;
using SolutionComponentComparer.Core;
using SolutionComponentComparer.Data;
using SolutionComponentComparer.UI;
using XrmToolBox.Extensibility;

namespace SolutionComponentComparer
{
    /// <summary>
    /// Main tool control. Everything is built in code (no designer files) to keep the project small.
    /// The tool is strictly read-only: it never creates, updates or deletes anything in Dataverse.
    /// </summary>
    public class SolutionComponentComparerControl : PluginControlBase
    {
        private static readonly Color RedBack = Color.FromArgb(248, 215, 218);
        private static readonly Color AmberBack = Color.FromArgb(255, 232, 179);
        private static readonly Color BlueBack = Color.FromArgb(214, 233, 255);
        private static readonly Color GreenBack = Color.FromArgb(226, 240, 217);

        private const string AllTypes = "(All component types)";

        // toolbar
        private ToolStrip _toolStrip;
        private ToolStripButton _btnLoad;
        private ToolStripButton _btnCompare;
        private ToolStripDropDownButton _btnExport;
        private ToolStripButton _btnWhereUsed;
        private ToolStripLabel _lblInfo;

        // solutions (left)
        private TextBox _txtSolutionFilter;
        private CheckBox _chkShowManaged;
        private CheckBox _chkShowHidden;
        private CheckBox _chkSelectedOnly;
        private DataGridView _gridSolutions;
        private Label _lblSelection;

        // comparison tab
        private TabControl _tabs;
        private TabPage _tabCompare;
        private TabPage _tabDuplicates;
        private readonly Dictionary<ComparisonStatus, CheckBox> _statusFilters = new Dictionary<ComparisonStatus, CheckBox>();
        private TextBox _txtSearch;
        private ComboBox _cmbType;
        private Label _lblSummary;
        private DataGridView _gridCompare;

        // duplicates tab
        private CheckBox _chkDupMultiTarget;
        private TextBox _txtDupSearch;
        private Label _lblDupSummary;
        private DataGridView _gridDuplicates;

        // where used (bottom)
        private Label _lblWhereUsed;
        private DataGridView _gridWhereUsed;

        private readonly Timer _filterTimer = new Timer { Interval = 300 };

        // state
        private List<SolutionRow> _solutions = new List<SolutionRow>();
        private ComparisonResult _result;
        private ComponentNameResolver _names = new ComponentNameResolver();
        private PluginSettings _settings = new PluginSettings();
        private bool _loaded;
        private bool _busy;
        private bool _suppressSolutionEvents;

        public SolutionComponentComparerControl()
        {
            BuildUi();
            _filterTimer.Tick += (s, e) =>
            {
                _filterTimer.Stop();
                ApplyCompareFilter();
                ApplyDuplicateFilter();
                ApplySolutionFilter();
            };
        }

        // =====================================================================
        // XrmToolBox lifecycle
        // =====================================================================

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _loaded = true;
            if (Service != null) ExecuteMethod(LoadSolutions);
        }

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            SaveSettings();
            base.UpdateConnection(newService, detail, actionName, parameter);

            _names = new ComponentNameResolver();
            _result = null;
            _solutions = new List<SolutionRow>();
            _gridSolutions.DataSource = null;
            _gridCompare.DataSource = null;
            _gridDuplicates.DataSource = null;
            _gridWhereUsed.DataSource = null;
            _lblSummary.Text = "Pick source and target solutions on the left, then click Compare.";
            _lblDupSummary.Text = string.Empty;
            _lblWhereUsed.Text = "Where used: select a component and click \"Where used\" (or double-click it).";
            LoadSettings();
            UpdateButtons();

            if (_loaded && newService != null) LoadSolutions();
        }

        public override void ClosingPlugin(PluginCloseInfo info)
        {
            SaveSettings();
            base.ClosingPlugin(info);
        }

        // =====================================================================
        // Settings
        // =====================================================================

        private string SettingsKey
        {
            get
            {
                var detail = ConnectionDetail;
                if (detail == null) return "default";
                return detail.ConnectionId.HasValue ? detail.ConnectionId.Value.ToString("N") : "default";
            }
        }

        private void LoadSettings()
        {
            PluginSettings settings;
            try
            {
                if (!SettingsManager.Instance.TryLoad(GetType(), out settings, SettingsKey) || settings == null)
                {
                    settings = new PluginSettings();
                }
            }
            catch (Exception)
            {
                settings = new PluginSettings();
            }

            _settings = settings;
            _suppressSolutionEvents = true;
            _chkShowManaged.Checked = _settings.ShowManaged;
            _chkShowHidden.Checked = _settings.ShowHidden;
            _suppressSolutionEvents = false;
        }

        private void SaveSettings()
        {
            if (_settings == null) return;
            try
            {
                if (_solutions.Count > 0)
                {
                    _settings.SourceSolutions = _solutions.Where(s => s.Source).Select(s => s.UniqueName).ToList();
                    _settings.TargetSolutions = _solutions.Where(s => s.Target).Select(s => s.UniqueName).ToList();
                }
                _settings.ShowManaged = _chkShowManaged.Checked;
                _settings.ShowHidden = _chkShowHidden.Checked;
                SettingsManager.Instance.Save(GetType(), _settings, SettingsKey);
            }
            catch (Exception ex)
            {
                LogWarning("Could not save settings: {0}", ex.Message);
            }
        }

        // =====================================================================
        // Data operations (all read-only)
        // =====================================================================

        private void LoadSolutions()
        {
            if (_busy) return;
            // Keep the current picks across a reload: persist them, then read them back.
            if (_solutions.Count > 0) SaveSettings();
            LoadSettings();
            SetBusy(true);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading solutions...",
                Work = (worker, args) =>
                {
                    args.Result = new DataverseRepository(Service).LoadSolutions();
                },
                PostWorkCallBack = args =>
                {
                    SetBusy(false);
                    if (args.Error != null)
                    {
                        ShowError("Could not load solutions", args.Error);
                        return;
                    }

                    var infos = (List<SolutionInfo>)args.Result;
                    var sources = new HashSet<string>(_settings.SourceSolutions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                    var targets = new HashSet<string>(_settings.TargetSolutions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

                    _solutions = infos.Select(i =>
                    {
                        var row = new SolutionRow(i);
                        if (sources.Contains(i.UniqueName)) row.Source = true;
                        else if (targets.Contains(i.UniqueName)) row.Target = true;
                        row.PropertyChanged += (s, e) => UpdateSelectionLabel();
                        return row;
                    }).ToList();

                    ApplySolutionFilter();
                    UpdateSelectionLabel();
                    LogInfo("Loaded {0} solutions", _solutions.Count);
                }
            });
        }

        private void Compare()
        {
            if (_busy) return;
            var selected = _solutions.Where(s => s.Role != SolutionRole.None).ToList();
            if (!selected.Any(s => s.Source))
            {
                MessageBox.Show(this,
                    "Mark at least one solution as Source (the old solutions you want to check).\r\n\r\n" +
                    "Tip: select several rows and click \"Set as Source\". Targets are the solutions that should hold everything.",
                    "Solution Component Comparer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var refs = selected.Select(s => s.ToRef()).ToList();
            var environment = EnvironmentName();
            var names = _names;
            SaveSettings();
            SetBusy(true);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading solution components...",
                Work = (worker, args) =>
                {
                    var repository = new DataverseRepository(Service);
                    Action<string> progress = msg => worker.ReportProgress(0, msg);

                    var memberships = repository.LoadMemberships(refs.Select(r => r.Id).ToList(), progress);
                    names.EnsureNames(repository, memberships, progress);

                    progress("Comparing " + memberships.Count.ToString("N0") + " solution component rows...");
                    var result = ComparisonEngine.Compare(refs, memberships, names);
                    result.EnvironmentName = environment;

                    // Is a "missing" component still held by some other (not selected) solution?
                    var missing = result.Rows.Where(r => r.Status == ComparisonStatus.MissingInTarget).ToList();
                    if (missing.Count > 0)
                    {
                        var others = repository.LoadOtherSolutions(
                            missing.Select(r => new ComponentKey(r.ComponentType, r.ObjectId)).ToList(),
                            new HashSet<Guid>(refs.Select(r => r.Id)),
                            progress);
                        foreach (var row in missing)
                        {
                            List<string> list;
                            row.OtherSolutions = others.TryGetValue(new ComponentKey(row.ComponentType, row.ObjectId), out list)
                                ? string.Join(", ", list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                                : string.Empty;
                        }
                    }

                    args.Result = new object[] { result, names.Warnings.ToList() };
                },
                ProgressChanged = e =>
                {
                    var message = e.UserState as string;
                    if (!string.IsNullOrEmpty(message)) SetWorkingMessage(message);
                },
                PostWorkCallBack = args =>
                {
                    SetBusy(false);
                    if (args.Error != null)
                    {
                        ShowError("Comparison failed", args.Error);
                        return;
                    }

                    var payload = (object[])args.Result;
                    _result = (ComparisonResult)payload[0];
                    foreach (var w in (List<string>)payload[1]) LogWarning("{0}", w);

                    CheckBox listedBox;
                    if (_statusFilters.TryGetValue(ComparisonStatus.Listed, out listedBox))
                    {
                        listedBox.Visible = _result.Count(ComparisonStatus.Listed) > 0;
                        listedBox.Checked = true;
                    }
                    FillTypeFilter();
                    ApplyCompareFilter();
                    ApplyDuplicateFilter();
                    _tabs.SelectedTab = _tabCompare;
                    UpdateButtons();
                    LogInfo("Compared {0} source / {1} target solutions: {2} components, {3} missing in target",
                        _result.SourceSolutions.Count, _result.TargetSolutions.Count, _result.Rows.Count,
                        _result.Count(ComparisonStatus.MissingInTarget));

                    if (_result.TargetSolutions.Count == 0)
                    {
                        _lblSummary.Text = "No target selected: showing the plain component list of the source solutions. " + _lblSummary.Text;
                    }
                }
            });
        }

        private void ShowWhereUsed()
        {
            if (_busy) return;
            int type;
            Guid id;
            string name;
            if (!TryGetSelectedComponent(out type, out id, out name))
            {
                MessageBox.Show(this, "Select a component in the Comparison or Duplicates grid first.", "Where used",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var roles = _solutions.ToDictionary(s => s.Info.Id, s => s.Role);
            var typeName = ComponentTypeCatalog.GetTypeName(type, null);
            SetBusy(true);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Looking up all solutions that contain " + name + "...",
                Work = (worker, args) =>
                {
                    args.Result = new DataverseRepository(Service).LoadWhereUsed(type, id, solutionId =>
                    {
                        SolutionRole role;
                        if (!roles.TryGetValue(solutionId, out role)) return string.Empty;
                        return role == SolutionRole.None ? string.Empty : role.ToString();
                    });
                },
                PostWorkCallBack = args =>
                {
                    SetBusy(false);
                    if (args.Error != null)
                    {
                        ShowError("Where used failed", args.Error);
                        return;
                    }

                    var rows = (List<WhereUsedRow>)args.Result;
                    _gridWhereUsed.DataSource = new SortableBindingList<WhereUsedRow>(rows);
                    int outside = rows.Count(r => string.IsNullOrEmpty(r.Role));
                    _lblWhereUsed.Text = string.Format(
                        "Where used: {0} \"{1}\" is in {2} solution(s) in this environment, {3} of them not selected as source/target.",
                        typeName, name, rows.Count, outside);
                }
            });
        }

        // =====================================================================
        // Filtering / binding
        // =====================================================================

        private void ApplySolutionFilter()
        {
            if (_suppressSolutionEvents) return;
            var text = (_txtSolutionFilter.Text ?? string.Empty).Trim();
            var rows = _solutions.Where(s =>
                    (_chkShowManaged.Checked || !s.Managed || s.Role != SolutionRole.None) &&
                    (_chkShowHidden.Checked || s.Info.IsVisible || s.Role != SolutionRole.None) &&
                    (!_chkSelectedOnly.Checked || s.Role != SolutionRole.None) &&
                    (text.Length == 0 || Contains(s.DisplayName, text) || Contains(s.UniqueName, text) || Contains(s.Publisher, text)))
                .ToList();

            _gridSolutions.DataSource = new SortableBindingList<SolutionRow>(rows);
            UpdateSelectionLabel();
        }

        private void ApplyCompareFilter()
        {
            if (_result == null)
            {
                _gridCompare.DataSource = null;
                return;
            }

            var text = (_txtSearch.Text ?? string.Empty).Trim();
            var type = _cmbType.SelectedItem as string;
            bool allTypes = string.IsNullOrEmpty(type) || type == AllTypes;

            var rows = _result.Rows.Where(r =>
                    IsStatusVisible(r.Status) &&
                    (allTypes || r.TypeName == type) &&
                    (text.Length == 0 || MatchesSearch(r, text)))
                .ToList();

            _gridCompare.DataSource = new SortableBindingList<ComparisonRow>(rows);

            var sb = new StringBuilder();
            sb.AppendFormat("Showing {0:N0} of {1:N0}   |   ", rows.Count, _result.Rows.Count);
            sb.AppendFormat("Missing in target: {0:N0}   Target has less: {1:N0}   Only in target: {2:N0}   Covered by table: {3:N0}   In both: {4:N0}",
                _result.Count(ComparisonStatus.MissingInTarget),
                _result.Count(ComparisonStatus.BehaviorDiffers),
                _result.Count(ComparisonStatus.OnlyInTarget),
                _result.Count(ComparisonStatus.CoveredByTargetTable) + _result.Count(ComparisonStatus.CoveredBySourceTable),
                _result.Count(ComparisonStatus.InBoth));
            if (_result.Count(ComparisonStatus.Listed) > 0) sb.AppendFormat("   Listed: {0:N0}", _result.Count(ComparisonStatus.Listed));
            _lblSummary.Text = sb.ToString();
        }

        private void ApplyDuplicateFilter()
        {
            if (_result == null)
            {
                _gridDuplicates.DataSource = null;
                return;
            }

            var text = (_txtDupSearch.Text ?? string.Empty).Trim();
            var rows = _result.Duplicates.Where(d =>
                    (!_chkDupMultiTarget.Checked || d.TargetCount > 1) &&
                    (text.Length == 0 || Contains(d.Name, text) || Contains(d.Label, text) || Contains(d.TypeName, text) ||
                     Contains(d.ParentTable, text) || Contains(d.Solutions, text) || d.ObjectId.ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            _gridDuplicates.DataSource = new SortableBindingList<DuplicateRow>(rows);
            _lblDupSummary.Text = string.Format("Showing {0:N0} of {1:N0} components that sit in two or more of the selected solutions   |   in 2+ target solutions: {2:N0}",
                rows.Count, _result.Duplicates.Count, _result.Duplicates.Count(d => d.TargetCount > 1));
            _tabDuplicates.Text = "Duplicates (" + _result.Duplicates.Count.ToString("N0") + ")";
        }

        private bool IsStatusVisible(ComparisonStatus status)
        {
            CheckBox box;
            if (status == ComparisonStatus.CoveredBySourceTable) status = ComparisonStatus.CoveredByTargetTable;
            return !_statusFilters.TryGetValue(status, out box) || box.Checked;
        }

        private static bool MatchesSearch(ComparisonRow r, string text)
        {
            return Contains(r.Name, text) || Contains(r.Label, text) || Contains(r.TypeName, text) || Contains(r.ParentTable, text) ||
                   Contains(r.SourceSolutions, text) || Contains(r.TargetSolutions, text) || Contains(r.OtherSolutions, text) ||
                   r.ObjectId.ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Contains(string value, string text)
        {
            return value != null && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void FillTypeFilter()
        {
            var current = _cmbType.SelectedItem as string;
            _cmbType.BeginUpdate();
            _cmbType.Items.Clear();
            _cmbType.Items.Add(AllTypes);
            foreach (var t in _result.Rows.Select(r => r.TypeName).Distinct().OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                _cmbType.Items.Add(t);
            }
            _cmbType.SelectedItem = current != null && _cmbType.Items.Contains(current) ? current : AllTypes;
            _cmbType.EndUpdate();
        }

        private void UpdateSelectionLabel()
        {
            int sources = _solutions.Count(s => s.Source);
            int targets = _solutions.Count(s => s.Target);
            _lblSelection.Text = string.Format("{0} solutions loaded   |   Source: {1}   Target: {2}", _solutions.Count, sources, targets);
            UpdateButtons();
        }

        private void SetRole(SolutionRole role)
        {
            var rows = _gridSolutions.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.DataBoundItem as SolutionRow)
                .Where(r => r != null)
                .ToList();
            if (rows.Count == 0 && _gridSolutions.CurrentRow != null)
            {
                var current = _gridSolutions.CurrentRow.DataBoundItem as SolutionRow;
                if (current != null) rows.Add(current);
            }

            foreach (var row in rows)
            {
                row.Source = role == SolutionRole.Source;
                row.Target = role == SolutionRole.Target;
            }
            _gridSolutions.Invalidate();
            UpdateSelectionLabel();
        }

        private void ClearAllRoles()
        {
            foreach (var row in _solutions)
            {
                row.Source = false;
                row.Target = false;
            }
            _gridSolutions.Invalidate();
            UpdateSelectionLabel();
        }

        // =====================================================================
        // Export
        // =====================================================================

        private void Export(bool excel, bool visibleOnly)
        {
            if (_result == null) return;

            var rows = visibleOnly ? GridItems<ComparisonRow>(_gridCompare) : _result.Rows;
            var dups = visibleOnly ? GridItems<DuplicateRow>(_gridDuplicates) : _result.Duplicates;

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = excel ? "Excel workbook (*.xlsx)|*.xlsx" : "HTML report (*.html)|*.html";
                dialog.FileName = "SolutionComparison_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + (excel ? ".xlsx" : ".html");
                if (!string.IsNullOrEmpty(_settings.LastExportFolder) && Directory.Exists(_settings.LastExportFolder))
                {
                    dialog.InitialDirectory = _settings.LastExportFolder;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    if (excel)
                    {
                        XlsxWriter.Write(dialog.FileName, ReportBuilder.BuildWorkbook(_result, rows, dups));
                    }
                    else
                    {
                        File.WriteAllText(dialog.FileName, ReportBuilder.BuildHtml(_result, rows, dups), new UTF8Encoding(true));
                    }

                    _settings.LastExportFolder = Path.GetDirectoryName(dialog.FileName);
                    LogInfo("Report exported to {0}", dialog.FileName);
                    if (MessageBox.Show(this, "Report saved:\r\n" + dialog.FileName + "\r\n\r\nOpen it now?", "Export",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        Process.Start(dialog.FileName);
                    }
                }
                catch (Exception ex)
                {
                    ShowError("Export failed", ex);
                }
            }
        }

        private static List<T> GridItems<T>(DataGridView grid)
        {
            var list = grid.DataSource as IEnumerable<T>;
            return list == null ? new List<T>() : list.ToList();
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private bool TryGetSelectedComponent(out int type, out Guid id, out string name)
        {
            type = 0;
            id = Guid.Empty;
            name = null;

            var grid = _tabs.SelectedTab == _tabDuplicates ? _gridDuplicates : _gridCompare;
            var row = grid.CurrentRow ?? (grid.SelectedRows.Count > 0 ? grid.SelectedRows[0] : null);
            if (row == null) return false;

            var cmp = row.DataBoundItem as ComparisonRow;
            if (cmp != null)
            {
                type = cmp.ComponentType;
                id = cmp.ObjectId;
                name = cmp.Name;
                return true;
            }

            var dup = row.DataBoundItem as DuplicateRow;
            if (dup != null)
            {
                type = dup.ComponentType;
                id = dup.ObjectId;
                name = dup.Name;
                return true;
            }
            return false;
        }

        private string EnvironmentName()
        {
            var detail = ConnectionDetail;
            if (detail == null) return string.Empty;
            var name = detail.ConnectionName;
            var org = detail.OrganizationFriendlyName;
            if (!string.IsNullOrEmpty(org) && !string.Equals(org, name, StringComparison.OrdinalIgnoreCase))
            {
                name = string.IsNullOrEmpty(name) ? org : name + " (" + org + ")";
            }
            return name ?? string.Empty;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool connected = Service != null;
            _btnLoad.Enabled = connected && !_busy;
            _btnCompare.Enabled = connected && !_busy && _solutions.Any(s => s.Source);
            _btnExport.Enabled = !_busy && _result != null;
            _btnWhereUsed.Enabled = connected && !_busy && _result != null;
            _lblInfo.Text = connected ? "Read-only: this tool never changes anything in the environment." : "Connect to an environment to start.";
        }

        private void ShowError(string title, Exception ex)
        {
            LogError("{0}: {1}", title, ex.ToString());
            MessageBox.Show(this, title + ":\r\n\r\n" + ex.Message, "Solution Component Comparer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static Color ColorFor(ComparisonStatus status)
        {
            switch (status)
            {
                case ComparisonStatus.MissingInTarget: return RedBack;
                case ComparisonStatus.BehaviorDiffers: return AmberBack;
                case ComparisonStatus.OnlyInTarget: return BlueBack;
                case ComparisonStatus.CoveredByTargetTable:
                case ComparisonStatus.CoveredBySourceTable: return GreenBack;
                default: return Color.White;
            }
        }

        private void CopyFromCompareGrid(Func<ComparisonRow, string> selector)
        {
            var values = _gridCompare.SelectedRows.Cast<DataGridViewRow>()
                .OrderBy(r => r.Index)
                .Select(r => r.DataBoundItem as ComparisonRow)
                .Where(r => r != null)
                .Select(selector)
                .ToList();
            if (values.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, values));
        }

        // =====================================================================
        // UI construction
        // =====================================================================

        private void BuildUi()
        {
            SuspendLayout();
            Name = "SolutionComponentComparerControl";
            Size = new Size(1300, 760);
            Font = new Font("Segoe UI", 9F);

            // ---------------- toolbar
            _toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Padding = new Padding(4, 2, 4, 2) };
            _btnLoad = new ToolStripButton("Load solutions");
            _btnLoad.Click += (s, e) => ExecuteMethod(LoadSolutions);
            _btnCompare = new ToolStripButton("Compare") { Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            _btnCompare.Click += (s, e) => ExecuteMethod(Compare);
            _btnExport = new ToolStripDropDownButton("Export report");
            _btnExport.DropDownItems.Add("Excel (.xlsx) - all components", null, (s, e) => Export(true, false));
            _btnExport.DropDownItems.Add("Excel (.xlsx) - only rows visible in the grids", null, (s, e) => Export(true, true));
            _btnExport.DropDownItems.Add(new ToolStripSeparator());
            _btnExport.DropDownItems.Add("HTML report - all components", null, (s, e) => Export(false, false));
            _btnExport.DropDownItems.Add("HTML report - only rows visible in the grids", null, (s, e) => Export(false, true));
            _btnWhereUsed = new ToolStripButton("Where used?") { ToolTipText = "All solutions in this environment that contain the selected component" };
            _btnWhereUsed.Click += (s, e) => ExecuteMethod(ShowWhereUsed);
            _lblInfo = new ToolStripLabel { Alignment = ToolStripItemAlignment.Right, ForeColor = Color.DimGray };
            _toolStrip.Items.AddRange(new ToolStripItem[]
            {
                _btnLoad, new ToolStripSeparator(), _btnCompare, new ToolStripSeparator(), _btnExport, _btnWhereUsed, _lblInfo
            });

            // ---------------- main split
            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 6
            };

            mainSplit.Panel1.Controls.Add(BuildSolutionsPanel());

            var rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel2,
                SplitterWidth = 6
            };
            rightSplit.Panel1.Controls.Add(BuildTabs());
            rightSplit.Panel2.Controls.Add(BuildWhereUsedPanel());
            mainSplit.Panel2.Controls.Add(rightSplit);

            Controls.Add(mainSplit);
            Controls.Add(_toolStrip);

            // Splitter distances can only be set once the control has a size.
            Load += (s, e) =>
            {
                try
                {
                    mainSplit.SplitterDistance = Math.Max(360, Math.Min(560, Width / 3));
                    rightSplit.SplitterDistance = Math.Max(200, rightSplit.Height - 200);
                }
                catch (Exception)
                {
                    // ignore layout races
                }
            };

            UpdateButtons();
            ResumeLayout(true);
        }

        private Control BuildSolutionsPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(4) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                Text = "Pick solutions: Source = solutions to check, Target = solutions that should contain their components",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Padding = new Padding(0, 2, 0, 4),
                MaximumSize = new Size(560, 0)
            };

            var filterRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            filterRow.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(0, 6, 2, 0) });
            _txtSolutionFilter = new TextBox { Width = 170 };
            _txtSolutionFilter.TextChanged += (s, e) => RestartFilterTimer();
            filterRow.Controls.Add(_txtSolutionFilter);
            _chkShowManaged = new CheckBox { Text = "Managed", AutoSize = true, Checked = true, Margin = new Padding(8, 4, 0, 0) };
            _chkShowManaged.CheckedChanged += (s, e) => ApplySolutionFilter();
            _chkShowHidden = new CheckBox { Text = "Hidden", AutoSize = true, Margin = new Padding(6, 4, 0, 0) };
            _chkShowHidden.CheckedChanged += (s, e) => ApplySolutionFilter();
            _chkSelectedOnly = new CheckBox { Text = "Picked only", AutoSize = true, Margin = new Padding(6, 4, 0, 0) };
            _chkSelectedOnly.CheckedChanged += (s, e) => ApplySolutionFilter();
            filterRow.Controls.Add(_chkShowManaged);
            filterRow.Controls.Add(_chkShowHidden);
            filterRow.Controls.Add(_chkSelectedOnly);

            var buttonRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
            var btnSource = MakeButton("Set as Source", RedBack, (s, e) => SetRole(SolutionRole.Source));
            var btnTarget = MakeButton("Set as Target", GreenBack, (s, e) => SetRole(SolutionRole.Target));
            var btnUnset = MakeButton("Unset", SystemColors.Control, (s, e) => SetRole(SolutionRole.None));
            var btnClear = MakeButton("Clear all", SystemColors.Control, (s, e) => ClearAllRoles());
            buttonRow.Controls.AddRange(new Control[] { btnSource, btnTarget, btnUnset, btnClear });

            _gridSolutions = MakeGrid(true);
            _gridSolutions.ReadOnly = false;
            AddCheck(_gridSolutions, "Source", "Source", 52);
            AddCheck(_gridSolutions, "Target", "Target", 52);
            AddText(_gridSolutions, "DisplayName", "Display name", 170);
            AddText(_gridSolutions, "UniqueName", "Unique name", 170);
            AddText(_gridSolutions, "Version", "Version", 80);
            var managedCol = new DataGridViewCheckBoxColumn { DataPropertyName = "Managed", HeaderText = "Managed", Width = 62, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic };
            _gridSolutions.Columns.Add(managedCol);
            AddText(_gridSolutions, "Publisher", "Publisher", 130);
            AddText(_gridSolutions, "InstalledOn", "Installed on", 120);
            _gridSolutions.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_gridSolutions.IsCurrentCellDirty) _gridSolutions.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _gridSolutions.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var row = _gridSolutions.Rows[e.RowIndex].DataBoundItem as SolutionRow;
                if (row == null) return;
                e.CellStyle.BackColor = row.Source ? RedBack : row.Target ? GreenBack : Color.White;
            };

            _lblSelection = new Label { AutoSize = true, Padding = new Padding(0, 4, 0, 2), Text = "No solutions loaded." };

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(filterRow, 0, 1);
            layout.Controls.Add(buttonRow, 0, 2);
            layout.Controls.Add(_gridSolutions, 0, 3);
            layout.Controls.Add(_lblSelection, 0, 4);
            return layout;
        }

        private Control BuildTabs()
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };

            // ---------------- comparison tab
            _tabCompare = new TabPage("Comparison");
            var cmpLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
            cmpLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cmpLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cmpLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cmpLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var statusRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            statusRow.Controls.Add(new Label { Text = "Show:", AutoSize = true, Margin = new Padding(0, 6, 4, 0), Font = new Font("Segoe UI", 9F, FontStyle.Bold) });
            AddStatusFilter(statusRow, ComparisonStatus.MissingInTarget, "Missing in target", RedBack,
                "In a source solution, in none of the targets. Lost if the sources are deleted and nothing else holds it.");
            AddStatusFilter(statusRow, ComparisonStatus.BehaviorDiffers, "Target has less", AmberBack,
                "In both, but the source carries more (e.g. table with all subcomponents vs. table without subcomponents).");
            AddStatusFilter(statusRow, ComparisonStatus.OnlyInTarget, "Only in target", BlueBack,
                "In a target solution, in none of the sources.");
            AddStatusFilter(statusRow, ComparisonStatus.CoveredByTargetTable, "Covered by table", GreenBack,
                "Not listed by itself on that side, but its table is there with 'include all subcomponents'.");
            AddStatusFilter(statusRow, ComparisonStatus.InBoth, "In both", Color.White,
                "In at least one source and at least one target.");
            AddStatusFilter(statusRow, ComparisonStatus.Listed, "Listed (no target)", Color.White,
                "No target selected: plain component list.");
            _statusFilters[ComparisonStatus.Listed].Visible = false;

            var btnProblems = new LinkLabel { Text = "problems only", AutoSize = true, Margin = new Padding(10, 6, 0, 0) };
            btnProblems.LinkClicked += (s, e) => SetStatusFilters(ComparisonStatus.MissingInTarget, ComparisonStatus.BehaviorDiffers);
            var btnAll = new LinkLabel { Text = "show all", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
            btnAll.LinkClicked += (s, e) => SetStatusFilters(_statusFilters.Keys.ToArray());
            statusRow.Controls.Add(btnProblems);
            statusRow.Controls.Add(btnAll);

            var searchRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
            searchRow.Controls.Add(new Label { Text = "Search:", AutoSize = true, Margin = new Padding(0, 6, 2, 0) });
            _txtSearch = new TextBox { Width = 260 };
            _txtSearch.TextChanged += (s, e) => RestartFilterTimer();
            searchRow.Controls.Add(_txtSearch);
            searchRow.Controls.Add(new Label { Text = "Type:", AutoSize = true, Margin = new Padding(10, 6, 2, 0) });
            _cmbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _cmbType.Items.Add(AllTypes);
            _cmbType.SelectedIndex = 0;
            _cmbType.SelectedIndexChanged += (s, e) => ApplyCompareFilter();
            searchRow.Controls.Add(_cmbType);

            _lblSummary = new Label
            {
                AutoSize = true,
                Padding = new Padding(0, 2, 0, 4),
                Text = "Pick source and target solutions on the left, then click Compare."
            };

            _gridCompare = MakeGrid(true);
            AddText(_gridCompare, "StatusText", "Status", 140);
            AddText(_gridCompare, "TypeName", "Component type", 140);
            AddText(_gridCompare, "Name", "Name", 230);
            AddText(_gridCompare, "Label", "Label", 150);
            AddText(_gridCompare, "ParentTable", "Table", 120);
            AddText(_gridCompare, "SourceCount", "Src #", 48);
            AddText(_gridCompare, "SourceSolutions", "Source solutions", 220);
            AddText(_gridCompare, "TargetCount", "Tgt #", 48);
            AddText(_gridCompare, "TargetSolutions", "Target solutions", 200);
            AddText(_gridCompare, "SourceBehavior", "Source behavior", 120);
            AddText(_gridCompare, "TargetBehavior", "Target behavior", 120);
            AddText(_gridCompare, "OtherSolutions", "Also in other solutions (not selected)", 220);
            AddText(_gridCompare, "ObjectId", "Object id", 250);
            AddText(_gridCompare, "ComponentType", "Type code", 70);
            _gridCompare.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var row = _gridCompare.Rows[e.RowIndex].DataBoundItem as ComparisonRow;
                if (row != null) e.CellStyle.BackColor = ColorFor(row.Status);
            };
            _gridCompare.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) ExecuteMethod(ShowWhereUsed);
            };
            var cmpMenu = new ContextMenuStrip();
            cmpMenu.Items.Add("Where used?", null, (s, e) => ExecuteMethod(ShowWhereUsed));
            cmpMenu.Items.Add("Copy name(s)", null, (s, e) => CopyFromCompareGrid(r => r.Name));
            cmpMenu.Items.Add("Copy object id(s)", null, (s, e) => CopyFromCompareGrid(r => r.ObjectId.ToString()));
            cmpMenu.Items.Add("Copy rows (tab separated)", null, (s, e) => CopyFromCompareGrid(r => string.Join("\t",
                r.StatusText, r.TypeName, r.Name, r.Label, r.ParentTable, r.SourceSolutions, r.TargetSolutions, r.ObjectId.ToString())));
            _gridCompare.ContextMenuStrip = cmpMenu;

            cmpLayout.Controls.Add(statusRow, 0, 0);
            cmpLayout.Controls.Add(searchRow, 0, 1);
            cmpLayout.Controls.Add(_lblSummary, 0, 2);
            cmpLayout.Controls.Add(_gridCompare, 0, 3);
            _tabCompare.Controls.Add(cmpLayout);

            // ---------------- duplicates tab
            _tabDuplicates = new TabPage("Duplicates");
            var dupLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
            dupLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            dupLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            dupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var dupFilterRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            _chkDupMultiTarget = new CheckBox
            {
                Text = "Only components in 2+ TARGET solutions",
                AutoSize = true,
                BackColor = AmberBack,
                Margin = new Padding(0, 4, 10, 0)
            };
            _chkDupMultiTarget.CheckedChanged += (s, e) => ApplyDuplicateFilter();
            dupFilterRow.Controls.Add(_chkDupMultiTarget);
            dupFilterRow.Controls.Add(new Label { Text = "Search:", AutoSize = true, Margin = new Padding(0, 6, 2, 0) });
            _txtDupSearch = new TextBox { Width = 260 };
            _txtDupSearch.TextChanged += (s, e) => RestartFilterTimer();
            dupFilterRow.Controls.Add(_txtDupSearch);

            _lblDupSummary = new Label { AutoSize = true, Padding = new Padding(0, 4, 0, 4) };

            _gridDuplicates = MakeGrid(true);
            AddText(_gridDuplicates, "TypeName", "Component type", 140);
            AddText(_gridDuplicates, "Name", "Name", 230);
            AddText(_gridDuplicates, "Label", "Label", 150);
            AddText(_gridDuplicates, "ParentTable", "Table", 120);
            AddText(_gridDuplicates, "Solutions", "Solutions  ([S] source, [T] target)", 380);
            AddText(_gridDuplicates, "SolutionCount", "Solutions #", 75);
            AddText(_gridDuplicates, "SourceCount", "In sources", 70);
            AddText(_gridDuplicates, "TargetCount", "In targets", 70);
            AddText(_gridDuplicates, "ObjectId", "Object id", 250);
            AddText(_gridDuplicates, "ComponentType", "Type code", 70);
            _gridDuplicates.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var row = _gridDuplicates.Rows[e.RowIndex].DataBoundItem as DuplicateRow;
                if (row != null) e.CellStyle.BackColor = row.TargetCount > 1 ? AmberBack : Color.White;
            };
            _gridDuplicates.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) ExecuteMethod(ShowWhereUsed);
            };

            dupLayout.Controls.Add(dupFilterRow, 0, 0);
            dupLayout.Controls.Add(_lblDupSummary, 0, 1);
            dupLayout.Controls.Add(_gridDuplicates, 0, 2);
            _tabDuplicates.Controls.Add(dupLayout);

            _tabs.TabPages.Add(_tabCompare);
            _tabs.TabPages.Add(_tabDuplicates);
            return _tabs;
        }

        private Control BuildWhereUsedPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _lblWhereUsed = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Padding = new Padding(0, 2, 0, 4),
                Text = "Where used: select a component and click \"Where used?\" (or double-click it)."
            };

            _gridWhereUsed = MakeGrid(false);
            AddText(_gridWhereUsed, "Role", "Role", 70);
            AddText(_gridWhereUsed, "UniqueName", "Unique name", 220);
            AddText(_gridWhereUsed, "DisplayName", "Display name", 220);
            AddText(_gridWhereUsed, "Version", "Version", 90);
            _gridWhereUsed.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Managed", HeaderText = "Managed", Width = 62, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic });
            AddText(_gridWhereUsed, "Behavior", "Behavior", 140);
            AddText(_gridWhereUsed, "Publisher", "Publisher", 160);
            _gridWhereUsed.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var row = _gridWhereUsed.Rows[e.RowIndex].DataBoundItem as WhereUsedRow;
                if (row == null) return;
                e.CellStyle.BackColor = row.Role == "Source" ? RedBack : row.Role == "Target" ? GreenBack : Color.White;
            };

            layout.Controls.Add(_lblWhereUsed, 0, 0);
            layout.Controls.Add(_gridWhereUsed, 0, 1);
            return layout;
        }

        private void AddStatusFilter(FlowLayoutPanel panel, ComparisonStatus status, string text, Color color, string tooltip)
        {
            var box = new CheckBox
            {
                Text = text,
                AutoSize = true,
                Checked = true,
                BackColor = color,
                Padding = new Padding(3, 1, 3, 1),
                Margin = new Padding(0, 3, 6, 0)
            };
            new ToolTip().SetToolTip(box, tooltip);
            box.CheckedChanged += (s, e) => ApplyCompareFilter();
            _statusFilters[status] = box;
            panel.Controls.Add(box);
        }

        private void SetStatusFilters(params ComparisonStatus[] visible)
        {
            foreach (var kv in _statusFilters)
            {
                kv.Value.Checked = visible.Contains(kv.Key);
            }
            ApplyCompareFilter();
        }

        private void RestartFilterTimer()
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        }

        private static Button MakeButton(string text, Color back, EventHandler click)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                BackColor = back,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(4, 0, 4, 0)
            };
            button.FlatAppearance.BorderColor = Color.Silver;
            button.Click += click;
            return button;
        }

        private static DataGridView MakeGrid(bool multiSelect)
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                MultiSelect = multiSelect,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithAutoHeaderText,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            grid.RowTemplate.Height = 22;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 122, 183);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DataError += (s, e) => { e.ThrowException = false; };

            // Double buffering makes large grids scroll smoothly.
            var property = typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            if (property != null) property.SetValue(grid, true, null);
            return grid;
        }

        private static void AddText(DataGridView grid, string property, string header, int width)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = property,
                HeaderText = header,
                Width = width,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Automatic
            });
        }

        private static void AddCheck(DataGridView grid, string property, string header, int width)
        {
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                DataPropertyName = property,
                HeaderText = header,
                Width = width,
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.Automatic
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _filterTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
