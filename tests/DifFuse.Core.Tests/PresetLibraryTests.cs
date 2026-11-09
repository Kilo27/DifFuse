using DifFuse.Core.Library;
using DifFuse.Core.Models;

namespace DifFuse.Core.Tests;

public sealed class PresetLibraryTests : IDisposable
{
    private sealed class DeletingRecycler : IFileRecycler
    {
        public List<string> Recycled { get; } = new();

        public void Recycle(string path)
        {
            Recycled.Add(path);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else File.Delete(path);
        }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "DifFuseTests", Guid.NewGuid().ToString("N"));
    private readonly DeletingRecycler _recycler = new();
    private readonly PresetLibrary _library;

    public PresetLibraryTests()
    {
        _library = new PresetLibrary(_root, _recycler);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Preset P(string name, byte gain = 100) => Preset.Default with
    {
        Name = name,
        Amp = new AmpSettings { Gain = gain },
    };

    [Fact]
    public void A_new_library_has_one_bank()
    {
        var banks = _library.GetBanks();
        Assert.Single(banks);
        Assert.Equal(PresetLibrary.DefaultBankName, banks[0].Name);
        Assert.True(Directory.Exists(banks[0].Path));
    }

    [Fact]
    public void Banks_get_unique_safe_names()
    {
        var a = _library.CreateBank("Live: Set 1");
        var b = _library.CreateBank("Live: Set 1");
        Assert.Equal("Live_ Set 1", a.Name);
        Assert.Equal("Live_ Set 1 (2)", b.Name);
        Assert.Equal(2, _library.GetBanks().Count);
    }

    [Fact]
    public void Saved_presets_load_back()
    {
        var bank = _library.CreateBank("Gig");
        var entry = _library.Save(bank, P("Crunch", 150));
        Assert.Equal("Crunch", entry.Name);
        Assert.EndsWith(".fuse", entry.Path);

        var loaded = _library.Load(_library.GetPresets(bank).Single());
        Assert.Equal(150, loaded.Amp.Gain);
        Assert.Equal("Crunch", loaded.Name);
    }

    [Fact]
    public void Saving_over_an_existing_name_needs_overwrite()
    {
        var bank = _library.CreateBank("Gig");
        _library.Save(bank, P("Crunch", 1));
        Assert.Throws<IOException>(() => _library.Save(bank, P("Crunch", 2)));

        _library.Save(bank, P("Crunch", 3), overwrite: true);
        Assert.Equal(3, _library.Load(_library.GetPresets(bank).Single()).Amp.Gain);
    }

    [Fact]
    public void Presets_are_listed_by_name()
    {
        var bank = _library.CreateBank("Gig");
        _library.Save(bank, P("b"));
        _library.Save(bank, P("A"));
        _library.Save(bank, P("c"));
        Assert.Equal(["A", "b", "c"], _library.GetPresets(bank).Select(e => e.Name));
    }

    [Fact]
    public void Delete_sends_files_and_banks_to_the_recycler()
    {
        var bank = _library.CreateBank("Old");
        var entry = _library.Save(bank, P("Gone"));
        _library.Delete(entry);
        Assert.Contains(entry.Path, _recycler.Recycled);
        Assert.Empty(_library.GetPresets(bank));

        _library.DeleteBank(bank);
        Assert.Contains(bank.Path, _recycler.Recycled);
        Assert.False(Directory.Exists(bank.Path));
    }

    [Fact]
    public void Rename_moves_the_file_and_updates_the_stored_name()
    {
        var bank = _library.CreateBank("Gig");
        var entry = _library.Save(bank, P("Old name"));
        var renamed = _library.Rename(entry, "New name");
        Assert.False(File.Exists(entry.Path));
        Assert.Equal("New name", _library.Load(renamed).Name);
        Assert.Throws<IOException>(() => _library.Rename(_library.Save(bank, P("Other")), "New name"));
    }

    [Fact]
    public void Rename_bank_keeps_its_presets()
    {
        var bank = _library.CreateBank("Gig");
        _library.Save(bank, P("One"));
        var renamed = _library.RenameBank(bank, "Studio");
        Assert.Equal("Studio", renamed.Name);
        Assert.Single(_library.GetPresets(renamed));

        var caseOnly = _library.RenameBank(renamed, "STUDIO");
        Assert.Equal("STUDIO", caseOnly.Name);
        Assert.Single(_library.GetPresets(caseOnly));
    }

    [Fact]
    public void Import_copies_presets_skips_name_only_files_and_never_overwrites()
    {
        var bank = _library.CreateBank("Imported");
        var source = Path.Combine(_root, "_source");
        Directory.CreateDirectory(source);

        var full = Path.Combine(source, "lead.fuse");
        FuseFile.Save(full, P("Lead", 222), "Kilo");
        var nameOnly = Path.Combine(source, "M2_00.fuse");
        File.WriteAllText(nameOnly, """<?xml version="1.0" encoding="utf-8"?><FUSE><Info name="Night Drive" author="Fender FUSE" /></FUSE>""");
        var broken = Path.Combine(source, "broken.fuse");
        File.WriteAllText(broken, "<Preset><Amp");

        var first = _library.Import(bank, [full, nameOnly, broken]);
        Assert.True(first[0].Imported);
        Assert.False(first[1].Imported);
        Assert.Contains("no amp settings", first[1].Message);
        Assert.False(first[2].Imported);

        var second = _library.Import(bank, [full]);
        Assert.Equal("Lead (2)", second[0].Entry!.Name);
        Assert.Equal(["Lead", "Lead (2)"], _library.GetPresets(bank).Select(e => e.Name));
        Assert.Equal(222, _library.Load(second[0].Entry!).Amp.Gain);
    }

    [Theory]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("   ", "fallback")]
    [InlineData("name...", "name")]
    public void File_names_are_made_safe(string input, string expected)
    {
        Assert.Equal(expected, PresetLibrary.SafeFileName(input, "fallback"));
    }
}
