using System.Text;
using PromptLoupe.Metadata;

namespace PromptLoupe.Tests;

public class MetadataReaderTests
{
    const string A1111 =
        "masterpiece, 1girl, (smile:1.2)\n" +
        "Negative prompt: lowres, bad hands\n" +
        "Steps: 28, Sampler: DPM++ 2M, Schedule type: Karras, CFG scale: 7, Seed: 12345, " +
        "Size: 832x1216, Model: animagine-xl, Lora hashes: \"a: 1, b: 2\"";

    static void AssertA1111(ImageMetadata m)
    {
        Assert.Null(m.Error);
        Assert.Equal("Stable Diffusion WebUI", m.Source);
        Assert.Equal("masterpiece, 1girl, (smile:1.2)", m.Prompt);
        Assert.Equal("lowres, bad hands", m.NegativePrompt);
        Assert.Equal(28, m.Steps);
        Assert.Equal("DPM++ 2M", m.Sampler);
        Assert.Equal("Karras", m.Scheduler);
        Assert.Equal(7.0, m.Cfg);
        Assert.Equal("12345", m.Seed);
        Assert.Equal("832x1216", m.Size);
        Assert.Equal("animagine-xl", m.Model);
        Assert.Contains(m.Params, p => p.Key == "Lora hashes" && p.Value == "a: 1, b: 2");
    }

    // ───────── PNG ─────────

    [Fact]
    public void Png_tEXt_A1111()
    {
        var path = TestImages.Save(TestImages.Png(TestImages.TEXt("parameters", A1111)), ".png");
        AssertA1111(MetadataReader.Read(path));
    }

    [Fact]
    public void Png_zTXt_A1111()
    {
        var path = TestImages.Save(TestImages.Png(TestImages.ZTXt("parameters", A1111)), ".png");
        AssertA1111(MetadataReader.Read(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Png_iTXt_A1111(bool compressed)
    {
        var path = TestImages.Save(TestImages.Png(TestImages.ITXt("parameters", A1111, compressed)), ".png");
        AssertA1111(MetadataReader.Read(path));
    }

    [Fact]
    public void Png_A1111_NoNegative()
    {
        var text = "1girl, solo\nSteps: 20, Sampler: Euler a, Seed: 1";
        var m = MetadataReader.Read(TestImages.Save(TestImages.Png(TestImages.TEXt("parameters", text)), ".png"));
        Assert.Equal("1girl, solo", m.Prompt);
        Assert.Null(m.NegativePrompt);
        Assert.Equal(20, m.Steps);
    }

    [Fact]
    public void Png_ComfyUI()
    {
        const string prompt = """
        {
          "3": { "class_type": "KSampler", "inputs": {
              "seed": 42, "steps": 30, "cfg": 5.5, "sampler_name": "euler", "scheduler": "normal",
              "positive": ["6", 0], "negative": ["7", 0], "model": ["4", 0], "latent_image": ["5", 0] } },
          "4": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "sdxl.safetensors" } },
          "5": { "class_type": "EmptyLatentImage", "inputs": { "width": 1024, "height": 768, "batch_size": 1 } },
          "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "a cat, garden", "clip": ["4", 1] } },
          "7": { "class_type": "CLIPTextEncode", "inputs": { "text": "blurry", "clip": ["4", 1] } },
          "8": { "class_type": "LoraLoader", "inputs": { "lora_name": "style.safetensors" } }
        }
        """;
        var m = MetadataReader.Read(TestImages.Save(TestImages.Png(TestImages.TEXt("prompt", prompt)), ".png"));
        Assert.Equal("ComfyUI", m.Source);
        Assert.Equal("a cat, garden", m.Prompt);
        Assert.Equal("blurry", m.NegativePrompt);
        Assert.Equal("42", m.Seed);
        Assert.Equal(30, m.Steps);
        Assert.Equal(5.5, m.Cfg);
        Assert.Equal("euler", m.Sampler);
        Assert.Equal("normal", m.Scheduler);
        Assert.Equal("sdxl.safetensors", m.Model);
        Assert.Equal("1024x768", m.Size);
        Assert.Contains(m.Params, p => p.Key == "LoRA" && p.Value == "style.safetensors");
    }

    [Fact]
    public void Png_NovelAI_V4_Characters()
    {
        const string comment = """
        {
          "prompt": "2girls, park", "uc": "lowres", "steps": 28, "scale": 5, "seed": 999,
          "sampler": "k_euler_ancestral", "noise_schedule": "karras", "width": 832, "height": 1216,
          "v4_prompt": { "caption": { "base_caption": "2girls, park", "char_captions": [
              { "char_caption": "girl, red hair", "centers": [ { "x": 0.1, "y": 0.5 } ] },
              { "char_caption": "girl, blue hair", "centers": [ { "x": 0.9, "y": 0.5 } ] } ] } },
          "v4_negative_prompt": { "caption": { "base_caption": "lowres", "char_captions": [
              { "char_caption": "bad hands" }, { "char_caption": "" } ] } }
        }
        """;
        var png = TestImages.Png(
            TestImages.TEXt("Software", "NovelAI"),
            TestImages.TEXt("Source", "NovelAI Diffusion V4.5"),
            TestImages.TEXt("Description", "2girls, park"),
            TestImages.TEXt("Comment", comment));
        var m = MetadataReader.Read(TestImages.Save(png, ".png"));

        Assert.Equal("NovelAI", m.Source);
        Assert.Equal("2girls, park", m.Prompt);
        Assert.Equal("lowres", m.NegativePrompt);
        Assert.Equal(28, m.Steps);
        Assert.Equal(5.0, m.Cfg);
        Assert.Equal("999", m.Seed);
        Assert.Equal("NovelAI Diffusion V4.5", m.Model);
        Assert.Equal("832x1216", m.Size);

        Assert.Equal(2, m.Characters.Count);
        Assert.Equal("girl, red hair", m.Characters[0].Prompt);
        Assert.Equal("bad hands", m.Characters[0].Negative);
        Assert.StartsWith("A3", m.Characters[0].Position);
        Assert.Equal("girl, blue hair", m.Characters[1].Prompt);
        Assert.StartsWith("E3", m.Characters[1].Position);

        // 整形済みの項目や、キャラクター別プロンプトは Params に重ねて出さない
        Assert.DoesNotContain(m.Params, p => p.Key is "v4_prompt" or "uc" or "prompt");
    }

    [Fact]
    public void Png_InvokeAI()
    {
        const string json = """
        { "positive_prompt": "castle", "negative_prompt": "ugly", "seed": 7, "steps": 25,
          "cfg_scale": 6.5, "scheduler": "dpmpp_2m", "width": 512, "height": 512, "model": { "name": "sd15" } }
        """;
        var m = MetadataReader.Read(TestImages.Save(TestImages.Png(TestImages.TEXt("invokeai_metadata", json)), ".png"));
        Assert.Equal("InvokeAI", m.Source);
        Assert.Equal("castle", m.Prompt);
        Assert.Equal("ugly", m.NegativePrompt);
        Assert.Equal(25, m.Steps);
        Assert.Equal(6.5, m.Cfg);
        Assert.Equal("sd15", m.Model);
        Assert.Equal("512x512", m.Size);
    }

    [Fact]
    public void Png_GenericJson()
    {
        const string json = """{ "metadata": { "Prompt": "sunset", "negative_prompt": "noise", "Steps": 12, "guidance_scale": 3.5 } }""";
        var m = MetadataReader.Read(TestImages.Save(TestImages.Png(TestImages.TEXt("someTool", json)), ".png"));
        Assert.StartsWith("不明なツール", m.Source);
        Assert.Equal("sunset", m.Prompt);
        Assert.Equal("noise", m.NegativePrompt);
        Assert.Equal(12, m.Steps);
        Assert.Equal(3.5, m.Cfg);
    }

    [Fact]
    public void Png_NoMetadata()
    {
        var m = MetadataReader.Read(TestImages.Save(TestImages.Png(), ".png"));
        Assert.Null(m.Error);
        Assert.False(m.HasData);
        Assert.Empty(m.Raw);
    }

    [Fact]
    public void Png_Japanese_UTF8()
    {
        var text = "猫耳, 笑顔\nSteps: 20, Sampler: Euler, Seed: 1";
        var m = MetadataReader.Read(TestImages.Save(TestImages.Png(TestImages.TEXt("parameters", text)), ".png"));
        Assert.Equal("猫耳, 笑顔", m.Prompt);
    }

    // ───────── JPEG ─────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Jpeg_UserComment_A1111(bool tiffBigEndian, bool utf16BigEndian)
    {
        var exif = new ExifBuilder(tiffBigEndian).Exif.UserComment(A1111, utf16BigEndian).Build();
        AssertA1111(MetadataReader.Read(TestImages.Save(TestImages.Jpeg(exif), ".jpg")));
    }

    [Fact]
    public void Jpeg_Comment()
    {
        var m = MetadataReader.Read(TestImages.Save(TestImages.Jpeg(null, "hello"), ".jpeg"));
        Assert.Equal("hello", m.Raw["JpegComment"]);
    }

    [Fact]
    public void Jpeg_CameraExif()
    {
        var exif = new ExifBuilder()
            .Ifd0.Ascii(0x010F, "Canon").Ascii(0x0110, "Canon EOS R5").Short(0x0112, 6)
                 .Ascii(0x0132, "2026:01:02 03:04:05")
            .Exif.Ascii(0x9003, "2025:12:31 23:59:58")
                 .Rational(0x829A, (1, 250)).Rational(0x829D, (28, 10)).Short(0x8827, 400)
                 .Rational(0x920A, (50, 1)).Short(0xA405, 75).SRational(0x9204, -1, 3)
                 .Short(0x9209, 1).Ascii(0xA434, "RF24-105mm F4 L IS USM")
            .Gps.Ascii(0x0001, "S").Rational(0x0002, (35, 1), (40, 1), (30, 1))
                .Ascii(0x0003, "W").Rational(0x0004, (139, 1), (45, 1), (0, 1))
                .Byte(0x0005, 1).Rational(0x0006, (12, 1))
            .Build();
        var x = MetadataReader.Read(TestImages.Save(TestImages.Jpeg(exif), ".jpg")).Exif;

        Assert.NotNull(x);
        Assert.Equal("Canon", x!.Make);
        Assert.Equal("Canon EOS R5", x.Model);
        Assert.Equal(6, x.Orientation);
        Assert.Equal(new DateTime(2025, 12, 31, 23, 59, 58), x.DateTaken);   // DateTimeOriginal を優先
        Assert.Equal(1.0 / 250, x.ExposureTime!.Value, 6);
        Assert.Equal(2.8, x.FNumber!.Value, 6);
        Assert.Equal(400, x.Iso);
        Assert.Equal(50, x.FocalLength);
        Assert.Equal(75, x.FocalLength35);
        Assert.Equal(-1.0 / 3, x.ExposureBias!.Value, 6);
        Assert.Equal("RF24-105mm F4 L IS USM", x.LensModel);
        Assert.Equal(-35.675, x.Latitude!.Value, 6);
        Assert.Equal(-139.75, x.Longitude!.Value, 6);
        Assert.Equal(-12, x.Altitude);
    }

    [Fact]
    public void Jpeg_DateTimeOnly_UsedAsDateTaken()
    {
        var exif = new ExifBuilder().Ifd0.Ascii(0x0132, "2026:01:02 03:04:05").Build();
        var x = MetadataReader.ReadExif(TestImages.Save(TestImages.Jpeg(exif), ".jpg"));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), x!.DateTaken);
    }

    [Fact]
    public void Jpeg_BlankDate_IsNull()
    {
        var exif = new ExifBuilder().Exif.Ascii(0x9003, "    :  :     :  :  ").Build();
        var x = MetadataReader.ReadExif(TestImages.Save(TestImages.Jpeg(exif), ".jpg"));
        Assert.Null(x!.DateTaken);
    }

    [Fact]
    public void Jpeg_GpsZero_Ignored()
    {
        var exif = new ExifBuilder()
            .Gps.Rational(0x0002, (0, 1), (0, 1), (0, 1)).Rational(0x0004, (0, 1), (0, 1), (0, 1))
            .Build();
        var x = MetadataReader.ReadExif(TestImages.Save(TestImages.Jpeg(exif), ".jpg"));
        Assert.False(x!.HasGps);
    }

    // ───────── WebP ─────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Webp_Exif(bool exifPrefix)
    {
        var exif = new ExifBuilder().Ifd0.Short(0x0112, 8).Exif.UserComment(A1111).Build();
        var path = TestImages.Save(TestImages.Webp(exif, exifPrefix), ".webp");
        AssertA1111(MetadataReader.Read(path));
        Assert.Equal(8, MetadataReader.ReadExif(path)!.Orientation);
    }

    // ───────── HEIC ─────────

    [Fact]
    public void Heic_Exif()
    {
        var exif = new ExifBuilder(bigEndian: true)
            .Ifd0.Ascii(0x010F, "Apple").Ascii(0x0110, "iPhone 15").Short(0x0112, 6)
            .Exif.Ascii(0x9003, "2026:05:06 07:08:09")
            .Build();
        var x = MetadataReader.Read(TestImages.Save(TestImages.Heic(exif), ".heic")).Exif;
        Assert.NotNull(x);
        Assert.Equal("Apple", x!.Make);
        Assert.Equal("iPhone 15", x.Model);
        Assert.Equal(6, x.Orientation);
        Assert.Equal(new DateTime(2026, 5, 6, 7, 8, 9), x.DateTaken);
    }

    // ───────── 壊れたファイル・未対応の形式 ─────────

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    [InlineData(".heic")]
    public void Garbage_DoesNotThrow(string ext)
    {
        var path = TestImages.Save(Encoding.ASCII.GetBytes("this is not an image at all"), ext);
        var m = MetadataReader.Read(path);
        Assert.False(m.HasData);
    }

    [Fact]
    public void TruncatedPng_ReportsErrorWithoutThrowing()
    {
        var png = TestImages.Png(TestImages.TEXt("parameters", A1111));
        var path = TestImages.Save(png[..(png.Length - 40)], ".png");
        var m = MetadataReader.Read(path);
        Assert.False(m.HasData);
    }

    [Fact]
    public void UnknownExtension_Empty()
    {
        var m = MetadataReader.Read(TestImages.Save(TestImages.Png(TestImages.TEXt("parameters", A1111)), ".bmp"));
        Assert.False(m.HasData);
    }

    [Fact]
    public void MissingFile_ReportsError()
    {
        var m = MetadataReader.Read(Path.Combine(Path.GetTempPath(), "no-such-file-" + Guid.NewGuid() + ".png"));
        Assert.NotNull(m.Error);
        Assert.Null(MetadataReader.ReadExif(Path.Combine(Path.GetTempPath(), "no-such-file.jpg")));
    }
}
