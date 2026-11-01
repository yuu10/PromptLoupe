using System.Collections.Generic;

namespace PromptLoupe.Metadata;

/// <summary>キャラクター別プロンプト(NovelAI V4 / V4.5 など)。</summary>
public sealed record CharacterPrompt(int Index, string? Prompt, string? Negative, string? Position);

/// <summary>画像に埋め込まれた生成AIメタデータ(解釈済み)。</summary>
public sealed class ImageMetadata
{
    /// <summary>生成元ツール名(例: "Stable Diffusion WebUI", "ComfyUI", "NovelAI")。解釈できなければ null。</summary>
    public string? Source { get; set; }

    public string? Prompt { get; set; }
    public string? NegativePrompt { get; set; }
    public string? Model { get; set; }
    public string? Sampler { get; set; }
    public string? Scheduler { get; set; }
    public int? Steps { get; set; }
    public double? Cfg { get; set; }
    public string? Seed { get; set; }
    public string? Size { get; set; }

    /// <summary>キャラクター別プロンプト(無ければ空)。</summary>
    public List<CharacterPrompt> Characters { get; } = new();

    /// <summary>表示用のパラメーター一覧(順序保持)。</summary>
    public List<KeyValuePair<string, string>> Params { get; } = new();

    /// <summary>ファイルから読み取った生のテキスト(PNGのtEXt/iTXt、EXIFのUserCommentなど)。</summary>
    public Dictionary<string, string> Raw { get; set; } = new();

    public string? Error { get; set; }

    public bool HasData => Source != null;

    public void AddParam(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Params.Add(new KeyValuePair<string, string>(key, value));
    }
}
