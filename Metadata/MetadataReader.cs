using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MViewer.Metadata;

/// <summary>
/// PNG / JPEG / WebP から生成AIのメタデータを読み取る。
/// 対応: Stable Diffusion WebUI(A1111/Forge), ComfyUI, NovelAI, InvokeAI。
/// </summary>
public static class MetadataReader
{
    const int MaxChunk = 64 * 1024 * 1024;

    public static ImageMetadata Read(string path)
    {
        var meta = new ImageMetadata();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
            meta.Raw = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => ReadPng(fs),
                ".jpg" or ".jpeg" => ReadJpeg(fs),
                ".webp" => ReadWebp(fs),
                _ => new Dictionary<string, string>()
            };
            Interpret(meta);
        }
        catch (Exception ex)
        {
            meta.Error = ex.Message;
        }
        return meta;
    }

    // ───────────────────────── 解釈 ─────────────────────────

    static void Interpret(ImageMetadata m)
    {
        var raw = m.Raw;
        try
        {
            if (raw.TryGetValue("parameters", out var p) && !string.IsNullOrWhiteSpace(p)) { ParseA1111(m, p); return; }
            if (raw.TryGetValue("UserComment", out var uc) && LooksLikeA1111(uc)) { ParseA1111(m, uc); return; }
            if (raw.TryGetValue("prompt", out var cp) && TryParseComfy(m, cp)) return;
            if (TryParseNovelAI(m)) return;
            if (raw.TryGetValue("invokeai_metadata", out var inv) && TryParseInvoke(m, inv)) return;
            foreach (var v in raw.Values)
                if (LooksLikeA1111(v)) { ParseA1111(m, v); return; }
            // 既知のツールに当てはまらないとき: 一般的な名前の項目を持つ JSON を汎用的に読む
            if (TryParseGenericJson(m)) return;
        }
        catch (Exception ex)
        {
            m.Error = "解析エラー: " + ex.Message;
        }
    }

    static bool LooksLikeA1111(string s) =>
        s.Contains("Steps: ", StringComparison.Ordinal) &&
        (s.Contains("Sampler: ", StringComparison.Ordinal) || s.Contains("Seed: ", StringComparison.Ordinal));

    static int? ToInt(string? s)
    {
        if (s == null) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return (int)d;
        return null;
    }

    static double? ToDouble(string? s) =>
        s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
    }

    // ───────────────────── A1111 / Forge ─────────────────────

    static readonly Regex ParamRegex =
        new(@"\s*([\w][\w ]*?):\s*(""(?:\\.|[^\\""])*""|[^,]*)(?:,\s*|$)", RegexOptions.Compiled);

    static void ParseA1111(ImageMetadata m, string text)
    {
        m.Source = "Stable Diffusion WebUI";
        text = text.Replace("\r\n", "\n").Trim('\0', ' ', '\n');
        var lines = text.Split('\n');

        int paramIdx = -1;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].StartsWith("Steps: ", StringComparison.Ordinal)) { paramIdx = i; break; }
        }

        string head = paramIdx >= 0 ? string.Join("\n", lines.Take(paramIdx)) : text;
        int neg;
        if (head.StartsWith("Negative prompt:", StringComparison.Ordinal)) neg = 0;
        else
        {
            int idx = head.IndexOf("\nNegative prompt:", StringComparison.Ordinal);
            neg = idx < 0 ? -1 : idx + 1;
        }
        if (neg >= 0)
        {
            m.Prompt = head[..neg].Trim();
            m.NegativePrompt = head[(neg + "Negative prompt:".Length)..].Trim();
        }
        else
        {
            m.Prompt = head.Trim();
        }

        if (paramIdx < 0) return;

        foreach (Match mt in ParamRegex.Matches(lines[paramIdx]))
        {
            string key = mt.Groups[1].Value.Trim();
            string val = mt.Groups[2].Value.Trim();
            if (val.Length >= 2 && val[0] == '"' && val[^1] == '"') val = val[1..^1];
            m.AddParam(key, val);
            switch (key)
            {
                case "Steps": m.Steps = ToInt(val); break;
                case "Sampler": m.Sampler = val; break;
                case "Schedule type": m.Scheduler = val; break;
                case "CFG scale": m.Cfg = ToDouble(val); break;
                case "Seed": m.Seed = val; break;
                case "Size": m.Size = val; break;
                case "Model": m.Model = val; break;
            }
        }
    }

    // ───────────────────────── ComfyUI ─────────────────────────

    static bool TryParseComfy(ImageMetadata m, string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch { return false; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            var nodes = new Dictionary<string, JsonElement>();
            foreach (var p in root.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("class_type", out _))
                    nodes[p.Name] = p.Value;
            if (nodes.Count == 0) return false;

            m.Source = "ComfyUI";

            static string ClassOf(JsonElement n) => n.GetProperty("class_type").GetString() ?? "";
            static bool InputsOf(JsonElement n, out JsonElement inputs)
            {
                inputs = default;
                return n.TryGetProperty("inputs", out inputs) && inputs.ValueKind == JsonValueKind.Object;
            }

            string? ResolveText(JsonElement v, int depth)
            {
                if (depth > 10) return null;
                if (v.ValueKind == JsonValueKind.String) return v.GetString();
                if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0 &&
                    nodes.TryGetValue(v[0].ToString(), out var n) && InputsOf(n, out var inp))
                {
                    foreach (var key in new[] { "text", "text_g", "string", "value", "prompt", "conditioning", "conditioning_1", "positive" })
                    {
                        if (inp.TryGetProperty(key, out var t))
                        {
                            var r = ResolveText(t, depth + 1);
                            if (!string.IsNullOrEmpty(r)) return r;
                        }
                    }
                }
                return null;
            }

            string? ResolveScalar(JsonElement v, int depth)
            {
                if (depth > 10) return null;
                switch (v.ValueKind)
                {
                    case JsonValueKind.String: return v.GetString();
                    case JsonValueKind.Number: return v.GetRawText();
                    case JsonValueKind.Array when v.GetArrayLength() > 0 &&
                                                  nodes.TryGetValue(v[0].ToString(), out var n) && InputsOf(n, out var inp):
                        foreach (var key in new[] { "value", "seed", "noise_seed", "int", "number" })
                            if (inp.TryGetProperty(key, out var t))
                            {
                                var r = ResolveScalar(t, depth + 1);
                                if (r != null) return r;
                            }
                        return null;
                    default: return null;
                }
            }

            string? Scalar(JsonElement inp, string key) =>
                inp.TryGetProperty(key, out var v) ? ResolveScalar(v, 0) : null;

            // サンプラー
            var ks = nodes.Values.FirstOrDefault(n => ClassOf(n).Contains("KSampler", StringComparison.Ordinal));
            if (ks.ValueKind == JsonValueKind.Object && InputsOf(ks, out var ki))
            {
                m.Seed = Scalar(ki, "seed") ?? Scalar(ki, "noise_seed");
                m.Steps = ToInt(Scalar(ki, "steps"));
                m.Cfg = ToDouble(Scalar(ki, "cfg"));
                m.Sampler = Scalar(ki, "sampler_name");
                m.Scheduler = Scalar(ki, "scheduler");
                if (ki.TryGetProperty("positive", out var pos)) m.Prompt = ResolveText(pos, 0);
                if (ki.TryGetProperty("negative", out var ng)) m.NegativePrompt = ResolveText(ng, 0);
            }

            // KSampler が無い構成: テキストエンコードノードから推測
            if (m.Prompt == null)
            {
                var texts = new List<string>();
                foreach (var n in nodes.Values)
                {
                    if (!ClassOf(n).Contains("TextEncode", StringComparison.Ordinal) || !InputsOf(n, out var inp)) continue;
                    foreach (var key in new[] { "text", "text_g", "prompt" })
                        if (inp.TryGetProperty(key, out var t) && ResolveText(t, 0) is { Length: > 0 } s) { texts.Add(s); break; }
                }
                if (texts.Count > 0) m.Prompt = texts[0];
                if (texts.Count > 1 && m.NegativePrompt == null) m.NegativePrompt = texts[1];
            }

            // モデル / LoRA / サイズ
            var loras = new List<string>();
            foreach (var n in nodes.Values)
            {
                if (!InputsOf(n, out var inp)) continue;
                string cls = ClassOf(n);
                if (m.Model == null)
                {
                    foreach (var key in new[] { "ckpt_name", "unet_name" })
                        if (inp.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) { m.Model = v.GetString(); break; }
                }
                if (cls.Contains("Lora", StringComparison.OrdinalIgnoreCase) &&
                    inp.TryGetProperty("lora_name", out var ln) && ln.ValueKind == JsonValueKind.String)
                    loras.Add(ln.GetString()!);
                if (m.Size == null && cls.Contains("EmptyLatent", StringComparison.Ordinal) &&
                    Str(inp, "width") is { } w && Str(inp, "height") is { } h)
                    m.Size = $"{w}x{h}";
            }

            m.AddParam("Model", m.Model);
            m.AddParam("Sampler", m.Sampler);
            m.AddParam("Scheduler", m.Scheduler);
            m.AddParam("Steps", m.Steps?.ToString(CultureInfo.InvariantCulture));
            m.AddParam("CFG scale", m.Cfg?.ToString(CultureInfo.InvariantCulture));
            m.AddParam("Seed", m.Seed);
            m.AddParam("Size", m.Size);
            foreach (var l in loras.Distinct()) m.AddParam("LoRA", l);
            return true;
        }
    }

    // ───────────────────────── NovelAI ─────────────────────────

    static bool TryParseNovelAI(ImageMetadata m)
    {
        var raw = m.Raw;
        if (!raw.TryGetValue("Comment", out var comment)) return false;
        bool isNai = raw.TryGetValue("Software", out var sw) && sw.Contains("NovelAI", StringComparison.OrdinalIgnoreCase);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(comment); } catch { return false; }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return false;
            if (!isNai && !r.TryGetProperty("uc", out _)) return false;

            m.Source = "NovelAI";
            m.Prompt = raw.TryGetValue("Description", out var d) ? d : Str(r, "prompt");
            m.NegativePrompt = Str(r, "uc");
            m.Steps = ToInt(Str(r, "steps"));
            m.Cfg = ToDouble(Str(r, "scale"));
            m.Seed = Str(r, "seed");
            m.Sampler = Str(r, "sampler");
            m.Scheduler = Str(r, "noise_schedule");
            if (Str(r, "width") is { } w && Str(r, "height") is { } h) m.Size = $"{w}x{h}";
            if (raw.TryGetValue("Source", out var src)) m.Model = src;

            // V4 系: ベースプロンプトが Description に無い場合の補完
            if (string.IsNullOrWhiteSpace(m.Prompt) &&
                r.TryGetProperty("v4_prompt", out var v4p) && v4p.TryGetProperty("caption", out var v4c))
                m.Prompt = Str(v4c, "base_caption");
            if (string.IsNullOrWhiteSpace(m.NegativePrompt) &&
                r.TryGetProperty("v4_negative_prompt", out var v4n) && v4n.TryGetProperty("caption", out var v4nc))
                m.NegativePrompt = Str(v4nc, "base_caption");

            m.AddParam("Model", m.Model);
            m.AddParam("Sampler", m.Sampler);
            m.AddParam("Scheduler", m.Scheduler);
            m.AddParam("Steps", m.Steps?.ToString(CultureInfo.InvariantCulture));
            m.AddParam("CFG scale", m.Cfg?.ToString(CultureInfo.InvariantCulture));
            m.AddParam("Seed", m.Seed);
            m.AddParam("Size", m.Size);
            m.AddParam("Software", raw.GetValueOrDefault("Software"));
            m.AddParam("Title", raw.GetValueOrDefault("Title"));
            m.AddParam("Generation time", raw.GetValueOrDefault("Generation time"));

            // キャラクター別プロンプト(V4 / V4.5)は Params ではなく Characters に入れる
            ReadNaiCharacters(m, r);

            foreach (var p in r.EnumerateObject())
            {
                // 上で整形して追加済みの項目と、別欄で扱う項目は除く
                if (p.Name is "prompt" or "uc" or "v4_prompt" or "v4_negative_prompt"
                    or "steps" or "scale" or "seed" or "sampler" or "noise_schedule" or "width" or "height") continue;
                switch (p.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        m.AddParam(p.Name, p.Value.GetString());
                        break;
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        m.AddParam(p.Name, p.Value.GetRawText());
                        break;
                    case JsonValueKind.Object:
                    case JsonValueKind.Array:
                        var txt = p.Value.GetRawText();
                        if (txt is not ("{}" or "[]")) m.AddParam(p.Name, txt);
                        break;
                }
            }
            return true;
        }
    }

    static void ReadNaiCharacters(ImageMetadata m, JsonElement r)
    {
        var pos = ReadNaiCaptions(r, "v4_prompt");
        var neg = ReadNaiCaptions(r, "v4_negative_prompt");
        int n = Math.Max(pos.Count, neg.Count);
        for (int i = 0; i < n; i++)
        {
            m.Characters.Add(new CharacterPrompt(
                i + 1,
                i < pos.Count ? pos[i].Text : null,
                i < neg.Count ? neg[i].Text : null,
                i < pos.Count ? pos[i].Position : null));
        }
    }

    static List<(string? Text, string? Position)> ReadNaiCaptions(JsonElement r, string prop)
    {
        var list = new List<(string?, string?)>();
        if (!r.TryGetProperty(prop, out var v4) || !v4.TryGetProperty("caption", out var cap)) return list;
        if (!cap.TryGetProperty("char_captions", out var chars) || chars.ValueKind != JsonValueKind.Array) return list;

        foreach (var c in chars.EnumerateArray())
        {
            string? position = null;
            if (c.TryGetProperty("centers", out var cs) && cs.ValueKind == JsonValueKind.Array && cs.GetArrayLength() > 0)
            {
                double? x = ToDouble(Str(cs[0], "x")), y = ToDouble(Str(cs[0], "y"));
                if (x != null && y != null)
                {
                    // NovelAI の 5×5 グリッド表記(A1〜E5)
                    char col = (char)('A' + Math.Clamp((int)(x.Value * 5), 0, 4));
                    int row = Math.Clamp((int)(y.Value * 5), 0, 4) + 1;
                    position = $"{col}{row} (x={x.Value:0.##}, y={y.Value:0.##})";
                }
            }
            list.Add((Str(c, "char_caption"), position));
        }
        return list;
    }

    // ───────────────────── 汎用(未知のツールのJSON) ─────────────────────

    static readonly string[] PromptKeys = { "prompt", "positive_prompt", "positiveprompt", "text_prompt", "textprompt", "positive" };
    static readonly string[] NegativeKeys = { "negative_prompt", "negativeprompt", "negative", "uc", "negative_text" };
    static readonly string[] ModelKeys = { "model", "model_name", "modelname", "checkpoint", "base_model", "basemodel", "sd_model", "model_id" };
    static readonly string[] SamplerKeys = { "sampler", "sampler_name", "samplername", "sampling_method", "samplingmethod" };
    static readonly string[] SchedulerKeys = { "scheduler", "noise_schedule", "schedule_type" };
    static readonly string[] StepsKeys = { "steps", "sampling_steps", "samplingsteps", "num_inference_steps" };
    static readonly string[] CfgKeys = { "cfg", "cfg_scale", "cfgscale", "guidance_scale", "guidancescale", "scale" };
    static readonly string[] SeedKeys = { "seed", "noise_seed" };
    static readonly string[] ParentKeys = { "parameters", "params", "meta", "metadata", "generation", "generation_params", "data" };

    /// <summary>オブジェクトの中(深さ3まで)から、候補名(大文字小文字を区別しない)に合う最初の値を探す。</summary>
    static JsonElement? FindProp(JsonElement obj, string[] names, int depth = 0)
    {
        if (obj.ValueKind != JsonValueKind.Object || depth > 3) return null;
        foreach (var p in obj.EnumerateObject())
            if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase) &&
                p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return p.Value;
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object) continue;
            if (depth == 0 && !ParentKeys.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) continue;
            var r = FindProp(p.Value, names, depth + 1);
            if (r != null) return r;
        }
        return null;
    }

    static string? Text(JsonElement? e) =>
        e == null ? null : e.Value.ValueKind == JsonValueKind.String ? e.Value.GetString() : e.Value.GetRawText();

    static bool TryParseGenericJson(ImageMetadata m)
    {
        foreach (var kv in m.Raw)
        {
            var s = kv.Value.TrimStart();
            if (s.Length == 0 || s[0] != '{') continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(kv.Value); } catch { continue; }
            using (doc)
            {
                var r = doc.RootElement;
                var prompt = Text(FindProp(r, PromptKeys));
                if (string.IsNullOrWhiteSpace(prompt)) continue;

                m.Source = "不明なツール(JSON: " + kv.Key + ")";
                m.Prompt = prompt;
                m.NegativePrompt = Text(FindProp(r, NegativeKeys));
                m.Model = Text(FindProp(r, ModelKeys));
                m.Sampler = Text(FindProp(r, SamplerKeys));
                m.Scheduler = Text(FindProp(r, SchedulerKeys));
                m.Steps = ToInt(Text(FindProp(r, StepsKeys)));
                m.Cfg = ToDouble(Text(FindProp(r, CfgKeys)));
                m.Seed = Text(FindProp(r, SeedKeys));
                var w = Text(FindProp(r, new[] { "width" }));
                var h = Text(FindProp(r, new[] { "height" }));
                if (w != null && h != null) m.Size = $"{w}x{h}";

                m.AddParam("Model", m.Model);
                m.AddParam("Sampler", m.Sampler);
                m.AddParam("Scheduler", m.Scheduler);
                m.AddParam("Steps", m.Steps?.ToString(CultureInfo.InvariantCulture));
                m.AddParam("CFG scale", m.Cfg?.ToString(CultureInfo.InvariantCulture));
                m.AddParam("Seed", m.Seed);
                m.AddParam("Size", m.Size);
                return true;
            }
        }
        return false;
    }

    // ───────────────────────── InvokeAI ─────────────────────────

    static bool TryParseInvoke(ImageMetadata m, string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch { return false; }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return false;
            m.Prompt = Str(r, "positive_prompt");
            m.NegativePrompt = Str(r, "negative_prompt");
            if (m.Prompt == null && m.NegativePrompt == null) return false;

            m.Source = "InvokeAI";
            m.Seed = Str(r, "seed");
            m.Steps = ToInt(Str(r, "steps"));
            m.Cfg = ToDouble(Str(r, "cfg_scale"));
            m.Scheduler = Str(r, "scheduler");
            if (r.TryGetProperty("model", out var mo)) m.Model = Str(mo, "name");
            if (Str(r, "width") is { } w && Str(r, "height") is { } h) m.Size = $"{w}x{h}";

            m.AddParam("Model", m.Model);
            m.AddParam("Scheduler", m.Scheduler);
            m.AddParam("Steps", m.Steps?.ToString(CultureInfo.InvariantCulture));
            m.AddParam("CFG scale", m.Cfg?.ToString(CultureInfo.InvariantCulture));
            m.AddParam("Seed", m.Seed);
            m.AddParam("Size", m.Size);
            return true;
        }
    }

    // ───────────────────────── PNG ─────────────────────────

    static Dictionary<string, string> ReadPng(Stream s)
    {
        var d = new Dictionary<string, string>();
        Span<byte> sig = stackalloc byte[8];
        if (s.Read(sig) < 8 || sig[0] != 0x89 || sig[1] != 0x50 || sig[2] != 0x4E || sig[3] != 0x47) return d;

        var hdr = new byte[8];
        while (s.Position + 8 <= s.Length)
        {
            s.ReadExactly(hdr);
            uint len = BinaryPrimitives.ReadUInt32BigEndian(hdr);
            string type = Encoding.ASCII.GetString(hdr, 4, 4);
            if (type == "IEND") break;

            if ((type is "tEXt" or "iTXt" or "zTXt") && len <= MaxChunk)
            {
                var data = new byte[len];
                s.ReadExactly(data);
                s.Seek(4, SeekOrigin.Current); // CRC
                try { ParsePngText(type, data, d); } catch { /* 壊れたチャンクは無視 */ }
            }
            else
            {
                s.Seek((long)len + 4, SeekOrigin.Current);
            }
        }
        return d;
    }

    static void ParsePngText(string type, byte[] data, Dictionary<string, string> d)
    {
        int z = Array.IndexOf(data, (byte)0);
        if (z <= 0) return;
        string key = Encoding.Latin1.GetString(data, 0, z);
        string value;

        switch (type)
        {
            case "tEXt":
                value = DecodeText(data.AsSpan(z + 1));
                break;
            case "zTXt":
                value = DecodeText(Inflate(data.AsSpan(z + 2)));
                break;
            default: // iTXt
            {
                int i = z + 1;
                byte compressed = data[i];
                i += 2; // compression flag + method
                i = Array.IndexOf(data, (byte)0, i) + 1;   // language tag
                i = Array.IndexOf(data, (byte)0, i) + 1;   // translated keyword
                var body = data.AsSpan(i);
                value = compressed == 1 ? Encoding.UTF8.GetString(Inflate(body)) : Encoding.UTF8.GetString(body);
                break;
            }
        }
        d[key] = value.TrimEnd('\0');
    }

    static byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch { return Encoding.Latin1.GetString(bytes); }
    }

    // ───────────────────────── JPEG ─────────────────────────

    static Dictionary<string, string> ReadJpeg(Stream s)
    {
        var d = new Dictionary<string, string>();
        if (s.ReadByte() != 0xFF || s.ReadByte() != 0xD8) return d;

        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) break;
            if (b != 0xFF) continue;

            int marker;
            do { marker = s.ReadByte(); } while (marker == 0xFF);
            if (marker < 0 || marker == 0xDA || marker == 0xD9) break;       // SOS / EOI
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;

            int hi = s.ReadByte(), lo = s.ReadByte();
            if (lo < 0) break;
            int len = ((hi << 8) | lo) - 2;
            if (len < 0) break;

            if (marker == 0xE1 || marker == 0xFE)
            {
                var seg = new byte[len];
                s.ReadExactly(seg);
                if (marker == 0xE1 && seg.Length > 6 && Encoding.ASCII.GetString(seg, 0, 4) == "Exif")
                    ParseExif(seg.AsSpan(6).ToArray(), d);
                else if (marker == 0xFE)
                    d["JpegComment"] = DecodeText(seg);
            }
            else
            {
                s.Seek(len, SeekOrigin.Current);
            }
        }
        return d;
    }

    // ───────────────────────── WebP ─────────────────────────

    static Dictionary<string, string> ReadWebp(Stream s)
    {
        var d = new Dictionary<string, string>();
        var head = new byte[12];
        if (s.Read(head, 0, 12) < 12 || Encoding.ASCII.GetString(head, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(head, 8, 4) != "WEBP") return d;

        var hdr = new byte[8];
        while (s.Position + 8 <= s.Length)
        {
            s.ReadExactly(hdr);
            string fourcc = Encoding.ASCII.GetString(hdr, 0, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(4));

            if (fourcc == "EXIF" && size <= MaxChunk)
            {
                var data = new byte[size];
                s.ReadExactly(data);
                int off = data.Length > 6 && Encoding.ASCII.GetString(data, 0, 4) == "Exif" ? 6 : 0;
                ParseExif(data.AsSpan(off).ToArray(), d);
            }
            else
            {
                s.Seek(size, SeekOrigin.Current);
            }
            if ((size & 1) == 1) s.Seek(1, SeekOrigin.Current);
        }
        return d;
    }

    // ───────────────────────── EXIF ─────────────────────────

    static void ParseExif(byte[] t, Dictionary<string, string> d)
    {
        if (t.Length < 8) return;
        bool le = t[0] == (byte)'I';

        ushort U16(int o) => le ? BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(t.AsSpan(o));
        uint U32(int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(t.AsSpan(o));

        void Ifd(uint off, int depth)
        {
            if (depth > 2 || off + 2 > t.Length) return;
            int n = U16((int)off);
            for (int i = 0; i < n; i++)
            {
                int e = (int)off + 2 + i * 12;
                if (e + 12 > t.Length) break;

                ushort tag = U16(e), type = U16(e + 2);
                uint cnt = U32(e + 4);

                if (tag == 0x8769) { Ifd(U32(e + 8), depth + 1); continue; } // Exif IFD

                int unit = type switch { 3 => 2, 4 => 4, 5 => 8, _ => 1 };
                long size = (long)unit * cnt;
                long vo = size <= 4 ? e + 8 : U32(e + 8);
                if (vo < 0 || vo + size > t.Length) continue;

                var span = t.AsSpan((int)vo, (int)size);
                switch (tag)
                {
                    case 0x010E: d["ImageDescription"] = DecodeText(span).TrimEnd('\0'); break;
                    case 0x0131: d["Software"] = DecodeText(span).TrimEnd('\0'); break;
                    case 0x9286: d["UserComment"] = DecodeUserComment(span); break;
                }
            }
        }

        Ifd(U32(4), 0);
    }

    static string DecodeUserComment(ReadOnlySpan<byte> b)
    {
        if (b.Length < 8) return "";
        string prefix = Encoding.ASCII.GetString(b[..8]).TrimEnd('\0', ' ');
        var body = b[8..];

        if (prefix == "UNICODE")
        {
            // A1111 は UTF-16BE、他のツールは LE のことがある。先頭文字が ASCII の想定で判定。
            bool be = body.Length < 2 || body[0] == 0;
            if (body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF) { be = true; body = body[2..]; }
            else if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE) { be = false; body = body[2..]; }
            return (be ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(body).TrimEnd('\0');
        }
        if (prefix == "ASCII") return Encoding.ASCII.GetString(body).TrimEnd('\0');
        return DecodeText(body).TrimEnd('\0');
    }
}
