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

    // ================= 变更比对（DiffView 的数据来源） =================

    /// <summary>仓库是否已有提交（没有提交时 HEAD 无法解析，工作区比对要单独处理）</summary>
    public static bool HasCommits(string repo) => Run(repo, "rev-parse", "--verify", "HEAD").Ok;

    /// <summary>
    /// 列出比对范围内的全部变更文件。
    /// scope=WorkingTree 时取「工作区（含未跟踪新文件） vs HEAD」；
    /// scope=CommitRange 时取 a..b；scope=SingleCommit 时取提交 a 自身引入的改动。
    /// </summary>
    public static List<FileChange> Changes(string repo, DiffScope scope, string a = "", string b = "")
    {
        if (!IsRepo(repo)) return new List<FileChange>();

        switch (scope)
        {
            case DiffScope.WorkingTree:
            {
                // 尚无任何提交的新仓库：全部文件都算「未跟踪新增」
                if (!HasCommits(repo)) return UntrackedFiles(repo);
                var ns = Run(repo, "-c", "core.quotepath=false", "diff", "--name-status", "-z", "-M", "HEAD");
                var num = Run(repo, "-c", "core.quotepath=false", "diff", "--numstat", "-z", "-M", "HEAD");
                var list = ParseNameStatus(ns.Output, ParseNumStat(num.Output));
                list.AddRange(UntrackedFiles(repo));
                return list;
            }
            case DiffScope.CommitRange:
            {
                var ns = Run(repo, "-c", "core.quotepath=false", "diff", "--name-status", "-z", "-M", a, b);
                var num = Run(repo, "-c", "core.quotepath=false", "diff", "--numstat", "-z", "-M", a, b);
                return ParseNameStatus(ns.Output, ParseNumStat(num.Output));
            }
            default:
            {
                // git show 兼容「根提交」（git diff <hash>^ 在首次提交上会报错）
                var ns = Run(repo, "-c", "core.quotepath=false", "show", "--format=", "--name-status", "-z", "-M", a);
                var num = Run(repo, "-c", "core.quotepath=false", "show", "--format=", "--numstat", "-z", "-M", a);
                return ParseNameStatus(ns.Output, ParseNumStat(num.Output));
            }
        }
    }

    /// <summary>未跟踪的新文件（git status 中的 "??"，-uall 展开到文件级）</summary>
    public static List<FileChange> UntrackedFiles(string repo)
    {
        var list = new List<FileChange>();
        var r = Run(repo, "-c", "core.quotepath=false", "status", "--porcelain", "-uall");
        if (!r.Ok) return list;

        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("??", StringComparison.Ordinal)) continue;
            var path = line.Length > 3 ? line[3..].Trim() : "";
            if (string.IsNullOrWhiteSpace(path)) continue;

            var (lines, binary) = InspectFile(repo, path);
            list.Add(new FileChange
            {
                Kind = ChangeKind.Added,
                FilePath = path,
                Untracked = true,
                Added = lines,
                Binary = binary
            });
        }
        return list;
    }

    /// <summary>单个文件的差异文本（右侧差异视图的数据源）</summary>
    public static string PatchForFile(string repo, DiffScope scope, string a, string b, FileChange file, bool ignoreWhitespace)
    {
        if (!IsRepo(repo) || file is null) return "";
        // 未跟踪文件 git 没有 diff，按「全部行都是新增」现场合成
        if (file.Untracked) return SynthesizeUntrackedPatch(repo, file.FilePath);

        var args = BuildDiffArgs(scope, a, b, ignoreWhitespace);
        args.Add("--");
        args.Add(file.FilePath);
        // 重命名/复制必须把旧路径一起传给 git，否则路径过滤会让 git 退化成「新文件」
        if (!string.IsNullOrWhiteSpace(file.OldPath) && file.OldPath != file.FilePath)
        {
            args.Add(file.OldPath);
        }
        return Run(repo, args.ToArray()).Text;
    }

    /// <summary>整个比对范围的差异文本（未选中具体文件时的「全部差异」）</summary>
    public static string FullPatch(string repo, DiffScope scope, string a, string b, bool ignoreWhitespace)
    {
        if (!IsRepo(repo)) return "";

        var text = Run(repo, BuildDiffArgs(scope, a, b, ignoreWhitespace).ToArray()).Text;
        if (scope != DiffScope.WorkingTree) return text;

        // 工作区模式补上未跟踪新文件（git diff 不包含它们）
        var sb = new StringBuilder(text);
        foreach (var f in UntrackedFiles(repo))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(SynthesizeUntrackedPatch(repo, f.FilePath));
        }
        return sb.ToString();
    }

    /// <summary>拼装 diff / show 命令的公共参数</summary>
    private static List<string> BuildDiffArgs(DiffScope scope, string a, string b, bool ignoreWhitespace)
    {
        var args = new List<string> { "-c", "core.quotepath=false" };
        switch (scope)
        {
            case DiffScope.WorkingTree:
                args.Add("diff");
                if (ignoreWhitespace) args.Add("-w");
                if (HasCommits(a)) args.Add("HEAD");
                break;
            case DiffScope.CommitRange:
                args.Add("diff");
                args.Add("-M");
                if (ignoreWhitespace) args.Add("-w");
                args.Add(a);
                args.Add(b);
                break;
            default:
                args.Add("show");
                args.Add("--format=");
                args.Add("--patch");
                if (ignoreWhitespace) args.Add("-w");
                args.Add(a);
                break;
        }
        return args;
    }

    /// <summary>把未跟踪文件合成为一段标准 diff 文本，让右侧视图能像普通改动一样逐行展示</summary>
    private static string SynthesizeUntrackedPatch(string repo, string relPath)
    {
        var sb = new StringBuilder();
        sb.Append("diff --git a/").Append(relPath).Append(" b/").Append(relPath).Append('\n');
        sb.Append("new file mode 100644\n");
        sb.Append("--- /dev/null\n");
        sb.Append("+++ b/").Append(relPath).Append('\n');

        var (_, binary) = InspectFile(repo, relPath);
        if (binary)
        {
            sb.Append("（二进制 / 超大文件，不显示文本差异）\n");
            return sb.ToString();
        }

        var text = DecodeText(File.ReadAllBytes(FullPathOf(repo, relPath)))
            .Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        var count = lines.Length;
        if (count > 0 && lines[^1].Length == 0) count--;   // 结尾换行不算一行

        sb.Append("@@ -0,0 +1,").Append(count).Append(" @@\n");
        for (var i = 0; i < count; i++) sb.Append('+').Append(lines[i]).Append('\n');
        return sb.ToString();
    }

    /// <summary>检查文件：文本文件返回行数；二进制或超大文件返回 Binary=true</summary>
    private static (int Lines, bool Binary) InspectFile(string repo, string relPath)
    {
        try
        {
            var full = FullPathOf(repo, relPath);
            var fi = new FileInfo(full);
            if (!fi.Exists) return (0, true);
            if (fi.Length > 2 * 1024 * 1024) return (0, true);

            var bytes = File.ReadAllBytes(full);
            var probe = Math.Min(bytes.Length, 8192);
            for (var i = 0; i < probe; i++) if (bytes[i] == 0) return (0, true);

            var text = DecodeText(bytes);
            if (text.Length == 0) return (0, false);
            var lines = text.Split('\n').Length;
            if (text.EndsWith('\n')) lines--;
            return (lines, false);
        }
        catch
        {
            return (0, true);
        }
    }

    private static string FullPathOf(string repo, string relPath) =>
        Path.Combine(repo, relPath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>解码文本：UTF-8 为准，无法解码时用替换字符兜底（不抛异常）</summary>
    private static string DecodeText(byte[] bytes) => new UTF8Encoding(false, false).GetString(bytes);

    /// <summary>解析 --name-status -z 输出（\0 分隔；R/C 条目后带「旧路径 + 新路径」两个字段）</summary>
    private static List<FileChange> ParseNameStatus(string output, Dictionary<string, (int Add, int Rem, bool Bin)> stats)
    {
        var tokens = output.Split('\0');
        var list = new List<FileChange>();

        for (var i = 0; i < tokens.Length; i++)
        {
            var code = tokens[i].Trim();
            if (code.Length == 0) continue;

            var kind = code[0] switch
            {
                'A' => ChangeKind.Added,
                'M' => ChangeKind.Modified,
                'D' => ChangeKind.Deleted,
                'R' => ChangeKind.Renamed,
                'C' => ChangeKind.Copied,
                _ => ChangeKind.Other
            };

            var twoPaths = kind is ChangeKind.Renamed or ChangeKind.Copied;
            if (i + (twoPaths ? 2 : 1) >= tokens.Length) break;

            var oldPath = twoPaths ? tokens[i + 1] : "";
            var path = twoPaths ? tokens[i + 2] : tokens[i + 1];
            i += twoPaths ? 2 : 1;

            var change = new FileChange { Kind = kind, FilePath = path, OldPath = oldPath };
            if (stats.TryGetValue(path, out var s))
            {
                change.Added = s.Add;
                change.Removed = s.Rem;
                change.Binary = s.Bin;
            }
            list.Add(change);
        }
        return list;
    }

    /// <summary>解析 --numstat -z 输出（\0 分隔；重命名的路径跟在计数行之后的两个字段中）</summary>
    private static Dictionary<string, (int Add, int Rem, bool Bin)> ParseNumStat(string output)
    {
        var tokens = output.Split('\0');
        var map = new Dictionary<string, (int, int, bool)>(StringComparer.Ordinal);

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length == 0) continue;

            var parts = token.Split('\t');
            if (parts.Length < 3) continue;   // 重命名的计数行形如 "1\t1\t"，路径在后续字段里

            var bin = parts[0] == "-" || parts[1] == "-";
            var add = !bin && int.TryParse(parts[0], out var x) ? x : 0;
            var rem = !bin && int.TryParse(parts[1], out var y) ? y : 0;

            var path = parts[2];
            if (path.Length == 0)
            {
                if (i + 2 >= tokens.Length) break;
                path = tokens[i + 2];   // 新路径
                i += 2;
            }
            map[path] = (add, rem, bin);
        }
        return map;
    }
}

/// <summary>比对口径</summary>
public enum DiffScope
{
    /// <summary>工作区未提交改动 vs 最近一次提交</summary>
    WorkingTree,

    /// <summary>两次提交之间（a → b）</summary>
    CommitRange,

    /// <summary>某一个提交自身引入的改动</summary>
    SingleCommit
}

/// <summary>单个变更文件的类型</summary>
public enum ChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Other
}

/// <summary>比对范围内的一个变更文件（供界面列表直接绑定）</summary>
public class FileChange
{
    public ChangeKind Kind { get; init; }

    /// <summary>文件路径（重命名时为变更后的新路径）</summary>
    public string FilePath { get; init; } = "";

    /// <summary>重命名 / 复制前的旧路径</summary>
    public string OldPath { get; init; } = "";

    /// <summary>新增（+）行数；二进制文件为 0</summary>
    public int Added { get; set; }

    /// <summary>删除（−）行数</summary>
    public int Removed { get; set; }

    /// <summary>二进制或超大文件（不做文本差异）</summary>
    public bool Binary { get; set; }

    /// <summary>是否为「未跟踪的新文件」（git 尚未纳入版本控制）</summary>
    public bool Untracked { get; init; }

    /// <summary>状态中文描述（列表展示用）</summary>
    public string KindText => Kind switch
    {
        ChangeKind.Added => Untracked ? "新增·未跟踪" : "新增",
        ChangeKind.Modified => "修改",
        ChangeKind.Deleted => "删除",
        ChangeKind.Renamed => "重命名",
        ChangeKind.Copied => "复制",
        _ => "变更"
    };

    /// <summary>增删行数描述（列表展示用）</summary>
    public string StatText => Binary ? "二进制" : Untracked ? $"+{Added}" : $"+{Added} −{Removed}";

    /// <summary>是否为「新增类」条目（含未跟踪），供「仅显示新增」过滤使用</summary>
    public bool IsAdded => Kind == ChangeKind.Added;

    /// <summary>完整展示文本（可复制）</summary>
    public string Display => $"[{KindText}] {FilePath}  {StatText}";
}
