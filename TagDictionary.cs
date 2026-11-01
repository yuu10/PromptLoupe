using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PromptLoupe;

/// <summary>
/// Danbooru 系のタグ辞書(a1111-sd-webui-tagcomplete の danbooru.csv 形式)。
/// 1行が「タグ,分類,件数,"別名1,別名2"」。分類は 0=一般 / 1=アーティスト / 3=作品名 / 4=キャラクター名 / 5=メタ。
/// 検索用と同じ正規化(小文字・_ はスペース・\ なし)をしたタグ名で引く。
/// </summary>
public sealed class TagDictionary
{
    readonly Dictionary<string, byte> map;

    TagDictionary(Dictionary<string, byte> map) => this.map = map;

    /// <summary>登録されている語の数(別名を含む)。</summary>
    public int Count => map.Count;

    /// <summary>アプリの保存先にコピーした辞書の場所。</summary>
    public static string FilePath => Path.Combine(AppSettings.DataDir, "danbooru.csv");

    /// <summary>正規化済みのタグ名の分類を返す。辞書にないときは -1。</summary>
    public int Lookup(string normalizedTag) =>
        map.TryGetValue(normalizedTag, out var c) ? c : -1;

    /// <summary>辞書を読み込む。形式が違う(1件も読めない)ときは null とエラーメッセージを返す。</summary>
    public static (TagDictionary? Dictionary, string? Error) Load(string path)
    {
        try
        {
            var map = new Dictionary<string, byte>(200_000);
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                int c1 = line.IndexOf(',');
                if (c1 <= 0) continue;
                int c2 = line.IndexOf(',', c1 + 1);
                string catText = c2 < 0 ? line[(c1 + 1)..] : line[(c1 + 1)..c2];
                if (!byte.TryParse(catText.Trim(), out byte cat) || cat > 5) continue;

                string tag = FileItem.Normalize(line[..c1]).Trim();
                if (tag.Length == 0) continue;
                map.TryAdd(tag, cat);

                // 4列目が別名(引用符で囲まれ、カンマ区切り)
                int c3 = c2 < 0 ? -1 : line.IndexOf(',', c2 + 1);
                if (c3 >= 0 && c3 + 1 < line.Length)
                {
                    foreach (var a in line[(c3 + 1)..].Trim().Trim('"').Split(','))
                    {
                        string alias = FileItem.Normalize(a).Trim();
                        if (alias.Length > 0) map.TryAdd(alias, cat);
                    }
                }
            }

            if (map.Count == 0)
                return (null, "タグを1件も読み取れませんでした。danbooru.csv(タグ,分類,件数,別名 の形式)を選んでください。");
            return (new TagDictionary(map), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
