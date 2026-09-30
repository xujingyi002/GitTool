using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace GitTool;

/// <summary>
/// 「变更比对」页：一眼看清哪些文件被改了、改了什么、哪些是新增文件。
/// 支持三种口径：① 工作区未提交改动 vs 最近提交；② 某个提交自身引入的改动；③ 两次提交之间比对。
/// 左侧为变更文件清单（新增 / 修改 / 删除 / 重命名 / 新增·未跟踪），右侧为按行着色的差异内容。
/// </summary>
public class DiffView : UserControl
{
    /// <summary>差异视图最多渲染的行数，超出截断（避免超大差异卡界面）</summary>
    private const int MaxRenderLines = 5000;

    private Panel _top = null!;
    private ComboBox _cmbMode = null!;
    private Label _lblBase = null!;
    private ComboBox _cmbBase = null!;
    private Label _lblTo = null!;
    private ComboBox _cmbTarget = null!;
    private Button _btnCompare = null!;
    private CheckBox _chkAddedOnly = null!;
    private CheckBox _chkIgnoreWs = null!;
    private Label _lblSummary = null!;
    private SplitContainer _split = null!;
    private GroupBox _grpFiles = null!;
    private DataGridView _fileGrid = null!;
    private GroupBox _grpDiff = null!;
    private RichTextBox _diffBox = null!;

    private List<GitCommit> _commits = new();
    private List<FileChange> _all = new();
    private string _repo = "";
    private bool _loading;       // 构建期 / 载入提交列表期间抑制事件
    private bool _binding;       // 绑定表格数据期间抑制选中事件
    private bool _splitReady;    // 分隔条是否已按实际宽度定位

    /// <summary>操作日志（由主窗体写入底部日志区）</summary>
    public event Action<string>? Log;

    public DiffView()
    {
        BackColor = SystemColors.Control;
        BuildUi();
    }

    // ================= 界面构建 =================

    private void BuildUi()
    {
        _top = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(8, 6, 8, 4)
        };

        // 第一行：比对方式 + 基准/对比 + 开始比对
        // 用 TableLayoutPanel 而不是固定宽度的 FlowLayoutPanel：
        // 两个提交下拉按 50% 平分剩余宽度，窗口再窄也不会把「对比框 / 开始比对」挤出可视区被裁掉
        var row1 = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            ColumnCount = 7,
            BackColor = Color.Transparent
        };
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));        // 比对方式：
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));   // 比对方式下拉
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));        // 基准：
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));     // 基准下拉（随窗口伸缩）
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));        // 对比：
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));     // 对比下拉（随窗口伸缩）
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));        // 开始比对
        row1.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _cmbMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 5, 14, 0)
        };
        _cmbMode.Items.AddRange(new object[]
        {
            "工作区改动（未提交）",
            "单个提交的改动",
            "两次提交之间比对"
        });
        _cmbMode.SelectedIndexChanged += (_, _) => OnModeChanged();
        row1.Controls.Add(_cmbMode, 1, 0);

        row1.Controls.Add(LabelOf("比对方式："), 0, 0);

        _lblBase = LabelOf("基准：");
        row1.Controls.Add(_lblBase, 2, 0);
        _cmbBase = MakeCommitCombo();
        row1.Controls.Add(_cmbBase, 3, 0);

        _lblTo = LabelOf("对比：");
        row1.Controls.Add(_lblTo, 4, 0);
        _cmbTarget = MakeCommitCombo();
        row1.Controls.Add(_cmbTarget, 5, 0);

        _btnCompare = new Button
        {
            Text = "开始比对",
            Anchor = AnchorStyles.Left,
            Size = new Size(96, 30),
            Margin = new Padding(12, 3, 4, 0),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnCompare.Click += (_, _) => Compare();
        row1.Controls.Add(_btnCompare, 6, 0);

        // 第二行：过滤开关 + 统计摘要
        var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
        _chkAddedOnly = new CheckBox
        {
            Text = "仅显示新增文件",
            AutoSize = true,
            Margin = new Padding(4, 9, 18, 0)
        };
        _chkAddedOnly.CheckedChanged += (_, _) => ApplyFilter();
        row2.Controls.Add(_chkAddedOnly);

        _chkIgnoreWs = new CheckBox
        {
            Text = "忽略空白改动（-w）",
            AutoSize = true,
            Margin = new Padding(4, 9, 18, 0)
        };
        _chkIgnoreWs.CheckedChanged += (_, _) => Compare();
        row2.Controls.Add(_chkIgnoreWs);

        _lblSummary = new Label
        {
            AutoSize = true,
            Margin = new Padding(4, 10, 0, 0),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 76, 129),
            Text = "—"
        };
        row2.Controls.Add(_lblSummary);

        _top.Controls.Add(row2);
        _top.Controls.Add(row1);

        // 中部：左文件清单 / 右差异内容
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = SystemColors.Control
        };
        _split.Panel1.Controls.Add(BuildFileGridGroup());
        _split.Panel2.Controls.Add(BuildDiffGroup());

        Controls.Add(_split);
        Controls.Add(_top);

        // 控件齐备后再初始化下拉选择，避免事件在字段未就绪时触发
        _loading = true;
        _cmbMode.SelectedIndex = 0;
        _loading = false;
        OnModeChanged();
    }

    private GroupBox BuildFileGridGroup()
    {
        _grpFiles = new GroupBox
        {
            Text = "变更文件（点击任意一行 → 右侧查看该文件的差异）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };

        _fileGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(15, 76, 129),
                ForeColor = Color.White,
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            ColumnHeadersHeight = 30
        };
        _fileGrid.RowTemplate.Height = 26;
        _fileGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Kind",
            HeaderText = "状态",
            DataPropertyName = "KindText",
            FillWeight = 16
        });
        _fileGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FilePath",
            HeaderText = "文件 / 页面",
            DataPropertyName = "FilePath",
            FillWeight = 62
        });
        _fileGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Stat",
            HeaderText = "变更行数",
            DataPropertyName = "StatText",
            FillWeight = 22
        });
        _fileGrid.CellFormatting += Grid_CellFormatting;
        _fileGrid.SelectionChanged += (_, _) => ShowSelectedFilePatch();

        _grpFiles.Controls.Add(_fileGrid);
        return _grpFiles;
    }

    private GroupBox BuildDiffGroup()
    {
        _grpDiff = new GroupBox
        {
            Text = "差异内容",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };
        _diffBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Font = new Font("Consolas", 9F),
            BackColor = Color.White,
            DetectUrls = false
        };
        _grpDiff.Controls.Add(_diffBox);
        return _grpDiff;
    }

    private static Label LabelOf(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 10, 0, 0)
    };

    private static ComboBox MakeCommitCombo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,   // 在表格列内横向撑满
        Margin = new Padding(2, 5, 10, 0),
        // 下拉列表本体比框宽：框内放不下整行提交说明时，展开列表仍能看全后再选
        DropDownWidth = 640
    };

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_splitReady || _split is null || _split.Width < 360) return;
        try
        {
            _split.SplitterDistance = (int)(_split.Width * 0.44);
            _splitReady = true;
        }
        catch (InvalidOperationException)
        {
            // 控件尚未完成布局，下一次尺寸变化时再设置
        }
    }

    // ================= 对外接口 =================

    /// <summary>设置（或切换）要查看的仓库，并重新比对</summary>
    public void SetRepo(string repo)
    {
        _repo = repo ?? "";
        LoadCommits();
        Compare();
    }

    /// <summary>跳到「单个提交的改动」并立即比对（主窗体双击某条提交记录时调用）</summary>
    public void JumpToCommit(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return;
        _loading = true;
        _cmbMode.SelectedIndex = 1;
        SelectHash(_cmbBase, hash);
        _loading = false;
        OnModeChanged();   // 内部会触发一次比对
    }

    /// <summary>重新比对当前口径（主窗体刷新时调用）</summary>
    public void RefreshChanges() => Compare();

    // ================= 交互 =================

    private void OnModeChanged()
    {
        if (_loading) return;
        var mode = _cmbMode.SelectedIndex;
        var showBase = mode != 0;
        var showTarget = mode == 2;

        _lblBase.Visible = showBase;
        _cmbBase.Visible = showBase;
        _cmbBase.Enabled = showBase;
        _lblTo.Visible = showTarget;
        _cmbTarget.Visible = showTarget;
        _cmbTarget.Enabled = showTarget;

        Compare();
    }

    private DiffScope Scope => _cmbMode.SelectedIndex switch
    {
        1 => DiffScope.SingleCommit,
        2 => DiffScope.CommitRange,
        _ => DiffScope.WorkingTree
    };

    private string BaseRevision =>
        Scope == DiffScope.WorkingTree ? "HEAD" : (_cmbBase.SelectedItem as GitCommit)?.Hash ?? "HEAD";

    private string TargetRevision =>
        (_cmbTarget.SelectedItem as GitCommit)?.Hash ?? (_cmbBase.SelectedItem as GitCommit)?.Hash ?? "HEAD";

    /// <summary>载入提交列表（尽量保留用户当前选择的两个提交）</summary>
    private void LoadCommits()
    {
        var oldBase = (_cmbBase.SelectedItem as GitCommit)?.Hash;
        var oldTarget = (_cmbTarget.SelectedItem as GitCommit)?.Hash;

        _commits = GitService.IsRepo(_repo) ? GitService.Log(_repo, 300) : new List<GitCommit>();

        _loading = true;
        _cmbBase.DataSource = null;
        _cmbTarget.DataSource = null;
        _cmbBase.DataSource = new List<GitCommit>(_commits);
        _cmbTarget.DataSource = new List<GitCommit>(_commits);
        // 注意：对 DataSource 赋值会把 DisplayMember 重置为空（WinForms 行为），
        // 导致下拉框显示成类名 "GitTool.GitCommit"。必须在其后重新设置。
        _cmbBase.DisplayMember = "Display";
        _cmbTarget.DisplayMember = "Display";

        if (_commits.Count > 0)
        {
            if (!SelectHash(_cmbBase, oldBase)) _cmbBase.SelectedIndex = Math.Min(1, _commits.Count - 1);
            if (!SelectHash(_cmbTarget, oldTarget)) _cmbTarget.SelectedIndex = 0;
        }
        _loading = false;
    }

    private static bool SelectHash(ComboBox combo, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || combo.DataSource is not List<GitCommit> list) return false;
        var idx = list.FindIndex(c => c.Hash == hash);
        if (idx < 0) return false;
        combo.SelectedIndex = idx;
        return true;
    }

    /// <summary>执行比对：取变更清单 → 过滤展示 → 渲染全部差异</summary>
    private void Compare()
    {
        if (!GitService.IsRepo(_repo))
        {
            _all.Clear();
            _lblSummary.Text = "⚠ 当前路径不是 git 仓库，无法比对";
            _lblSummary.ForeColor = Color.FromArgb(192, 0, 0);
            ApplyFilter();
            RenderDiff("");
            _grpDiff.Text = "差异内容";
            return;
        }

        _all = GitService.Changes(_repo, Scope, BaseRevision, TargetRevision);
        UpdateSummary();
        ApplyFilter();

        _grpDiff.Text = "差异内容 — 全部变更文件";
        RenderDiff(_all.Count == 0 ? "" : GitService.FullPatch(_repo, Scope, BaseRevision, TargetRevision, _chkIgnoreWs.Checked));
        Log?.Invoke($"变更比对［{_cmbMode.Text}］：{_all.Count} 个变更文件");
    }

    /// <summary>刷新统计摘要</summary>
    private void UpdateSummary()
    {
        if (_all.Count == 0)
        {
            _lblSummary.Text = "没有差异：该比对范围内没有文件变更";
            _lblSummary.ForeColor = Color.FromArgb(39, 174, 96);
            return;
        }

        var added = _all.Count(x => x.Kind == ChangeKind.Added);
        var modified = _all.Count(x => x.Kind == ChangeKind.Modified);
        var deleted = _all.Count(x => x.Kind == ChangeKind.Deleted);
        var renamed = _all.Count(x => x.Kind is ChangeKind.Renamed or ChangeKind.Copied);
        var other = _all.Count - added - modified - deleted - renamed;
        var plus = _all.Sum(x => x.Added);
        var minus = _all.Sum(x => x.Removed);

        var sb = new StringBuilder();
        sb.Append($"共 {_all.Count} 个文件：新增 {added} / 修改 {modified} / 删除 {deleted}");
        if (renamed > 0) sb.Append($" / 重命名 {renamed}");
        if (other > 0) sb.Append($" / 其他 {other}");
        sb.Append($"　＋{plus} 行　−{minus} 行");

        _lblSummary.Text = sb.ToString();
        _lblSummary.ForeColor = Color.FromArgb(15, 76, 129);
    }

    /// <summary>按「仅显示新增文件」开关刷新表格</summary>
    private void ApplyFilter()
    {
        var view = _chkAddedOnly.Checked ? _all.Where(x => x.IsAdded).ToList() : new List<FileChange>(_all);

        _binding = true;
        _fileGrid.DataSource = null;
        _fileGrid.DataSource = view;
        _fileGrid.ClearSelection();
        _binding = false;
        _grpFiles.Text = _chkAddedOnly.Checked
            ? $"变更文件 — 仅新增（{view.Count} / {_all.Count}）"
            : $"变更文件（{view.Count} 个；点击任意一行 → 查看该文件差异）";
    }

    private void ShowSelectedFilePatch()
    {
        if (_binding) return;
        if (_fileGrid.CurrentRow?.DataBoundItem is not FileChange file) return;

        var patch = GitService.PatchForFile(_repo, Scope, BaseRevision, TargetRevision, file, _chkIgnoreWs.Checked);
        _grpDiff.Text = $"差异内容 — {file.FilePath}　[{file.KindText}　{file.StatText}]";
        RenderDiff(patch);
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _fileGrid.Rows[e.RowIndex].DataBoundItem is not FileChange f) return;
        if (_fileGrid.Columns[e.ColumnIndex].Name != "Kind") return;

        e.CellStyle.ForeColor = f.Kind switch
        {
            ChangeKind.Added => Color.FromArgb(26, 127, 55),      // 新增：绿
            ChangeKind.Modified => Color.FromArgb(190, 110, 10),  // 修改：橙
            ChangeKind.Deleted => Color.FromArgb(192, 0, 0),      // 删除：红
            ChangeKind.Renamed or ChangeKind.Copied => Color.FromArgb(110, 60, 180),
            _ => Color.FromArgb(70, 80, 90)
        };
    }

    // ================= 差异渲染 =================

    /// <summary>把 unified diff 文本渲染成逐行着色的视图</summary>
    private void RenderDiff(string patch)
    {
        _diffBox.Visible = false;   // 渲染期间隐藏，避免大文本反复重绘
        try
        {
            _diffBox.Clear();

            if (string.IsNullOrWhiteSpace(patch))
            {
                AppendStyled("没有差异：当前比对范围内没有变更内容。\n", Color.Gray, Color.White);
                return;
            }

            var lines = patch.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var limit = Math.Min(lines.Length, MaxRenderLines);
            var fore = new Color[limit];
            var back = new Color[limit];
            var inHunk = false;
            for (var i = 0; i < limit; i++) Classify(lines[i], ref inHunk, out fore[i], out back[i]);

            var idx = 0;
            while (idx < limit)
            {
                var end = idx;
                while (end < limit && fore[end] == fore[idx] && back[end] == back[idx]) end++;

                var sb = new StringBuilder();
                for (var k = idx; k < end; k++) sb.Append(lines[k]).Append('\n');
                AppendStyled(sb.ToString(), fore[idx], back[idx]);
                idx = end;
            }

            if (lines.Length > limit)
            {
                AppendStyled(
                    $"\n……差异过长，仅显示前 {limit} 行（请点击左侧单个文件查看完整差异）\n",
                    Color.FromArgb(150, 80, 0),
                    Color.FromArgb(255, 248, 220));
            }
        }
        finally
        {
            _diffBox.Visible = true;
        }
    }

    /// <summary>逐行判定颜色：文件头 / 差异块头 / 新增行 / 删除行 / 上下文行</summary>
    private static void Classify(string line, ref bool inHunk, out Color fore, out Color back)
    {
        const string addFg = "#1A7F37", delFg = "#CF222E", fileFg = "#0F4C81", metaFg = "#57606A", hunkFg = "#0969DA";

        if (line.StartsWith("diff --git", StringComparison.Ordinal) ||
            line.StartsWith("diff --cc", StringComparison.Ordinal) ||
            line.StartsWith("diff --combined", StringComparison.Ordinal))
        {
            inHunk = false;
            fore = Hex(fileFg);
            back = Color.FromArgb(235, 242, 248);
            return;
        }

        if (!inHunk)
        {
            // 差异块前的元信息（index / 文件头 / 模式 / 重命名 / 二进制提示）
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = true;
                fore = Hex(hunkFg);
                back = Color.FromArgb(221, 244, 255);
                return;
            }
            fore = Hex(metaFg);
            back = Color.White;
            return;
        }

        if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            fore = Hex(hunkFg);
            back = Color.FromArgb(221, 244, 255);
            return;
        }
        if (line.Length > 0 && line[0] == '+')
        {
            fore = Hex(addFg);
            back = Color.FromArgb(230, 255, 236);
            return;
        }
        if (line.Length > 0 && line[0] == '-')
        {
            fore = Hex(delFg);
            back = Color.FromArgb(255, 235, 233);
            return;
        }
        if (line.StartsWith("\\ No newline", StringComparison.Ordinal))
        {
            fore = Color.Gray;
            back = Color.White;
            return;
        }
        fore = Color.FromArgb(36, 41, 47);
        back = Color.White;
    }

    private static Color Hex(string hex) => ColorTranslator.FromHtml(hex);

    private void AppendStyled(string text, Color fore, Color back)
    {
        _diffBox.SelectionStart = _diffBox.TextLength;
        _diffBox.SelectionLength = 0;
        _diffBox.SelectionColor = fore;
        _diffBox.SelectionBackColor = back;
        _diffBox.AppendText(text);
    }
}
