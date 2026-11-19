using System.IO;
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
    /// <summary>The full path to <c>settings.json</c> under the configured data root.</summary>
    public string FilePath => paths.SettingsFile;

    public SettingsLoad Load() => Load(out _, afterRecover: null, afterRead: null);

    /// <summary>As <see cref="Load()"/>; the two actions let a test interleave a save with the read.</summary>
    internal SettingsLoad Load(Action? afterRecover, Action? afterRead) => Load(out _, afterRecover, afterRead);

    private SettingsLoad Load(out byte[]? read, Action? afterRecover = null, Action? afterRead = null)
    {
        read = null;
        Settings? value;
        string? reason;
        try
        {
            if (JsonFile.TryReadSettled(paths.SettingsFile, BerthJson.Default.Settings, exactName: false, out value, out reason, out read, afterRecover, afterRead))
            {
                reason = null;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoad(Settings.Default, Exists: true, Unreadable: $"It could not be read as it was being saved: {error.Message}");
        }

        if (reason is not null)
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

    /// <summary>As <see cref="Save(Settings)"/>, with the writer's later seams (<see cref="JsonFile.WriteOver"/>) for a test.</summary>
    internal void Save(Settings value, Action? beforeReplace, Action? afterCheck, Action? afterReplace)
    {
        seams = (afterCheck, afterReplace);
        try
        {
            Save(value, beforeReplace);
        }
        finally
        {
            seams = (null, null);
        }
    }

    private (Action? AfterCheck, Action? AfterReplace) seams;

    /// <summary>
    /// Reads the current settled settings, applies <paramref name="change"/> to that exact snapshot, and writes it
    /// only if the file on disk still matches that snapshot when replaced.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is null.</exception>
    /// <exception cref="ArgumentException">The resulting settings are null or break the SPEC §14 schema.</exception>
    /// <exception cref="UnreadableSettingsException">The file on disk cannot be read, so it is kept.</exception>
    /// <exception cref="IOException">The folder or the file cannot be written, or another writer changed it first.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or the file may not be written.</exception>
    public SettingsLoad Update(Func<Settings, Settings> change) => Update(change, beforeReplace: null);

    /// <summary>As <see cref="Update(Func{Settings, Settings})"/>; <paramref name="beforeReplace"/> lets a test act just before the file is put in place.</summary>
    internal SettingsLoad Update(Func<Settings, Settings> change, Action? beforeReplace)
    {
        ArgumentNullException.ThrowIfNull(change);
        SettingsLoad current = Load(out byte[]? read);
        if (current.Unreadable is { } unreadable)
        {
            throw new UnreadableSettingsException(unreadable);
        }

        Settings value = change(current.Value)
            ?? throw new ArgumentException("These settings must not be null.", nameof(change));
        string? problem = Problem(value);
        if (problem is not null)
        {
            throw new ArgumentException($"These settings must not be saved: {problem}", nameof(change));
        }

        WriteLoaded(current.Exists, read, value, beforeReplace);
        return current;
    }

    /// <summary>As <see cref="Save(Settings)"/>; <paramref name="beforeReplace"/> lets a test act just before the file is put in place.</summary>
    internal void Save(Settings value, Action? beforeReplace)
    {
        ArgumentNullException.ThrowIfNull(value);
        string? problem = Problem(value);
        if (problem is not null)
        {
            throw new ArgumentException($"These settings must not be saved: {problem}", nameof(value));
        }

        SettingsLoad current = Load(out byte[]? read);
        if (current.Unreadable is { } unreadable)
        {
            throw new UnreadableSettingsException(unreadable);
        }

        WriteLoaded(current.Exists, read, value, beforeReplace);
    }

    private void WriteLoaded(bool exists, byte[]? read, Settings value, Action? beforeReplace)
    {
        BerthPaths.EnsureFolderFor(paths.SettingsFile);
        if (exists)
        {
            try
            {
                JsonFile.WriteOver(paths.SettingsFile, value, BerthJson.Default.Settings, read!, beforeReplace is null ? null : _ => beforeReplace(), seams.AfterCheck, seams.AfterReplace);
            }
            catch (TargetChangedException changed) when (changed.InnerException is IOException or UnauthorizedAccessException)
            {
                // It could not be read just before the replace (another program held it): that is the refusal.
                throw new UnreadableSettingsException(changed.InnerException.Message);
            }
            catch (TargetChangedException)
            {
                // Read again only once the writer has let go: an exception filter would run while its lock and its
                // note are still held, so a readable change would wait and then look unreadable.
                if (Load().Unreadable is { } arrived)
                {
                    // The file changed while it was being saved, into one that cannot be read: it was kept; say so.
                    throw new UnreadableSettingsException(arrived);
                }

                throw;
            }

            return;
        }

        try
        {
            JsonFile.WriteNew(paths.SettingsFile, value, BerthJson.Default.Settings, beforeReplace is null ? null : _ => beforeReplace());
        }
        catch (TargetAppearedException)
        {
            // Read again only once the writer has let go: an exception filter would run while its lock and its
            // note are still held, so a readable change would wait and then look unreadable.
            if (Load().Unreadable is { } appeared)
            {
                // A file arrived while this first save was being made, and it cannot be read: keep it and say so.
                throw new UnreadableSettingsException(appeared);
            }

            throw;
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
