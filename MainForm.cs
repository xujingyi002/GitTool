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
    private ComboBox _cmbRepo = null!;
    private Label _lblBranch = null!;
    private Label _lblStatus = null!;
    private TabControl _tabs = null!;
    private DataGridView _grid = null!;
    private TextBox _txtDetail = null!;
    private DiffView _diffView = null!;
    private TextBox _txtLog = null!;
    private Button _btnCommit = null!;
    private Button _btnDiscard = null!;
    private Button _btnRevert = null!;
    private Button _btnResetSoft = null!;
    private Button _btnResetHard = null!;
    private Button _btnNewBranch = null!;
    private Button _btnCheckout = null!;
    private Button _btnTag = null!;
    private Button _btnPull = null!;
    private Button _btnFetchBranch = null!;
    private Button _btnDiff = null!;

    private List<GitCommit> _commits = new();

    /// <summary>最近打开的仓库（最近使用的在最前），与下拉框内容一致</summary>
    private List<string> _history = new();
    private string _repo = "";
    private string _lastRemembered = "";   // 本次会话已记入历史的仓库（避免重复写盘）
    private bool _loading;   // 重填下拉框期间抑制选中事件

    public MainForm()
    {
        Text = "Git 仓库管理工具";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 760);
        Font = new Font("微软雅黑", 9F);

        _history = RepositoryStore.Load();
        _repo = PickInitialRepo();

        BuildUi();
        Shown += (_, _) => RefreshAll();
    }

    /// <summary>启动时默认打开：上次用过的仓库 → 内置默认仓库 → 当前目录</summary>
    private static string PickInitialRepo()
    {
        foreach (var p in RepositoryStore.Load())
        {
            if (Directory.Exists(p)) return p;
        }
        return Directory.Exists(@"E:\agent\MES3") ? @"E:\agent\MES3" : Directory.GetCurrentDirectory();
    }

    // ================= 界面构建 =================

    private void BuildUi()
    {
        // ---- 顶部：仓库与状态 ----
        var top = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.WhiteSmoke, Padding = new Padding(8, 6, 8, 4) };

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
        row1.Controls.Add(Label("仓库路径："));
        _cmbRepo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,          // 可直接输入/粘贴路径，也可从下拉历史中选择
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            Width = 520,
            Font = new Font("Consolas", 9.5F),
            Margin = new Padding(2, 4, 6, 0)
        };
        _cmbRepo.SelectedIndexChanged += CmbRepo_SelectedIndexChanged;
        _cmbRepo.KeyDown += CmbRepo_KeyDown;
        row1.Controls.Add(_cmbRepo);

        var btnBrowse = MakeButton("浏览…", BtnBrowse);
        btnBrowse.Width = 78;
        row1.Controls.Add(btnBrowse);

        var btnRefresh = MakeButton("刷新", (_, _) => RefreshAll());
        btnRefresh.Width = 64;
        row1.Controls.Add(btnRefresh);

        var btnForget = MakeButton("移除记录", BtnForgetRepo, Color.FromArgb(120, 130, 140));
        btnForget.Width = 84;
        row1.Controls.Add(btnForget);

        _lblBranch = new Label { Text = "分支：—", AutoSize = true, Margin = new Padding(14, 8, 0, 0), Font = new Font("微软雅黑", 9.5F, FontStyle.Bold), ForeColor = Color.FromArgb(15, 76, 129) };
        row1.Controls.Add(_lblBranch);

        _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 26, Text = "—", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(40, 50, 60) };
        top.Controls.Add(_lblStatus);
        top.Controls.Add(row1);

        // 填入历史仓库列表：下次启动可直接从下拉框选择，无需再翻文件夹
        RefreshRepoCombo(_repo);

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
            Height = 116,
            WrapContents = true,
            Padding = new Padding(8, 6, 8, 4),
            BackColor = Color.WhiteSmoke
        };
        _btnCommit = MakeButton("提交并推送", BtnCommit, Color.FromArgb(15, 76, 129));
        _btnDiscard = MakeButton("丢弃未提交改动", BtnDiscard, Color.FromArgb(192, 0, 0));
        _btnRevert = MakeButton("撤销此提交（安全）", BtnRevert, Color.FromArgb(142, 68, 173));
        _btnResetSoft = MakeButton("回到此提交（保留改动）", BtnResetSoft, Color.FromArgb(230, 126, 34));
        _btnResetHard = MakeButton("回到此提交（丢弃改动）", BtnResetHard, Color.FromArgb(192, 0, 0));
        _btnNewBranch = MakeButton("新建分支", BtnNewBranch);
        _btnCheckout = MakeButton("切换分支/提交", BtnCheckout);
        _btnTag = MakeButton("打标签", BtnTag);
        _btnPull = MakeButton("拉取当前分支", BtnPull, Color.FromArgb(39, 174, 96));
        _btnFetchBranch = MakeButton("获取远程分支", BtnFetchBranch, Color.FromArgb(41, 128, 185));
        _btnDiff = MakeButton("查看提交的改动", BtnViewCommitDiff, Color.FromArgb(41, 128, 185));
        actions.Controls.Add(_btnCommit);
        actions.Controls.Add(_btnRevert);
        actions.Controls.Add(_btnResetSoft);
        actions.Controls.Add(_btnResetHard);
        actions.Controls.Add(_btnDiscard);
        actions.Controls.Add(_btnPull);
        actions.Controls.Add(_btnFetchBranch);
        actions.Controls.Add(_btnNewBranch);
        actions.Controls.Add(_btnCheckout);
        actions.Controls.Add(_btnTag);
        actions.Controls.Add(_btnDiff);
        actions.Controls.Add(new Label
        {
            Text = "提示：提交会自动推送到远程；「拉取当前分支」同步最新、「获取远程分支」可在其它终端继续作业。高危操作（红按钮）执行前自动 stash 备份。",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(10, 12, 0, 0)
        });

        // ---- 中部：提交历史 + 详情（页签1） / 变更比对（页签2） ----
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 560,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGroup("提交历史（最新在上；双击某行 → 跳到「变更比对」查看改动）", out _grid));
        split.Panel2.Controls.Add(MakeGroup("提交详情 / 差异", out _txtDetail));
        _txtDetail.Multiline = true;
        _txtDetail.ReadOnly = true;
        _txtDetail.ScrollBars = ScrollBars.Both;
        _txtDetail.WordWrap = false;
        _txtDetail.Font = new Font("Consolas", 9F);
        _txtDetail.BackColor = Color.FromArgb(250, 250, 250);

        var tabHistory = new TabPage("提交历史 / 详情")
        {
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(3)
        };
        tabHistory.Controls.Add(split);

        var tabDiff = new TabPage("变更比对（哪些文件被改了）")
        {
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(3)
        };
        _diffView = new DiffView { Dock = DockStyle.Fill };
        _diffView.Log += AppendLog;
        tabDiff.Controls.Add(_diffView);

        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(tabHistory);
        _tabs.TabPages.Add(tabDiff);

        Controls.Add(_tabs);
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
        _grid.CellDoubleClick -= Grid_CellDoubleClick;
        _grid.CellDoubleClick += Grid_CellDoubleClick;
    }

    // ================= 数据加载 =================

    /// <summary>刷新仓库状态、分支与提交历史（每次操作后都会调用）</summary>
    private void RefreshAll()
    {
        _repo = _cmbRepo.Text.Trim().Trim('"').Trim();
        if (!Directory.Exists(_repo) || !GitService.IsRepo(_repo))
        {
            _lblBranch.Text = "分支：—";
            _lblStatus.Text = "✖ 该目录不是 git 仓库（检查路径，或直接在下拉框中选择历史仓库）";
            _lblStatus.ForeColor = Color.FromArgb(192, 0, 0);
            Text = "Git 仓库管理工具";
            _grid.DataSource = null;
            _txtDetail.Clear();
            _diffView.SetRepo(_repo);
            return;
        }

        RememberRepo(_repo);   // 识别成功 → 记入下拉历史，下次直接选

        var name = Path.GetFileName(_repo.TrimEnd(Path.DirectorySeparatorChar));
        Text = string.IsNullOrEmpty(name) ? $"{_repo} — Git 仓库管理工具" : $"[{name}] Git 仓库管理工具";
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
        _diffView.SetRepo(_repo);
    }

    /// <summary>识别成功后把仓库记入历史（下次可从下拉框直接选）</summary>
    private void RememberRepo(string repo)
    {
        // 本次会话已经记过这个仓库 → 不再重写文件（也避免「移除记录」后立刻被加回来）
        if (string.Equals(_lastRemembered, repo, StringComparison.OrdinalIgnoreCase))
        {
            _cmbRepo.Text = repo;
            return;
        }
        _lastRemembered = repo;

        if (_history.Count > 0 && string.Equals(_history[0], repo, StringComparison.OrdinalIgnoreCase))
        {
            _cmbRepo.Text = repo;
            return;
        }

        _history = RepositoryStore.Add(repo);
        RefreshRepoCombo(repo);
        AppendLog($"✔ 已记住仓库（下次可直接下拉选择）：{repo}");
    }

    /// <summary>重填下拉框内容并保持文本框显示路径</summary>
    private void RefreshRepoCombo(string? keepText)
    {
        _loading = true;
        try
        {
            _cmbRepo.BeginUpdate();
            _cmbRepo.Items.Clear();
            _cmbRepo.Items.AddRange(_history.ToArray());
            _cmbRepo.EndUpdate();

            var text = keepText ?? _cmbRepo.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                _cmbRepo.Text = text;
                _cmbRepo.SelectionStart = _cmbRepo.Text.Length;
                _cmbRepo.SelectionLength = 0;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>从下拉框里选了一个历史仓库 → 立即切换</summary>
    private void CmbRepo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (_cmbRepo.SelectedItem is not string path || path.Length == 0) return;
        if (string.Equals(path, _repo, StringComparison.OrdinalIgnoreCase)) return;
        RefreshAll();
    }

    /// <summary>在路径框里按回车 → 立即加载该仓库</summary>
    private void CmbRepo_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        e.Handled = true;
        RefreshAll();
    }

    /// <summary>把当前路径从历史记录中移除（不影响已打开的仓库）</summary>
    private void BtnForgetRepo(object? sender, EventArgs e)
    {
        var path = _cmbRepo.Text.Trim();
        if (path.Length == 0) return;
        if (!_history.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
        {
            Warn("当前路径不在历史记录里，无需移除。");
            return;
        }

        _history = RepositoryStore.Remove(path);
        RefreshRepoCombo(path);
        AppendLog($"已从下拉列表移除：{path}（记录文件：{RepositoryStore.StorageFile}）");
    }

    private GitCommit? Selected() =>
        _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].DataBoundItem as GitCommit;

    private void Grid_SelectionChanged(object? sender, EventArgs e) => LoadDetail();

    /// <summary>双击提交行 → 直接跳到「变更比对」页看这次提交改了什么</summary>
    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var c = Selected();
        if (c is null) return;
        _tabs.SelectedIndex = 1;
        _diffView.JumpToCommit(c.Hash);
    }

    /// <summary>「查看提交的改动」按钮：内容与双击提交行一致</summary>
    private void BtnViewCommitDiff(object? sender, EventArgs e)
    {
        var c = Selected();
        if (!EnsureRepo() || c is null) { Warn("请先在左侧提交历史中选择一个提交。"); return; }
        _tabs.SelectedIndex = 1;
        _diffView.JumpToCommit(c.Hash);
    }

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
        _cmbRepo.Text = dlg.SelectedPath;
        RefreshAll();
    }

    /// <summary>提交当前全部改动，并立即自动推送到远程仓库（保证本地提交与远程推送一致）</summary>
    private void BtnCommit(object? sender, EventArgs e)
    {
        if (!EnsureRepo()) return;
        var status = GitService.Status(_repo);
        if (string.IsNullOrWhiteSpace(status))
        {
            Warn("工作区没有需要提交的改动。");
            return;
        }

        var n = status.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var msg = Interaction.InputBox(
            "请输入提交说明（第一行写概要）：" + Environment.NewLine + Environment.NewLine + status,
            "提交当前改动（将自动推送到远程）", "说明本次改动");
        if (string.IsNullOrWhiteSpace(msg)) return;

        // 1) 本地提交（先不刷新，等推送完成后统一刷新）
        AppendLog($"──── 提交改动（{n} 项）────");
        var commit = GitService.CommitAll(_repo, msg);
        AppendLog(commit.Ok ? "✔ 本地提交完成" : $"✖ 提交失败（退出码 {commit.ExitCode}）");
        if (!string.IsNullOrWhiteSpace(commit.Text)) AppendLog(commit.Text);
        if (!commit.Ok)
        {
            MessageBox.Show(commit.Text, "提交失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 2) 提交成功 → 立即自动推送（保持本地与远程一致）
        AutoPush(auto: true);
    }

    /// <summary>
    /// 把当前分支推送到远程；首次推送自动 --set-upstream（让本地跟踪远程）。
    /// 无远程时：本地提交仍保留，仅提示未推送（绝不回滚本地提交）。
    /// 推送失败（如远程领先需先 pull）：本地提交保留并提示用户先拉取。
    /// </summary>
    private void AutoPush(bool auto)
    {
        var branch = GitService.CurrentBranch(_repo);
        if (!GitService.HasRemote(_repo))
        {
            AppendLog("ⓘ 当前仓库未配置远程仓库（origin），本次提交仅保存在本地，未推送到远程。");
            Warn("改动已提交到本地，但当前仓库没有配置远程仓库（origin），因此未推送到远程。\n如需同步，请先用命令行添加远程：git remote add origin <仓库URL>。");
            RefreshAll();
            return;
        }

        var upstream = GitService.Upstream(_repo, branch);
        var setUp = string.IsNullOrWhiteSpace(upstream);
        AppendLog($"──── {(auto ? "自动" : "")}推送到远程：{branch}{(setUp ? "（首次，自动设置 upstream）" : "")}────");
        var push = GitService.Push(_repo, setUp);
        AppendLog(push.Ok ? $"✔ 已推送到远程：{branch}" : $"✖ 推送失败（退出码 {push.ExitCode}）");
        if (!string.IsNullOrWhiteSpace(push.Text)) AppendLog(push.Text);

        if (!push.Ok)
        {
            // 本地提交已成功保留，不回滚；提示用户先拉取解决分叉
            AppendLog("⚠ 本地提交已保留，但推送被拒绝。最常见原因是远程已有新提交（与本地分叉），请先「拉取当前分支」合并后再试。");
            MessageBox.Show(push.Text + "\n\n常见原因：远程已有新提交（与本地分叉）。\n请先点「拉取当前分支」合并，再提交/推送。本地提交不会丢失。",
                "推送失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            AppendLog("✔ 本地与远程已同步（本地提交 = 远程提交）。");
        }
        RefreshAll();
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

    /// <summary>拉取当前分支的远程更新（fetch + pull），用于在其它终端同步最新代码</summary>
    private void BtnPull(object? sender, EventArgs e)
    {
        if (!EnsureRepo()) return;
        if (!GitService.HasRemote(_repo)) { Warn("当前仓库没有配置远程仓库（origin），无法拉取。"); return; }

        // 先获取远程全部更新，再拉取当前分支（确保拿到最新）
        RunGit("获取远程更新（fetch）", () => GitService.Fetch(_repo, "origin"), refresh: false);
        RunGit("拉取当前分支（pull）", () => GitService.Pull(_repo));
    }

    /// <summary>从远程获取某个分支并在本地创建/切换到同名跟踪分支（便于在其它终端继续作业）</summary>
    private void BtnFetchBranch(object? sender, EventArgs e)
    {
        if (!EnsureRepo()) return;
        if (!GitService.HasRemote(_repo)) { Warn("当前仓库没有配置远程仓库（origin），无法获取远程分支。"); return; }

        // 先 fetch，确保拿到最新远程分支信息
        RunGit("获取远程更新（fetch）", () => GitService.Fetch(_repo, "origin"), refresh: false);

        var remotes = GitService.RemoteBranches(_repo, "origin");
        if (remotes.Count == 0)
        {
            Warn("远程暂无可拉取的分支。");
            return;
        }

        var pick = Interaction.InputBox(
            "请输入要获取并切换的远程分支名（origin/ 开头）：" + Environment.NewLine + Environment.NewLine +
            string.Join("\n", remotes),
            "获取远程分支", remotes.FirstOrDefault() ?? "origin/main");
        if (string.IsNullOrWhiteSpace(pick)) return;
        pick = pick.Trim();
        if (!remotes.Contains(pick))
        {
            Warn($"输入的远程分支不存在：{pick}\n可用分支：\n" + string.Join("\n", remotes));
            return;
        }

        RunGit($"获取并切换到 {pick}", () => GitService.FetchAndCheckout(_repo, pick));
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
