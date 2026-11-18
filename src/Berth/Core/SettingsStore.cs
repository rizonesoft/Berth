using Berth.Core.Model;

namespace Berth.Core;

/// <summary>
/// What <see cref="SettingsStore.Load"/> found. <see cref="Exists"/> is false only when there is no file, which is
/// what makes a first run (SPEC §17); an unreadable file exists, gives the defaults, and names its reason.
/// </summary>
internal sealed record SettingsLoad(Settings Value, bool Exists, string? Unreadable);

/// <summary><c>settings.json</c> under the data root (SPEC §14), read and written through <see cref="JsonFile"/>.</summary>
internal sealed class SettingsStore(BerthPaths paths)
{
    public SettingsLoad Load()
    {
        if (!JsonFile.TryRead(paths.SettingsFile, BerthJson.Default.Settings, out Settings? value, out string? reason))
        {
            return reason == JsonFile.Missing
                ? new SettingsLoad(Settings.Default, Exists: false, Unreadable: null)
                : new SettingsLoad(Settings.Default, Exists: true, Unreadable: reason);
        }

        string? problem = Problem(value!);
        return problem is null
            ? new SettingsLoad(value!, Exists: true, Unreadable: null)
            : new SettingsLoad(Settings.Default, Exists: true, Unreadable: problem);
    }

    /// <summary>Writes <paramref name="value"/>, unless the file on disk is there but unreadable.</summary>
    /// <exception cref="ArgumentException">The settings break the SPEC §14 schema.</exception>
    /// <exception cref="UnreadableSettingsException">The file on disk cannot be read, so it is kept.</exception>
    /// <exception cref="IOException">The folder or the file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or the file may not be written.</exception>
    public void Save(Settings value) => Save(value, beforeReplace: null);

    /// <summary>As <see cref="Save(Settings)"/>; <paramref name="beforeReplace"/> lets a test act just before the file is put in place.</summary>
    internal void Save(Settings value, Action? beforeReplace)
    {
        ArgumentNullException.ThrowIfNull(value);
        string? problem = Problem(value);
        if (problem is not null)
        {
            throw new ArgumentException($"These settings must not be saved: {problem}", nameof(value));
        }

        SettingsLoad current = Load();
        if (current.Unreadable is { } unreadable)
        {
            throw new UnreadableSettingsException(unreadable);
        }

        BerthPaths.EnsureFolderFor(paths.SettingsFile);
        if (current.Exists)
        {
            JsonFile.Write(paths.SettingsFile, value, BerthJson.Default.Settings, beforeReplace is null ? null : _ => beforeReplace());
            return;
        }

        try
        {
            JsonFile.WriteNew(paths.SettingsFile, value, BerthJson.Default.Settings, beforeReplace is null ? null : _ => beforeReplace());
        }
        catch (TargetAppearedException) when (Load().Unreadable is { } appeared)
        {
            // A file arrived while this first save was being made, and it cannot be read: keep it and say so.
            throw new UnreadableSettingsException(appeared);
        }
    }

    private static string? Problem(Settings settings)
    {
        if (settings.SchemaVersion != Settings.CurrentSchemaVersion)
        {
            return $"It has schemaVersion {settings.SchemaVersion}; this version of Berth reads {Settings.CurrentSchemaVersion}.";
        }

        return settings.Window is { } window && (window.Width <= 0 || window.Height <= 0)
            ? "Its window has no size."
            : null;
    }
}
