namespace Lateraroot.Core;

public enum DockEdge { Right, Left }
public enum FileOrder { Name, Modified, Type, Size }

public sealed class StackRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新しいスタック";
    public string Symbol { get; set; } = "◇";
    public string Color { get; set; } = "#A7B8FF";
    public string Filter { get; set; } = ".txt";

    [System.Text.Json.Serialization.JsonIgnore]
    public string[] Tokens => Filter.Split([',', ';', ' ', '、'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.ToLowerInvariant()).ToArray();

    public bool Matches(string path, bool directory) => Tokens.Any(token => directory
        ? token == "<folders>"
        : token != "*" && token != "<folders>" && string.Equals(Path.GetExtension(path), Normalize(token), StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string token) => token.StartsWith("*.") ? token[1..] : token.StartsWith('.') ? token : "." + token;
}

public sealed class AppSettings
{
    public DockEdge Edge { get; set; } = DockEdge.Right;
    public FileOrder Order { get; set; } = FileOrder.Name;
    public string Monitor { get; set; } = "";
    public bool IncludePublicDesktop { get; set; } = true;
    public int CloseDelayMs { get; set; } = 500;
    public List<StackRule> Stacks { get; set; } = Defaults();

    public static List<StackRule> Defaults() =>
    [
        new() { Id = "images", Name = "画像", Symbol = "▧", Color = "#B4A3F8", Filter = ".png, .jpg, .jpeg, .gif, .webp, .svg, .bmp, .heic, .ico" },
        new() { Id = "documents", Name = "ドキュメント", Symbol = "≡", Color = "#9BBEFF", Filter = ".pdf, .docx, .doc, .txt, .md, .xlsx, .xls, .csv, .pptx, .ppt, .rtf" },
        new() { Id = "media", Name = "メディア", Symbol = "▷", Color = "#F4B78D", Filter = ".mp4, .mov, .mkv, .avi, .mp3, .wav, .flac, .m4a" },
        new() { Id = "archives", Name = "アーカイブ", Symbol = "◇", Color = "#E4C879", Filter = ".zip, .7z, .rar, .tar, .gz" },
        new() { Id = "shortcuts", Name = "ショートカット", Symbol = "↗", Color = "#93D7BF", Filter = ".lnk, .url" },
        new() { Id = "folders", Name = "フォルダー", Symbol = "▦", Color = "#E6BD79", Filter = "<folders>" },
        new() { Id = "other", Name = "その他", Symbol = "⋯", Color = "#A9B0C3", Filter = "*" }
    ];

    public void Validate()
    {
        if (!Enum.IsDefined(Edge) || !Enum.IsDefined(Order)) throw new InvalidDataException("配置または並び順が不正です。");
        if (Stacks is null || Stacks.Count is < 1 or > 16) throw new InvalidDataException("スタックは1〜16個にしてください。");
        if (Stacks.Any(s => s is null || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 24 || string.IsNullOrWhiteSpace(s.Filter)))
            throw new InvalidDataException("スタック名（24文字以内）と分類ルールを入力してください。");
        if (Stacks.Select(s => s.Id).Distinct().Count() != Stacks.Count) throw new InvalidDataException("スタックIDが重複しています。");
        if (Stacks.Count(s => s.Tokens.Contains("*")) != 1) throw new InvalidDataException("未分類用の「*」を持つスタックを1つ残してください。");
        foreach (var stack in Stacks)
        {
            if (stack.Tokens.Any(t => t != "*" && t != "<folders>" && (t.Contains('/') || t.Contains('\\') || t.TrimStart('*', '.').Length == 0 || t[1..].Contains('*') || t.Contains('?'))))
                throw new InvalidDataException("分類には拡張子（.pdfなど）、<folders>、* を使ってください。");
            if (stack.Color is null || !System.Text.RegularExpressions.Regex.IsMatch(stack.Color, "^#[0-9A-Fa-f]{6}$")) stack.Color = "#A7B8FF";
            if (string.IsNullOrWhiteSpace(stack.Symbol) || stack.Symbol.Length > 3) stack.Symbol = "◇";
        }
        CloseDelayMs = Math.Clamp(CloseDelayMs, 200, 2000);
    }

    public StackRule Classify(string path, bool directory, string? assignedId = null) =>
        Stacks.FirstOrDefault(s => s.Id == assignedId) ?? Stacks.FirstOrDefault(s => s.Matches(path, directory)) ?? Stacks.First(s => s.Tokens.Contains("*"));
}

public sealed record DesktopItem(string Path, string Name, bool IsDirectory, long Size, DateTime Modified, string StackId, string? RecordId, bool CanOrganize)
{
    public string Extension => IsDirectory ? "フォルダー" : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
    public bool IsStored => RecordId is not null;
    public string Detail => IsDirectory ? "フォルダー" : Size >= 1_048_576 ? $"{Size / 1_048_576d:0.#} MB" : $"{Math.Max(1, Size / 1024d):0.#} KB";
}

public sealed class StorageRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OriginalPath { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public string? AssignedStackId { get; set; }
}

public sealed record OperationResult(int Succeeded, IReadOnlyList<string> Errors);
