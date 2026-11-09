using DifFuse.Core.Models;

namespace DifFuse.Core.Library;

public sealed record LibraryBank(string Name, string Path);

public sealed record LibraryEntry(string Name, string Path, string BankName);

/// <summary>Outcome of importing one file.</summary>
public sealed record ImportItem(string SourcePath, LibraryEntry? Entry, string Message)
{
    public bool Imported => Entry is not null;
}

/// <summary>Moves files and folders out of the way. The app sends them to the Recycle Bin.</summary>
public interface IFileRecycler
{
    void Recycle(string path);
}

public sealed class RecycleBinRecycler : IFileRecycler
{
    public void Recycle(string path)
    {
        if (Directory.Exists(path))
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        else if (File.Exists(path))
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
    }
}

/// <summary>
/// The PC preset library: one folder per bank, one FUSE-format .fuse file per preset, so FUSE
/// files can be dropped straight in.
/// </summary>
public sealed class PresetLibrary
{
    public const string Extension = ".fuse";
    public const string DefaultBankName = "My Presets";

    private readonly IFileRecycler _recycler;

    public PresetLibrary(string root, IFileRecycler? recycler = null)
    {
        Root = root;
        _recycler = recycler ?? new RecycleBinRecycler();
    }

    public string Root { get; }

    public static string DefaultRoot => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DifFuse", "Library");

    public IReadOnlyList<LibraryBank> GetBanks()
    {
        Directory.CreateDirectory(Root);
        var banks = Directory.GetDirectories(Root)
            .Select(d => new LibraryBank(System.IO.Path.GetFileName(d), d))
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (banks.Count == 0)
        {
            banks.Add(CreateBank(DefaultBankName));
        }
        return banks;
    }

    public LibraryBank CreateBank(string name)
    {
        var safe = UniqueName(Root, SafeFileName(name, "Bank"), isDirectory: true);
        var path = System.IO.Path.Combine(Root, safe);
        Directory.CreateDirectory(path);
        return new LibraryBank(safe, path);
    }

    public LibraryBank RenameBank(LibraryBank bank, string newName)
    {
        var safe = SafeFileName(newName, bank.Name);
        if (string.Equals(safe, bank.Name, StringComparison.Ordinal)) return bank;
        if (!string.Equals(safe, bank.Name, StringComparison.OrdinalIgnoreCase))
        {
            safe = UniqueName(Root, safe, isDirectory: true);
        }
        var path = System.IO.Path.Combine(Root, safe);
        if (string.Equals(safe, bank.Name, StringComparison.OrdinalIgnoreCase))
        {
            // Case-only rename needs a hop through a temporary name on Windows.
            var temp = path + ".renaming";
            Directory.Move(bank.Path, temp);
            Directory.Move(temp, path);
        }
        else
        {
            Directory.Move(bank.Path, path);
        }
        return new LibraryBank(safe, path);
    }

    public void DeleteBank(LibraryBank bank) => _recycler.Recycle(bank.Path);

    public IReadOnlyList<LibraryEntry> GetPresets(LibraryBank bank)
    {
        if (!Directory.Exists(bank.Path)) return [];
        return Directory.GetFiles(bank.Path, "*" + Extension)
            .Select(f => new LibraryEntry(System.IO.Path.GetFileNameWithoutExtension(f), f, bank.Name))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public Preset Load(LibraryEntry entry)
    {
        var doc = FuseFile.Load(entry.Path);
        return doc.Preset ?? throw new InvalidDataException($"{entry.Name} has no amp settings.");
    }

    public string PathFor(LibraryBank bank, string presetName) =>
        System.IO.Path.Combine(bank.Path, SafeFileName(presetName, "Untitled") + Extension);

    public bool Exists(LibraryBank bank, string presetName) => File.Exists(PathFor(bank, presetName));

    /// <summary>Saves a preset into a bank. Throws when the name is taken and <paramref name="overwrite"/> is false.</summary>
    public LibraryEntry Save(LibraryBank bank, Preset preset, string author = "", bool overwrite = false)
    {
        Directory.CreateDirectory(bank.Path);
        var path = PathFor(bank, preset.Name);
        if (File.Exists(path) && !overwrite)
        {
            throw new IOException($"A preset called \"{preset.Name}\" is already in {bank.Name}.");
        }
        FuseFile.Save(path, preset, author);
        return new LibraryEntry(System.IO.Path.GetFileNameWithoutExtension(path), path, bank.Name);
    }

    public void Delete(LibraryEntry entry) => _recycler.Recycle(entry.Path);

    public LibraryEntry Rename(LibraryEntry entry, string newName)
    {
        var doc = FuseFile.Load(entry.Path);
        var preset = (doc.Preset ?? throw new InvalidDataException($"{entry.Name} has no amp settings.")) with { Name = newName };
        var bank = new LibraryBank(entry.BankName, System.IO.Path.GetDirectoryName(entry.Path)!);
        var target = PathFor(bank, newName);
        bool sameFile = string.Equals(target, entry.Path, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && File.Exists(target))
        {
            throw new IOException($"A preset called \"{newName}\" is already in {bank.Name}.");
        }
        FuseFile.Save(target, preset, doc.Author);
        if (!sameFile) File.Delete(entry.Path);
        return new LibraryEntry(System.IO.Path.GetFileNameWithoutExtension(target), target, bank.Name);
    }

    /// <summary>Copies FUSE files into a bank. Never overwrites: a taken name gets a number.</summary>
    public IReadOnlyList<ImportItem> Import(LibraryBank bank, IEnumerable<string> files)
    {
        var results = new List<ImportItem>();
        foreach (var file in files)
        {
            var fileName = System.IO.Path.GetFileName(file);
            try
            {
                var doc = FuseFile.Load(file);
                if (doc.Preset is null)
                {
                    results.Add(new ImportItem(file, null,
                        $"{fileName}: skipped. It holds only FUSE's name and notes for \"{doc.Name}\", no amp settings."));
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(doc.Name) ? System.IO.Path.GetFileNameWithoutExtension(file) : doc.Name;
                var unique = name;
                for (int n = 2; Exists(bank, unique); n++) unique = $"{name} ({n})";
                var entry = Save(bank, doc.Preset with { Name = unique }, doc.Author);
                var note = doc.Warnings.Count > 0 ? " " + string.Join(" ", doc.Warnings) : "";
                results.Add(new ImportItem(file, entry, $"{fileName}: imported as \"{unique}\".{note}"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                results.Add(new ImportItem(file, null, $"{fileName}: {ex.Message}"));
            }
        }
        return results;
    }

    public static string SafeFileName(string name, string fallback)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static string UniqueName(string parent, string name, bool isDirectory)
    {
        bool Taken(string n) => isDirectory
            ? Directory.Exists(System.IO.Path.Combine(parent, n))
            : File.Exists(System.IO.Path.Combine(parent, n));
        var unique = name;
        for (int i = 2; Taken(unique); i++) unique = $"{name} ({i})";
        return unique;
    }
}
