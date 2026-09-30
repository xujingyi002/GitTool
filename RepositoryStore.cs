using System.Text;

namespace GitTool;

/// <summary>
/// 最近打开的仓库记录：一行一个路径，保存在 %APPDATA%\GitTool\recent-repos.txt。
/// 按「最近使用在最前」排序、自动去重、上限 15 条；文件是纯文本，可手工编辑。
/// 任何读写异常都只当作「没有历史」，绝不影响主流程。
/// </summary>
public static class RepositoryStore
{
    private const int MaxItems = 15;

    private static string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitTool");

    /// <summary>历史记录存放目录（默认 %APPDATA%\GitTool；测试时可改写为临时目录）</summary>
    public static string StorageDirectory
    {
        get => _dir;
        set { if (!string.IsNullOrWhiteSpace(value)) _dir = value; }
    }

    /// <summary>历史记录文件完整路径</summary>
    public static string StorageFile => Path.Combine(_dir, "recent-repos.txt");

    /// <summary>读取历史记录（文件不存在或损坏时返回空列表）</summary>
    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(StorageFile)) return new List<string>();

            var list = new List<string>();
            foreach (var raw in File.ReadAllLines(StorageFile, Encoding.UTF8))
            {
                var path = raw.Trim();
                if (path.Length == 0) continue;
                if (list.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(path);
            }
            if (list.Count > MaxItems) list.RemoveRange(MaxItems, list.Count - MaxItems);
            return list;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>把一个仓库放到历史最前面（已存在则前移），返回更新后的列表</summary>
    public static List<string> Add(string path)
    {
        var list = Load();
        var normalized = Normalize(path);
        if (normalized.Length == 0) return list;

        // 已有同一条目（仅大小写 / 结尾斜杠不同）→ 沿用原来记录的写法，只把它前移
        var existing = list.FirstOrDefault(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        list.RemoveAll(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, existing ?? normalized);

        if (list.Count > MaxItems) list.RemoveRange(MaxItems, list.Count - MaxItems);
        Save(list);
        return list;
    }

    /// <summary>从历史中移除一个路径，返回更新后的列表</summary>
    public static List<string> Remove(string path)
    {
        var list = Load();
        var normalized = Normalize(path);
        if (list.RemoveAll(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)) > 0) Save(list);
        return list;
    }

    /// <summary>规范化路径：去首尾空白与引号、去掉结尾的分隔符（保护 "E:\" 这类根路径）</summary>
    private static string Normalize(string? path)
    {
        var p = (path ?? "").Trim().Trim('"').Trim();
        if (p.Length <= 3) return p;   // "E:\" 或 "C:" 之类不要动
        return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void Save(List<string> list)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllLines(StorageFile, list, new UTF8Encoding(false));
        }
        catch
        {
            // 写入失败（无权限 / 被占用）只影响「下次不记得」，不打断当前操作
        }
    }
}
