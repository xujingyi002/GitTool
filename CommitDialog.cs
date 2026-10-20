using System.Drawing;
using System.Windows.Forms;

namespace GitTool;

/// <summary>
/// 提交对话框：改动列表独立滚动，提交说明栏固定高度始终可见可编辑。
/// 替代 Interaction.InputBox —— 后者把整段改动拼进提示文本，改动一多输入框就被挤出可视区。
/// 布局用 TableLayoutPanel：列表行按百分比吸收剩余空间，备注行与按钮行用绝对高度保底。
/// </summary>
internal sealed class CommitDialog : Form
{
    private readonly TextBox _txtChanges;
    private readonly TextBox _txtMessage;

    /// <summary>用户填写的提交说明（已去首尾空白）</summary>
    public string Message => _txtMessage.Text.Trim();

    public CommitDialog(string repoName, string branch, string status)
    {
        var lines = status.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int lineCount = lines.Length;

        Text = $"提交当前改动（将自动推送到远程）— {branch}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("微软雅黑", 9.5F);
        BackColor = Color.White;

        // ---- 尺寸：按改动行数自适应，且不超过屏幕工作区的 82%，小屏也不会超界 ----
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int listHeight = Math.Clamp(lineCount * 18 + 12, 96, 240);
        const int chrome = 280;                       // 提示行 + 备注区 + 按钮区 + 内边距 + 标题栏
        int maxW = Math.Min(760, Math.Max(440, wa.Width - 80));
        int maxH = Math.Max(380, (int)(wa.Height * 0.82));
        int width = Math.Min(720, maxW);
        int height = Math.Clamp(listHeight + chrome, 360, maxH);
        ClientSize = new Size(width, height);
        MinimumSize = new Size(Math.Min(480, maxW), Math.Min(380, maxH));

        // ---- 布局 ----
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 12, 14, 10),
            BackColor = Color.White
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));      // 顶部提示
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // 改动列表：吸收剩余空间并滚动
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));     // 备注区：固定高度，始终可见
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));      // 按钮区

        var tip = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(60, 70, 80),
            Text = $"仓库：{repoName}　｜　分支：{branch}　｜　本次将提交 {lineCount} 项改动\r\n" +
                   "下方列表可滚动查看；填写「提交说明」后点「提交并推送」。"
        };
        root.Controls.Add(tip, 0, 0);

        _txtChanges = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            BackColor = Color.FromArgb(248, 249, 250),
            Font = new Font("Consolas", 9F),
            Text = string.Join("\r\n", lines)
        };
        root.Controls.Add(_txtChanges, 0, 1);

        // 备注区：标签 + 输入框，整块固定 112px，不会被上方列表挤压
        var msgPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 6, 0, 4),
            BackColor = Color.White
        };
        msgPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        msgPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        msgPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var msgLabel = new Label
        {
            Text = "提交说明（第一行写概要，Ctrl+Enter 提交）：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        _txtMessage = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "说明本次改动"
        };
        msgPanel.Controls.Add(msgLabel, 0, 0);
        msgPanel.Controls.Add(_txtMessage, 0, 1);
        root.Controls.Add(msgPanel, 0, 2);

        // 按钮区（右下对齐）
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 0),
            Padding = new Padding(0, 8, 0, 0),
            BackColor = Color.White
        };
        var btnOk = new Button
        {
            Text = "提交并推送",
            DialogResult = DialogResult.OK,
            Size = new Size(112, 32),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(8, 0, 0, 0)
        };
        btnOk.FlatAppearance.BorderSize = 0;
        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(84, 32),
            FlatStyle = FlatStyle.Flat
        };
        buttons.Controls.Add(btnOk);
        buttons.Controls.Add(btnCancel);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // Ctrl+Enter 直接提交（Esc 由 CancelButton 处理）
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (!string.IsNullOrWhiteSpace(Message)) DialogResult = DialogResult.OK;
            }
        };

        // 提交说明为空时不允许关闭（避免空提交信息）
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(Message))
            {
                e.Cancel = true;
                MessageBox.Show(this, "请先填写提交说明。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtMessage.Focus();
            }
        };

        // 打开即聚焦备注框并全选默认文案，直接输入即可覆盖
        Shown += (_, _) =>
        {
            _txtMessage.Focus();
            _txtMessage.SelectAll();
        };
    }
}
