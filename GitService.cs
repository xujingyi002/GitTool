using System.Diagnostics;
using System.Text;

namespace GitTool;

/// <summary>一条提交记录</summary>
public class GitCommit
{
    /// <summary>短哈希（7 位）</summary>
    public string ShortHash { get; init; } = "";

    /// <summary>完整哈希</summary>
    public string Hash { get; init; } = "";

    public string Author { get; init; } = "";

    /// <summary>提交时间（yyyy-MM-dd HH:mm）</summary>
    public string Date { get; init; } = "";

    /// <summary>提交说明（首行）</summary>
    public string Subject { get; init; } = "";

    /// <summary>列表展示文本：供界面直接绑定</summary>
    public string Display => $"{ShortHash}　{Date}　{Author}　{Subject}";
}

/// <summary>git 命令执行结果</summary>
public class GitResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string Error { get; init; } = "";

    /// <summary>命令是否成功</summary>
    public bool Ok => ExitCode == 0;

    /// <summary>合并输出（成功用 stdout，失败附带 stderr）</summary>
    public string Text => Ok ? Output : (string.IsNullOrWhiteSpace(Error) ? Output : Output + Environment.NewLine + Error);
}

/// <summary>
/// git 命令封装层：只负责"拼命令 + 跑进程 + 取输出"，不包含界面逻辑。
/// 所有危险操作（reset --hard / clean / discard）都提供独立方法，便于调用方加二次确认。
/// </summary>
public static class GitService
{
    /// <summary>调用 git 并返回结果（工作目录 = 仓库根）</summary>
    public static GitResult Run(string repoPath, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // git 在 Windows 下按 UTF-8 输出，显式指定编码避免中文乱码
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return new GitResult { ExitCode = -1, Error = "无法启动 git 进程（请确认 git 已安装并在 PATH 中）。" };
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(60_000);
            return new GitResult { ExitCode = p.HasExited ? p.ExitCode : -1, Output = stdout.TrimEnd(), Error = stderr.TrimEnd() };
        }
        catch (Exception ex)
        {
            return new GitResult { ExitCode = -1, Error = $"执行失败：{ex.Message}" };
        }
    }

    /// <summary>该目录是否是 git 仓库</summary>
    public static bool IsRepo(string path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && Run(path, "rev-parse", "--is-inside-work-tree").Output.Trim() == "true";

    /// <summary>当前分支名（detached HEAD 时返回短哈希）</summary>
    public static string CurrentBranch(string repo)
    {
        var r = Run(repo, "rev-parse", "--abbrev-ref", "HEAD");
        return r.Ok ? r.Output.Trim() : "（未知）";
    }

    /// <summary>本地分支列表（当前分支前缀 "* "）</summary>
    public static List<string> Branches(string repo)
    {
        var r = Run(repo, "branch", "--format=%(refname:short)");
        if (!r.Ok) return new List<string>();
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
    }

    /// <summary>提交历史（默认最近 200 条，最新在前）</summary>
    public static List<GitCommit> Log(string repo, int max = 200)
    {
        // 用 0x1f（单元分隔符）分隔字段，避免提交说明中出现 "|" 时解析错位
        var r = Run(repo, "log", $"-n{max}", "--pretty=format:%H%x1f%h%x1f%an%x1f%ad%x1f%s", "--date=format:%Y-%m-%d %H:%M");
        var list = new List<GitCommit>();
        if (!r.Ok) return list;

        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\u001f');
            if (parts.Length < 5) continue;
            list.Add(new GitCommit
            {
                Hash = parts[0],
                ShortHash = parts[1],
                Author = parts[2],
                Date = parts[3],
                Subject = parts[4]
            });
        }
        return list;
    }

    /// <summary>工作区状态（porcelain 简短格式；空 = 干净）</summary>
    public static string Status(string repo) => Run(repo, "status", "--porcelain").Output;

    /// <summary>工作区状态的可读摘要</summary>
    public static string StatusSummary(string repo)
    {
        var raw = Status(repo);
        if (string.IsNullOrWhiteSpace(raw)) return "工作区干净（无未提交改动）";
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var untracked = lines.Count(l => l.StartsWith("??"));
        var modified = lines.Length - untracked;
        return $"有 {lines.Length} 项未提交改动（已跟踪修改 {modified} 项 / 未跟踪 {untracked} 项）";
    }

    /// <summary>某次提交的详情（作者/时间/说明/变更文件统计）</summary>
    public static string Show(string repo, string hash) =>
        Run(repo, "show", "--stat", "--date=format:%Y-%m-%d %H:%M", hash).Text;

    /// <summary>某次提交的完整差异（含文件内容改动）</summary>
    public static string ShowDiff(string repo, string hash, bool statOnly = false) =>
        Run(repo, "show", statOnly ? "--stat" : "--patch", "--date=format:%Y-%m-%d %H:%M", hash).Text;

    /// <summary>提交全部改动（git add -A + commit）</summary>
    public static GitResult CommitAll(string repo, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return new GitResult { ExitCode = -1, Error = "提交说明不能为空。" };
        var add = Run(repo, "add", "-A");
        if (!add.Ok) return add;
        return Run(repo, "commit", "-m", message.Trim());
    }

    /// <summary>撤销某次提交（生成一次反向提交，安全：不改写历史）</summary>
    public static GitResult RevertCommit(string repo, string hash) => Run(repo, "revert", "--no-edit", hash);

    /// <summary>回到某次提交：保留之后的改动到工作区（reset --soft）</summary>
    public static GitResult ResetSoft(string repo, string hash) => Run(repo, "reset", "--soft", hash);

    /// <summary>回到某次提交：丢弃之后的全部改动（reset --hard，高危）</summary>
    public static GitResult ResetHard(string repo, string hash) => Run(repo, "reset", "--hard", hash);

    /// <summary>丢弃所有未提交改动（checkout -- . ；includeUntracked=true 时同时清理未跟踪文件）</summary>
    public static GitResult DiscardChanges(string repo, bool includeUntracked)
    {
        var checkout = Run(repo, "checkout", "--", ".");
        if (!checkout.Ok) return checkout;
        if (!includeUntracked) return checkout;
        return Run(repo, "clean", "-fd");
    }

    /// <summary>把当前改动暂存起来（stash）——执行危险操作前的自动/手动备份手段</summary>
    public static GitResult Stash(string repo, string message) =>
        Run(repo, "stash", "push", "-u", "-m", string.IsNullOrWhiteSpace(message) ? "GitTool 自动备份" : message);

    /// <summary>新建分支（可同时切换）</summary>
    public static GitResult CreateBranch(string repo, string name, bool checkout) =>
        checkout ? Run(repo, "checkout", "-b", name) : Run(repo, "branch", name);

    /// <summary>切换分支/提交</summary>
    public static GitResult Checkout(string repo, string target) => Run(repo, "checkout", target);

    /// <summary>打标签（在当前 HEAD 上）</summary>
    public static GitResult Tag(string repo, string name, string message) =>
        string.IsNullOrWhiteSpace(message)
            ? Run(repo, "tag", name)
            : Run(repo, "tag", "-a", name, "-m", message);

    /// <summary>为某次提交打标签（不切换 HEAD）</summary>
    public static GitResult TagCommit(string repo, string hash, string name, string message) =>
        string.IsNullOrWhiteSpace(message)
            ? Run(repo, "tag", name, hash)
            : Run(repo, "tag", "-a", name, "-m", message, hash);

    /// <summary>当前 HEAD 的提交号（用于操作后刷新判断）</summary>
    public static string HeadHash(string repo) => Run(repo, "rev-parse", "--short", "HEAD").Output.Trim();
}
