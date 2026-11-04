using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PromptLoupe.Tests;

/// <summary>
/// テスト用の画像ファイルを、メタデータの部分だけ組み立てる(画素データは入れない)。
/// MetadataReader は画素を読まないので、これで足りる。
/// </summary>
static class TestImages
{
    // ───────── 一時ファイル ─────────

    /// <summary>一時フォルダに書き出して、そのパスを返す。拡張子で形式が決まる。</summary>
    public static string Save(byte[] data, string ext)
    {
        var dir = Path.Combine(Path.GetTempPath(), "PromptLoupe.Tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ext);
        File.WriteAllBytes(path, data);
        return path;
    }

    // ───────── PNG ─────────

    public static byte[] Png(params (string Type, byte[] Data)[] chunks)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(ms, "IHDR", new byte[13]);
        foreach (var (type, data) in chunks) WriteChunk(ms, type, data);
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();

        static void WriteChunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
            s.Write(len);
            s.Write(Encoding.ASCII.GetBytes(type));
            s.Write(data);
            s.Write(new byte[4]); // CRC(読み取り側は確かめない)
        }
    }

    public static (string, byte[]) TEXt(string key, string value) =>
        ("tEXt", Concat(Encoding.Latin1.GetBytes(key), new byte[] { 0 }, Encoding.UTF8.GetBytes(value)));

    public static (string, byte[]) ZTXt(string key, string value) =>
        ("zTXt", Concat(Encoding.Latin1.GetBytes(key), new byte[] { 0, 0 }, Deflate(Encoding.UTF8.GetBytes(value))));

    public static (string, byte[]) ITXt(string key, string value, bool compressed)
    {
        var body = Encoding.UTF8.GetBytes(value);
        if (compressed) body = Deflate(body);
        return ("iTXt", Concat(
            Encoding.Latin1.GetBytes(key), new byte[] { 0, (byte)(compressed ? 1 : 0), 0 },
            Encoding.ASCII.GetBytes("ja"), new byte[] { 0 },   // 言語タグ
            new byte[] { 0 },                                  // 翻訳したキーワード(空)
            body));
    }

    static byte[] Deflate(byte[] data)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return ms.ToArray();
    }

    // ───────── JPEG ─────────

    public static byte[] Jpeg(byte[]? exifTiff, string? comment = null)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0xFF, 0xD8 });
        if (exifTiff != null) WriteSegment(ms, 0xE1, Concat(Encoding.ASCII.GetBytes("Exif\0\0"), exifTiff));
        if (comment != null) WriteSegment(ms, 0xFE, Encoding.UTF8.GetBytes(comment));
        WriteSegment(ms, 0xDA, new byte[10]);          // SOS(ここで読み取りは止まる)
        ms.Write(new byte[] { 0x12, 0x34, 0xFF, 0xD9 });
        return ms.ToArray();

        static void WriteSegment(Stream s, byte marker, byte[] data)
        {
            s.WriteByte(0xFF);
            s.WriteByte(marker);
            int len = data.Length + 2;
            s.WriteByte((byte)(len >> 8));
            s.WriteByte((byte)len);
            s.Write(data);
        }
    }

    // ───────── WebP ─────────

    public static byte[] Webp(byte[] exifTiff, bool exifPrefix = false)
    {
        var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes("WEBP"));
        WriteChunk(body, "VP8X", new byte[10]);
        WriteChunk(body, "VP8 ", new byte[7]);   // 奇数の大きさ(埋め草の読み飛ばしを試す)
        WriteChunk(body, "EXIF", exifPrefix ? Concat(Encoding.ASCII.GetBytes("Exif\0\0"), exifTiff) : exifTiff);

        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("RIFF"));
        ms.Write(U32Le((uint)body.Length));
        body.Position = 0;
        body.CopyTo(ms);
        return ms.ToArray();

        static void WriteChunk(Stream s, string fourcc, byte[] data)
        {
            s.Write(Encoding.ASCII.GetBytes(fourcc));
            s.Write(U32Le((uint)data.Length));
            s.Write(data);
            if ((data.Length & 1) == 1) s.WriteByte(0);
        }
    }

    // ───────── HEIC(ISOBMFF) ─────────

    /// <summary>ftyp・meta(iinf と iloc)・mdat だけの HEIF。Exif アイテム(ID 2)を mdat に置く。</summary>
    public static byte[] Heic(byte[] exifTiff)
    {
        // Exif アイテムの中身: TIFF ヘッダーまでのオフセット(6)+ "Exif\0\0" + TIFF
        var item = Concat(U32Be(6), Encoding.ASCII.GetBytes("Exif\0\0"), exifTiff);

        var ftyp = Box("ftyp", Concat(Encoding.ASCII.GetBytes("heic"), U32Be(0), Encoding.ASCII.GetBytes("mif1heic")));

        // infe(version 2): item_ID, item_protection_index, item_type, item_name
        byte[] Infe(ushort id, string type) =>
            Box("infe", Concat(new byte[] { 2, 0, 0, 0 }, U16Be(id), U16Be(0), Encoding.ASCII.GetBytes(type), new byte[] { 0 }));
        var iinf = Box("iinf", Concat(new byte[] { 0, 0, 0, 0 }, U16Be(2), Infe(1, "hvc1"), Infe(2, "Exif")));

        // iloc の大きさは中身のオフセットに依らないので、先に仮の値で組んで位置を決める
        byte[] Iloc(uint exifOffset) => Box("iloc", Concat(
            new byte[] { 0, 0, 0, 0 },   // version 0
            new byte[] { 0x44, 0x00 },   // offset_size=4, length_size=4, base_offset_size=0
            U16Be(2),
            // アイテム 1(画像。中身は使わない)
            U16Be(1), U16Be(0), U16Be(1), U32Be(0), U32Be(0),
            // アイテム 2(Exif)
            U16Be(2), U16Be(0), U16Be(1), U32Be(exifOffset), U32Be((uint)item.Length)));

        byte[] Meta(uint exifOffset) => Box("meta", Concat(new byte[] { 0, 0, 0, 0 }, iinf, Iloc(exifOffset)));

        uint offset = (uint)(ftyp.Length + Meta(0).Length + 8);   // mdat のヘッダー(8 バイト)の後
        return Concat(ftyp, Meta(offset), Box("mdat", item));
    }

    static byte[] Box(string type, byte[] body) => Concat(U32Be((uint)(body.Length + 8)), Encoding.ASCII.GetBytes(type), body);

    // ───────── 共通 ─────────

    public static byte[] Concat(params byte[][] parts)
    {
        var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p);
        return ms.ToArray();
    }

    static byte[] U16Be(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }
    static byte[] U32Be(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }
    static byte[] U32Le(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }
}

/// <summary>EXIF(TIFF 形式)を組み立てる。IFD0・Exif IFD・GPS IFD に項目を足して Build する。</summary>
sealed class ExifBuilder
{
    readonly bool be;
    readonly List<Entry> ifd0 = new(), exif = new(), gps = new();

    sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Value);

    /// <summary>bigEndian = true なら "MM"(モトローラ)、false なら "II"(インテル)。</summary>
    public ExifBuilder(bool bigEndian = false) => be = bigEndian;

    public ExifBuilder Ifd0 => Target(ifd0);
    public ExifBuilder Exif => Target(exif);
    public ExifBuilder Gps => Target(gps);

    List<Entry> current = null!;
    ExifBuilder Target(List<Entry> list) { current = list; return this; }

    public ExifBuilder Ascii(ushort tag, string s)
    {
        var b = TestImages.Concat(Encoding.ASCII.GetBytes(s), new byte[] { 0 });
        current.Add(new Entry(tag, 2, (uint)b.Length, b));
        return this;
    }

    public ExifBuilder Short(ushort tag, ushort v) { current.Add(new Entry(tag, 3, 1, U16(v))); return this; }

    public ExifBuilder Byte(ushort tag, byte v) { current.Add(new Entry(tag, 1, 1, new[] { v })); return this; }

    public ExifBuilder Rational(ushort tag, params (uint Num, uint Den)[] values)
    {
        var b = TestImages.Concat(values.SelectMany(v => new[] { U32(v.Num), U32(v.Den) }).ToArray());
        current.Add(new Entry(tag, 5, (uint)values.Length, b));
        return this;
    }

    public ExifBuilder SRational(ushort tag, int num, int den)
    {
        current.Add(new Entry(tag, 10, 1, TestImages.Concat(U32((uint)num), U32((uint)den))));
        return this;
    }

    public ExifBuilder Undefined(ushort tag, byte[] v) { current.Add(new Entry(tag, 7, (uint)v.Length, v)); return this; }

    /// <summary>UserComment(文字コード "UNICODE"、UTF-16)。</summary>
    public ExifBuilder UserComment(string text, bool utf16BigEndian = true) =>
        Undefined(0x9286, TestImages.Concat(Encoding.ASCII.GetBytes("UNICODE\0"),
            (utf16BigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode).GetBytes(text)));

    public byte[] Build()
    {
        var root = new List<Entry>(ifd0);
        // Exif IFD・GPS IFD へのポインター(値は後で埋める)
        if (exif.Count > 0) root.Add(new Entry(0x8769, 4, 1, new byte[4]));
        if (gps.Count > 0) root.Add(new Entry(0x8825, 4, 1, new byte[4]));

        static int IfdSize(int n) => 2 + 12 * n + 4;
        int pos0 = 8;
        int posExif = pos0 + IfdSize(root.Count);
        int posGps = posExif + (exif.Count > 0 ? IfdSize(exif.Count) : 0);
        int dataPos = posGps + (gps.Count > 0 ? IfdSize(gps.Count) : 0);

        var data = new MemoryStream();
        byte[] WriteIfd(List<Entry> entries)
        {
            var ms = new MemoryStream();
            ms.Write(U16((ushort)entries.Count));
            foreach (var e in entries)
            {
                ms.Write(U16(e.Tag));
                ms.Write(U16(e.Type));
                ms.Write(U32(e.Count));
                var v = e.Tag switch
                {
                    0x8769 => U32((uint)posExif),
                    0x8825 => U32((uint)posGps),
                    _ => e.Value,
                };
                if (v.Length <= 4)
                {
                    ms.Write(v);
                    ms.Write(new byte[4 - v.Length]);
                }
                else
                {
                    ms.Write(U32((uint)(dataPos + data.Length)));
                    data.Write(v);
                    if ((data.Length & 1) == 1) data.WriteByte(0);
                }
            }
            ms.Write(new byte[4]); // 次の IFD はなし
            return ms.ToArray();
        }

        var head = be ? new byte[] { (byte)'M', (byte)'M', 0, 42 } : new byte[] { (byte)'I', (byte)'I', 42, 0 };
        var parts = new List<byte[]> { head, U32((uint)pos0), WriteIfd(root) };
        if (exif.Count > 0) parts.Add(WriteIfd(exif));
        if (gps.Count > 0) parts.Add(WriteIfd(gps));
        parts.Add(data.ToArray());
        return TestImages.Concat(parts.ToArray());
    }

    byte[] U16(ushort v)
    {
        var b = new byte[2];
        if (be) BinaryPrimitives.WriteUInt16BigEndian(b, v); else BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        return b;
    }

    byte[] U32(uint v)
    {
        var b = new byte[4];
        if (be) BinaryPrimitives.WriteUInt32BigEndian(b, v); else BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }
}
