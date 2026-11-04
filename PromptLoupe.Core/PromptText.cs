using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PromptLoupe;

/// <summary>プロンプトの文字列の扱い(検索用の正規化、タグへの分割、重みの除去)。</summary>
public static class PromptText
{
    /// <summary>
    /// 検索用に正規化する: 小文字にし、_ をスペースに、エスケープ用の \ を取り除く。
    /// (hatsune_miku と hatsune miku、rem \(re:zero\) と rem (re:zero) を同じものとして探すため)
    /// </summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "").Replace('_', ' ').ToLowerInvariant();
    }

    /// <summary>
    /// プロンプトをカンマ(と改行)で区切ってタグにする。(…) [...] {...} &lt;...&gt; の中のカンマでは区切らない。
    /// かっこの数が合わないプロンプトでは、全体が1つのタグにならないよう、かっこを無視して区切り直す。
    /// </summary>
    public static List<string> SplitTags(string text)
    {
        var tags = Split(text, respectBrackets: true, out bool balanced);
        if (!balanced) tags = Split(text, respectBrackets: false, out _);
        return tags;

        static List<string> Split(string s, bool respectBrackets, out bool balanced)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;

            void Flush()
            {
                var t = sb.ToString().Trim();
                if (t.Length > 0) list.Add(t);
                sb.Clear();
            }

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)   // \( \) などのエスケープは、かっことして数えない
                {
                    sb.Append(c).Append(s[++i]);
                    continue;
                }
                if (respectBrackets)
                {
                    if ("([{<".IndexOf(c) >= 0) depth++;
                    else if (")]}>".IndexOf(c) >= 0 && depth > 0) depth--;
                }
                if ((c == ',' || c == '\n' || c == '\r') && depth == 0) Flush();
                else sb.Append(c);
            }
            Flush();
            balanced = depth == 0;
            return list;
        }
    }

    /// <summary>絞り込みに入れる形にする: 強調の重み・外側のかっこ・NovelAI の重み表記を取り除く。</summary>
    public static string CleanTag(string tag)
    {
        var t = tag.Trim();
        t = Regex.Replace(t, @"^-?\d+(\.\d+)?::", "").Trim();          // NovelAI: 1.2::tag
        if (t.EndsWith("::", StringComparison.Ordinal)) t = t[..^2].Trim(); // NovelAI: tag::
        while (t.Length >= 2 && "([{".IndexOf(t[0]) >= 0 && ")]}".IndexOf(t[^1]) >= 0 && t[^2] != '\\')
            t = t[1..^1].Trim();                                         // (tag:1.2) → tag:1.2
        t = Regex.Replace(t, @":\s*-?\d+(\.\d+)?$", "").Trim();          // tag:1.2 → tag
        return t;
    }

    /// <summary>
    /// 集計用のタグにする: プロンプトをタグに分け、重みやかっこを外して正規化し、重複を除く。
    /// (絞り込み欄に入る形と同じなので、そのまま絞り込みに使える)
    /// </summary>
    public static string[] TagKeys(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return SplitTags(text)
            .Select(t => Normalize(CleanTag(t)).Trim())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToArray();
    }
}
