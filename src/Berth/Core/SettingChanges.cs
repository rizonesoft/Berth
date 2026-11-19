using System.IO;

namespace Berth.Core;

/// <summary>The outcome of a setting toggle after persistence or rollback (SPEC §11, §18).</summary>
internal sealed record SettingChange(bool Applied, bool Value, string? Message);

/// <summary>
/// Applies, reads back and logs Berth's settings changes in one place so the tray menu and main window share exact
/// rollback state and SPEC §18 log lines.
/// </summary>
internal sealed class SettingChanges
{
    private const int MaxSaveAttempts = 3;
    private readonly SettingsStore store;
    private readonly Log log;

    public SettingChanges(SettingsStore store, Log log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);
        this.store = store;
        this.log = log;
    }

    /// <summary>Runs before each update attempt; tests use it to inject concurrent edits or corruption.</summary>
    internal Action<int>? BeforeSave { get; init; }

    /// <summary>Runs inside <see cref="SettingsStore.Update(Func{Model.Settings, Model.Settings}, Action?)"/> before file replacement.</summary>
    internal Action<int>? BeforeReplace { get; init; }

    /// <summary>Runs after a save finishes and before the post-save read-back check.</summary>
    internal Action? AfterSave { get; init; }

    /// <summary>
    /// Updates <c>restoreAutomatically</c> to <paramref name="on"/> while preserving other fields from the saved
    /// snapshot, verifies persistence by reading <c>settings.json</c> back, and logs one SPEC §18 line.
    /// </summary>
    public SettingChange SetRestoreAutomatically(bool on, bool? current = null)
    {
        SettingsLoad before = store.Load();
        bool rollback = current ?? !on;
        if (before.Unreadable is not null)
        {
            log.Fail(Messages.UnreadableSettings);
            return new SettingChange(Applied: false, Value: rollback, Message: Messages.UnreadableSettings);
        }

        if (before.Exists)
        {
            rollback = before.Value.RestoreAutomatically;
        }

        for (int attempt = 1; attempt <= MaxSaveAttempts; attempt++)
        {
            BeforeSave?.Invoke(attempt);
            try
            {
                _ = store.Update(
                    s => s with { RestoreAutomatically = on },
                    BeforeReplace is null ? null : () => BeforeReplace(attempt),
                    loaded =>
                    {
                        if (loaded.Exists)
                        {
                            rollback = loaded.Value.RestoreAutomatically;
                        }
                    });
                break;
            }
            catch (UnreadableSettingsException)
            {
                log.Fail(Messages.UnreadableSettings);
                return new SettingChange(Applied: false, Value: rollback, Message: Messages.UnreadableSettings);
            }
            catch (Exception race) when (race is TargetChangedException or TargetAppearedException)
            {
                SettingsLoad latest = store.Load();
                if (latest.Exists && latest.Unreadable is null)
                {
                    rollback = latest.Value.RestoreAutomatically;
                }

                if (attempt < MaxSaveAttempts)
                {
                    continue;
                }

                if (latest.Unreadable is not null)
                {
                    log.Fail(Messages.UnreadableSettings);
                    return new SettingChange(Applied: false, Value: rollback, Message: Messages.UnreadableSettings);
                }

                string raceMessage = Messages.WriteFailed(store.FilePath, race.Message ?? string.Empty);
                log.Fail(raceMessage);
                return new SettingChange(Applied: false, Value: rollback, Message: raceMessage);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                SettingsLoad latest = store.Load();
                if (latest.Exists && latest.Unreadable is null)
                {
                    rollback = latest.Value.RestoreAutomatically;
                }

                string writeMessage = Messages.WriteFailed(store.FilePath, error.Message ?? string.Empty);
                log.Fail(writeMessage);
                return new SettingChange(Applied: false, Value: rollback, Message: writeMessage);
            }
        }

        AfterSave?.Invoke();
        SettingsLoad after = store.Load();
        if (after.Unreadable is not null || !after.Exists || after.Value.RestoreAutomatically != on)
        {
            if (after.Exists && after.Unreadable is null)
            {
                rollback = after.Value.RestoreAutomatically;
            }

            string reason = after.Unreadable ?? "The settings file read back after saving did not hold the new value.";
            string verifyMessage = Messages.WriteFailed(store.FilePath, reason);
            log.Fail(verifyMessage);
            return new SettingChange(Applied: false, Value: rollback, Message: verifyMessage);
        }

        log.Info(Messages.SettingChanged(Messages.RestoreAutomaticallySetting, on));
        return new SettingChange(Applied: true, Value: on, Message: null);
    }

    /// <summary>
    /// Logs and returns the outcome of a "Start with Windows" registry update owned by the desktop layer.
    /// </summary>
    public SettingChange RecordStartWithWindows(bool on, Exception? failure)
    {
        if (failure is null)
        {
            log.Info(Messages.SettingChanged(Messages.StartWithWindowsSetting, on));
            return new SettingChange(Applied: true, Value: on, Message: null);
        }

        string message = Messages.StartWithWindowsFailed(failure.Message ?? string.Empty);
        log.Fail(message);
        return new SettingChange(Applied: false, Value: !on, Message: message);
    }
}
