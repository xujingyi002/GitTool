using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Microsoft.VisualBasic;

namespace GitTool;

/// <summary>
/// Git 仓库管理工具主窗体：
/// 左侧提交历史、右侧提交详情/diff、底部操作区（提交、回滚三档、分支、标签）与执行日志。
/// 设计原则：**危险操作必须二次确认，且执行前自动 stash 备份**，避免误操作丢代码。
/// </summary>
public class MainForm : Form
{
    private TextBox _txtRepo = null!;
    private Label _lblBranch = null!;
    private Label _lblStatus = null!;
    private DataGridView _grid = null!;
    private TextBox _txtDetail = null!;
    private TextBox _txtLog = null!;
    private Button _btnCommit = null!;
    private Button _btnDiscard = null!;
    private Button _btnRevert = null!;
    private Button _btnResetSoft = null!;
    private Button _btnResetHard = null!;
    private Button _btnNewBranch = null!;
    private Button _btnCheckout = null!;
    private Button _btnTag = null!;

    private List<GitCommit> _commits = new();
    private string _repo = "";

    public MainForm()
    {
        Text = "Git 仓库管理工具";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 760);
        Font = new Font("微软雅黑", 9F);

        _repo = Directory.Exists(@"E:\agent\MES3") ? @"E:\agent\MES3" : Directory.GetCurrentDirectory();

        BuildUi();
        Shown += (_, _) => RefreshAll();
    }

    // ================= 界面构建 =================

    private void BuildUi()
    {
        // ---- 顶部：仓库与状态 ----
        var top = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.WhiteSmoke, Padding = new Padding(8, 6, 8, 4) };

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
        row1.Controls.Add(Label("仓库路径："));
        _txtRepo = new TextBox { Width = 620, Text = _repo, Font = new Font("Consolas", 9.5F), Margin = new Padding(2, 4, 6, 0) };
        row1.Controls.Add(_txtRepo);
        row1.Controls.Add(MakeButton("浏览…", BtnBrowse));
        row1.Controls.Add(MakeButton("刷新", (_, _) => RefreshAll()));
        _lblBranch = new Label { Text = "分支：—", AutoSize = true, Margin = new Padding(14, 8, 0, 0), Font = new Font("微软雅黑", 9.5F, FontStyle.Bold), ForeColor = Color.FromArgb(15, 76, 129) };
        row1.Controls.Add(_lblBranch);

        _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 26, Text = "—", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(40, 50, 60) };
        top.Controls.Add(_lblStatus);
        top.Controls.Add(row1);

        // ---- 底部：日志 ----
        _txtLog = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 150,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(250, 250, 250)
        };

        // ---- 底部：操作按钮 ----
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 88,
            WrapContents = true,
            Padding = new Padding(8, 6, 8, 4),
            BackColor = Color.WhiteSmoke
        };
        _btnCommit = MakeButton("提交当前改动", BtnCommit, Color.FromArgb(15, 76, 129));
        _btnDiscard = MakeButton("丢弃未提交改动", BtnDiscard, Color.FromArgb(192, 0, 0));
        _btnRevert = MakeButton("撤销此提交（安全）", BtnRevert, Color.FromArgb(142, 68, 173));
        _btnResetSoft = MakeButton("回到此提交（保留改动）", BtnResetSoft, Color.FromArgb(230, 126, 34));
        _btnResetHard = MakeButton("回到此提交（丢弃改动）", BtnResetHard, Color.FromArgb(192, 0, 0));
        _btnNewBranch = MakeButton("新建分支", BtnNewBranch);
        _btnCheckout = MakeButton("切换分支/提交", BtnCheckout);
        _btnTag = MakeButton("打标签", BtnTag);
        actions.Controls.Add(_btnCommit);
        actions.Controls.Add(_btnRevert);
        actions.Controls.Add(_btnResetSoft);
        actions.Controls.Add(_btnResetHard);
        actions.Controls.Add(_btnDiscard);
        actions.Controls.Add(_btnNewBranch);
        actions.Controls.Add(_btnCheckout);
        actions.Controls.Add(_btnTag);
        actions.Controls.Add(new Label
        {
            Text = "提示：高危操作（红按钮）执行前会自动 stash 备份当前改动，可在日志中看到恢复命令",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(10, 12, 0, 0)
        });

        // ---- 中部：提交历史 + 详情 ----
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 560,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGroup("提交历史（最新在上；选中一行 → 右侧查看详情与差异）", out _grid));
        split.Panel2.Controls.Add(MakeGroup("提交详情 / 差异", out _txtDetail));
        _txtDetail.Multiline = true;
        _txtDetail.ReadOnly = true;
        _txtDetail.ScrollBars = ScrollBars.Both;
        _txtDetail.WordWrap = false;
        _txtDetail.Font = new Font("Consolas", 9F);
        _txtDetail.BackColor = Color.FromArgb(250, 250, 250);

        Controls.Add(split);
        Controls.Add(actions);
        Controls.Add(_txtLog);
        Controls.Add(top);

        SetupGrid();
    }

    private static Label Label(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 8, 0, 0)
    };

    private static Button MakeButton(string text, EventHandler onClick, Color? backColor = null)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(150, 32),
            Margin = new Padding(4, 4, 4, 0),
            BackColor = backColor ?? Color.FromArgb(90, 100, 110),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        b.Click += onClick;
        return b;
    }

    private static Control MakeGroup(string title, out DataGridView grid)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6, 12, 6, 6) };
        grid = new DataGridView
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
        grid.RowTemplate.Height = 26;
        box.Controls.Add(grid);
        return box;
    }

    private static Control MakeGroup(string title, out TextBox textBox)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6, 12, 6, 6) };
        textBox = new TextBox { Dock = DockStyle.Fill };
        box.Controls.Add(textBox);
        return box;
    }

    private void SetupGrid()
    {
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ShortHash", HeaderText = "提交号", DataPropertyName = "ShortHash", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "时间", DataPropertyName = "Date", FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Author", HeaderText = "作者", DataPropertyName = "Author", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Subject", HeaderText = "说明", DataPropertyName = "Subject", FillWeight = 60 });
        _grid.SelectionChanged -= Grid_SelectionChanged;
        _grid.SelectionChanged += Grid_SelectionChanged;
    }

    // ================= 数据加载 =================

    /// <summary>刷新仓库状态、分支与提交历史（每次操作后都会调用）</summary>
    private void RefreshAll()
    {
        _repo = _txtRepo.Text.Trim();
        if (!Directory.Exists(_repo) || !GitService.IsRepo(_repo))
        {
            _lblBranch.Text = "分支：—";
            _lblStatus.Text = "✖ 该目录不是 git 仓库（请检查路径）";
            _lblStatus.ForeColor = Color.FromArgb(192, 0, 0);
            _grid.DataSource = null;
            _txtDetail.Clear();
            return;
        }

        _lblBranch.Text = $"分支：{GitService.CurrentBranch(_repo)}";
        _lblStatus.Text = $"仓库状态：{GitService.StatusSummary(_repo)}　｜　HEAD：{GitService.HeadHash(_repo)}";
        _lblStatus.ForeColor = GitService.Status(_repo).Length == 0 ? Color.FromArgb(39, 174, 96) : Color.FromArgb(230, 126, 34);

        _commits = GitService.Log(_repo);
        _grid.DataSource = null;
        _grid.DataSource = _commits;
        if (_grid.Rows.Count > 0)
        {
            _grid.ClearSelection();
            _grid.Rows[0].Selected = true;
        }
        LoadDetail();
    }

    private GitCommit? Selected() =>
        _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].DataBoundItem as GitCommit;

    private void Grid_SelectionChanged(object? sender, EventArgs e) => LoadDetail();

    /// <summary>加载选中提交的详情（含变更文件统计）</summary>
    private void LoadDetail()
    {
        var c = Selected();
        if (c is null || !GitService.IsRepo(_repo))
        {
            _txtDetail.Clear();
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"提交：{c.Hash}");
        sb.AppendLine($"作者：{c.Author}    时间：{c.Date}");
        sb.AppendLine($"说明：{c.Subject}");
        sb.AppendLine(new string('-', 80));
        sb.AppendLine(GitService.Show(_repo, c.Hash));
        _txtDetail.Text = sb.ToString();
    }

    // ================= 操作实现 =================

    private void BtnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择 git 仓库目录", SelectedPath = _repo };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _txtRepo.Text = dlg.SelectedPath;
        RefreshAll();
    }

    /// <summary>提交当前全部改动</summary>
    private void BtnCommit(object? sender, EventArgs e)
    {
        if (!EnsureRepo()) return;
        var status = GitService.Status(_repo);
        if (string.IsNullOrWhiteSpace(status))
        {
            Warn("工作区没有需要提交的改动。");
            return;
        }

        var msg = Interaction.InputBox(
            "请输入提交说明（第一行写概要）：" + Environment.NewLine + Environment.NewLine + status,
            "提交当前改动", "说明本次改动");
        if (string.IsNullOrWhiteSpace(msg)) return;

        RunGit($"提交改动（{status.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} 项）",
            () => GitService.CommitAll(_repo, msg));
    }

    /// <summary>撤销某次提交：生成反向提交（不改写历史，最安全）</summary>
    private void BtnRevert(object? sender, EventArgs e)
    {
        var c = Selected();
        if (!EnsureRepo() || c is null) { Warn("请先在左侧选择一个提交。"); return; }
        if (!Confirm($"撤销提交 {c.ShortHash}？\n\n说明：{c.Subject}\n\n将生成一次「反向提交」抵销该次改动（历史保留，安全）。")) return;

        RunGit($"撤销提交 {c.ShortHash}", () => GitService.RevertCommit(_repo, c.Hash));
    }

    /// <summary>回到某次提交：之后的改动保留到工作区（可再决定如何处理）</summary>
    private void BtnResetSoft(object? sender, EventArgs e)
    {
        var c = Selected();
        if (!EnsureRepo() || c is null) { Warn("请先在左侧选择一个提交。"); return; }
        if (!Confirm($"回到提交 {c.ShortHash}？\n\n说明：{c.Subject}\n\n该提交之后的改动会**保留在工作区**（不会丢失），你可以重新提交或丢弃。")) return;

        RunGit($"soft reset → {c.ShortHash}", () => GitService.ResetSoft(_repo, c.Hash));
    }

    /// <summary>回到某次提交：丢弃之后的全部改动（高危，执行前自动 stash 备份）</summary>
    private void BtnResetHard(object? sender, EventArgs e)
    {
        var c = Selected();
        if (!EnsureRepo() || c is null) { Warn("请先在左侧选择一个提交。"); return; }

        var ahead = Math.Max(0, _commits.FindIndex(x => x.Hash == c.Hash));
        if (!Confirm($"⚠ 高危操作：回到提交 {c.ShortHash} 并**丢弃之后的全部改动**。\n\n" +
                     $"说明：{c.Subject}\n" +
                     $"将丢弃的提交数：约 {ahead} 次\n\n" +
                     "执行前会自动把当前未提交改动 stash 备份（不会立刻永久丢失）。\n\n确认继续？")) return;

        AutoStashBeforeDanger("hard reset 前自动备份");
        RunGit($"hard reset → {c.ShortHash}", () => GitService.ResetHard(_repo, c.Hash));
    }

    /// <summary>丢弃未提交改动（高危，执行前自动 stash 备份）</summary>
    private void BtnDiscard(object? sender, EventArgs e)
    {
        if (!EnsureRepo()) return;
        if (string.IsNullOrWhiteSpace(GitService.Status(_repo))) { Warn("工作区本来就是干净的，无需丢弃。"); return; }

        if (!Confirm("⚠ 高危操作：丢弃当前**全部未提交改动**（含未跟踪文件）。\n\n" +
                     "执行前会自动 stash 备份，可在日志中看到恢复命令。\n\n确认继续？")) return;

        AutoStashBeforeDanger("丢弃改动前自动备份");
        RunGit("丢弃未提交改动", () => GitService.DiscardChanges(_repo, true));
    }

    /// <summary>新建分支（可同时切换）</summary>
    private void BtnNewBranch(object? sender, EventArgs e)
    {
        if (!EnsureRepo()) return;
        var name = Interaction.InputBox("请输入新分支名（如 feature/xxx）：", "新建分支", $"feature/{DateTime.Now:MMdd-HHmm}");
        if (string.IsNullOrWhiteSpace(name)) return;
        var checkout = Confirm($"是否立即切换到新分支 {name.Trim()}？\n（选「否」则只创建不切换）");
        RunGit($"新建分支 {name.Trim()}", () => GitService.CreateBranch(_repo, name.Trim(), checkout));
    }

    /// <summary>切换分支或提交</summary>
    private void BtnCheckout(object? sender, EventArgs e)
    {
        if (!EnsureRepo()) return;
        var branches = GitService.Branches(_repo);
        var hint = branches.Count == 0 ? "" : "\n\n本地分支：" + string.Join("、", branches);
        var target = Interaction.InputBox("请输入要切换的分支名（或提交号）：" + hint, "切换分支/提交", branches.FirstOrDefault() ?? "main");
        if (string.IsNullOrWhiteSpace(target)) return;
        RunGit($"切换到 {target.Trim()}", () => GitService.Checkout(_repo, target.Trim()));
    }

    /// <summary>给选中提交打标签</summary>
    private void BtnTag(object? sender, EventArgs e)
    {
        var c = Selected();
        if (!EnsureRepo() || c is null) { Warn("请先在左侧选择一个提交。"); return; }
        var name = Interaction.InputBox($"给提交 {c.ShortHash} 打标签，请输入标签名：", "打标签", $"v{DateTime.Now:yyyyMMdd-HHmm}");
        if (string.IsNullOrWhiteSpace(name)) return;
        var msg = Interaction.InputBox("请输入标签说明（可留空）：", "标签说明", c.Subject);
        RunGit($"打标签 {name.Trim()}", () => GitService.TagCommit(_repo, c.Hash, name.Trim(), msg));
    }

    // ================= 辅助 =================

    private bool EnsureRepo()
    {
        if (GitService.IsRepo(_repo)) return true;
        Warn("当前路径不是 git 仓库，请先选择正确的仓库目录。");
        return false;
    }

    /// <summary>危险操作前自动 stash 备份（仅当有未提交改动时）</summary>
    private void AutoStashBeforeDanger(string reason)
    {
        if (string.IsNullOrWhiteSpace(GitService.Status(_repo))) return;
        var r = GitService.Stash(_repo, $"{reason} {DateTime.Now:yyyy-MM-dd HH:mm}");
        AppendLog($"$ git stash push -u -m \"{reason}\"");
        AppendLog(r.Ok
            ? $"    ✔ 已自动备份未提交改动（恢复命令：git stash pop）"
            : $"    ✖ 自动备份失败：{r.Text}");
    }

    /// <summary>统一执行一条 git 操作：写日志 → 执行 → 提示结果 → 刷新界面</summary>
    private void RunGit(string title, Func<GitResult> action, bool refresh = true)
    {
        AppendLog($"──── {title} ────");
        GitResult result;
        try
        {
            result = action();
        }
        catch (Exception ex)
        {
            AppendLog($"✖ 执行异常：{ex.Message}");
            Warn($"执行异常：{ex.Message}");
            return;
        }

        AppendLog(result.Ok ? $"✔ {title} 完成" : $"✖ {title} 失败（退出码 {result.ExitCode}）");
        if (!string.IsNullOrWhiteSpace(result.Text)) AppendLog(result.Text);

        if (result.Ok)
        {
            if (refresh) RefreshAll();
        }
        else
        {
            MessageBox.Show(result.Text, $"{title} 失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AppendLog(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
        _txtLog.AppendText(line + Environment.NewLine);
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    private bool Confirm(string text) =>
        MessageBox.Show(text, "操作确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
