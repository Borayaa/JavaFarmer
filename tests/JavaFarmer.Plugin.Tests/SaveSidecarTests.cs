namespace JavaFarmer.Plugin.Tests;

[Collection("Game")]
public sealed class SaveSidecarTests : IDisposable
{
    private readonly string save = Repo.NewTempDir("sidecar-save");
    private readonly string backup = Repo.NewTempDir("sidecar-backup");

    public void Dispose()
    {
        SaveSidecar.Language = null;
        SaveSidecar.Sections.Clear();
        try { Directory.Delete(save, true); Directory.Delete(backup, true); } catch { }
    }

    [Fact]
    public void RoundTrip_ToSaveAndBackup_WithEvents()
    {
        int saving = 0, loaded = 0;
        Action s = () => { saving++; SaveSidecar.Sections["ai_ml"] = "{\"level\": 2, \"seed\": 12345}"; };
        Action l = () => loaded++;
        SaveSidecar.Saving += s;
        SaveSidecar.Loaded += l;
        try
        {
            SaveSidecar.Language = "rust";
            SaveSidecar.Sections["cybersecurity"] = "{\"bits\": [1, 2.5, -3e2], \"name\": \"a\\\"b\", \"ok\": true, \"none\": null}";
            SaveSidecar.Sections["plain"] = "not json\nsecond \"line\"\ttab";
            SaveSidecar.Write(save, backup);
            Assert.Equal(1, saving);
            Assert.Equal(File.ReadAllText(Path.Combine(save, SaveSidecar.FileName)), File.ReadAllText(Path.Combine(backup, SaveSidecar.FileName)));

            var expected = new Dictionary<string, string>(SaveSidecar.Sections);
            SaveSidecar.Language = null;
            SaveSidecar.Sections.Clear();
            Assert.True(SaveSidecar.Read(backup));
            Assert.Equal(1, loaded);
            Assert.True(SaveSidecar.FileFound);
            Assert.Equal("rust", SaveSidecar.Language);
            Assert.Equal(backup, SaveSidecar.SaveDirectory);
            Assert.Equal(expected.Count, SaveSidecar.Sections.Count);
            foreach (var kv in expected) Assert.Equal(kv.Value, SaveSidecar.Sections[kv.Key]);

            // write without the Saving event (used right after a conversion)
            SaveSidecar.Write(false, save);
            Assert.Equal(1, saving);
        }
        finally
        {
            SaveSidecar.Saving -= s;
            SaveSidecar.Loaded -= l;
        }
    }

    [Fact]
    public void Format_IsReadableJson()
    {
        SaveSidecar.Language = "java";
        SaveSidecar.Sections.Clear();
        SaveSidecar.Sections["x"] = "{}";
        string json = SaveSidecar.Serialize();
        Assert.Equal("{\n  \"version\": 2,\n  \"language\": \"java\",\n  \"extensions\": {\n    \"x\": {}\n  }\n}\n", json);
        Assert.True(MiniJson.IsValid(json));
        SaveSidecar.Sections.Clear();
        Assert.True(MiniJson.IsValid(SaveSidecar.Serialize()));
    }

    [Fact]
    public void MissingOrBrokenFile_MeansAnOldJavaSave()
    {
        SaveSidecar.Language = "rust";
        SaveSidecar.Sections["a"] = "{}";
        Assert.False(SaveSidecar.Read(save));
        Assert.Null(SaveSidecar.Language);
        Assert.Empty(SaveSidecar.Sections);
        Assert.Equal("java", SaveSidecar.EffectiveLanguage);
        Assert.False(SaveSidecar.FileFound);

        File.WriteAllText(Path.Combine(save, SaveSidecar.FileName), "{ \"language\": \"rust\", ");
        Assert.False(SaveSidecar.Read(save));
        Assert.Null(SaveSidecar.Language);

        File.WriteAllText(Path.Combine(save, SaveSidecar.FileName), "{\"language\":\"python\",\"future\":[1,{\"a\":[]}]}");
        Assert.True(SaveSidecar.Read(save));
        Assert.Equal("python", SaveSidecar.Language);
        Assert.Empty(SaveSidecar.Sections);
        Assert.False(SaveSidecar.Read(null));
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("[]", true)]
    [InlineData(" {\"a\": [1, -2.5e3, true, false, null, \"x\\n\\\"\"]} ", true)]
    [InlineData("\"x\"", true)]
    [InlineData("12", true)]
    [InlineData("{", false)]
    [InlineData("{\"a\" 1}", false)]
    [InlineData("[1,]", false)]
    [InlineData("01x", false)]
    [InlineData("tru", false)]
    [InlineData("", false)]
    public void MiniJson_Validates(string text, bool valid) => Assert.Equal(valid, MiniJson.IsValid(text));

    [Fact]
    public void MiniJson_ReadsUnicodeEscapes()
    {
        string json = "\"caf\\" + "u00" + "e9\"";
        Assert.True(MiniJson.TryReadString(json, out var s));
        Assert.Equal(4, s.Length);
        Assert.Equal((char)0xE9, s[3]);
    }
}
