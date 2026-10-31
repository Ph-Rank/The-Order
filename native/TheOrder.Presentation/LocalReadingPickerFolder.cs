using TheOrder.Domain;

namespace TheOrder.Presentation;

public static class LocalReadingPickerFolder
{
    public static string? Select(LibraryEntry entry, string? currentInstallationId, Func<string, bool> exists)
    {
        var ordered = entry.Installations.OrderBy(i => i.Id == currentInstallationId ? 0 : i.Id == entry.PreferredInstallationId ? 1 : 2);
        return ordered.Select(i => i.Folder).FirstOrDefault(folder => !string.IsNullOrWhiteSpace(folder) && exists(folder));
    }
}
