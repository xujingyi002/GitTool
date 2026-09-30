namespace GitTool;

/// <summary>程序入口：启动 Git 仓库管理工具主窗体。</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
