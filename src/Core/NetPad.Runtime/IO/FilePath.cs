using System.IO;

namespace NetPad.IO;

/// <summary>
/// An absolute path to a file.
/// </summary>
public record FilePath(string Path) : AbsolutePath(Path)
{
    public virtual bool Equals(FilePath? other) =>
        Path.Equals(other?.Path,
            PlatformUtil.IsOSWindows()
                ? StringComparison.InvariantCultureIgnoreCase
                : StringComparison.InvariantCulture);

    public override int GetHashCode() => Path.ToLowerInvariant().GetHashCode();

    public static implicit operator FilePath(string name) => new(name);

    public FileInfo GetInfo() => new(Path);

    public FilePath Combine(params string[] paths) => System.IO.Path.Combine(paths.Prepend(Path).ToArray());

    public override bool Exists() => File.Exists(Path);

    public override void DeleteIfExists()
    {
        if (Exists())
        {
            File.Delete(Path);
        }
    }
}
