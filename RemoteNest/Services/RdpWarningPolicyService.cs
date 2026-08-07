using System.Diagnostics;
using System.Text.Json;
using RemoteNest.Serialization;

namespace RemoteNest.Services;

public enum RdpWarningPolicyStatus
{
    /// <summary>Windows defaults — every warning is shown.</summary>
    Default,
    /// <summary>Every supported warning is suppressed.</summary>
    Patched,
    /// <summary>Some values are patched and some are not (e.g. the admin part was declined).</summary>
    Partial
}

/// <summary>Where the pre-patch snapshot is persisted.</summary>
public interface IPolicyBackupStore
{
    string? Read();
    void Write(string value);
}

/// <summary>Production store — one entry inside settings.json.</summary>
public sealed class SettingsBackupStore : IPolicyBackupStore
{
    private const string Key = "rdpWarningBackup";
    public string? Read() => SettingsStore.Get(Key);
    public void Write(string value) => SettingsStore.Set(Key, value);
}

/// <summary>
/// Optional, fully reversible patch that suppresses the Remote Desktop client's security
/// prompts — including the redirection/anti-phishing dialog introduced by the April 2026
/// Windows update (<c>RedirectionWarningDialogVersion</c>, documented as the supported way
/// to restore the previous dialog behavior).
///
/// Every value is snapshotted before it is written — including "this value did not exist" —
/// so <see cref="RevertAsync"/> restores the machine to exactly its prior state instead of
/// to a guessed default.
///
/// Security note: these prompts exist to stop malicious .rdp files from silently
/// redirecting local drives, clipboard and credentials. Suppressing them is reasonable when
/// every connection is one the user configured in this app, and reckless when .rdp files
/// arrive by e-mail. The settings UI states this before the user opts in.
/// </summary>
public sealed class RdpWarningPolicyService
{
    /// <summary>A single registry value the patch owns.</summary>
    public sealed record PolicyValue(
        string Id,
        RegistryHiveKind Hive,
        string Path,
        string Name,
        int PatchedData)
    {
        public bool RequiresElevation => Hive == RegistryHiveKind.LocalMachine;
    }

    private const string AbsentMarker = "absent";
    private const string LocalDevicesPrefix = "LocalDevices:";
    private const string LocalDevicesPath = @"Software\Microsoft\Terminal Server Client\LocalDevices";
    private const string TerminalServerClientPath = @"Software\Microsoft\Terminal Server Client";

    /// <summary>Data Windows writes itself when a user ticks "don't ask me again" for local devices.</summary>
    private const int LocalDevicesTrustAll = 0x4D;

    /// <summary>The machine-wide and per-user values the patch always covers.</summary>
    public static readonly PolicyValue[] CoreValues =
    [
        // April 2026 update: new redirection / anti-phishing dialog. Setting this to 1
        // restores the pre-update behavior. Machine-wide policy -> needs admin.
        new("RedirectionWarningDialogVersion", RegistryHiveKind.LocalMachine,
            @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services\Client",
            "RedirectionWarningDialogVersion", 1),

        // "Do you want to connect?" consent prompt shown before an .rdp file launches.
        new("RdpLaunchConsentAccepted", RegistryHiveKind.CurrentUser,
            TerminalServerClientPath, "RdpLaunchConsentAccepted", 1),

        // "The identity of the remote computer cannot be verified" certificate warning.
        new("AuthenticationLevelOverride", RegistryHiveKind.CurrentUser,
            TerminalServerClientPath, "AuthenticationLevelOverride", 0),
    ];

    /// <summary>Per-host "do you trust this remote connection / local devices" entry.</summary>
    public static PolicyValue ForHost(string host) =>
        new($"{LocalDevicesPrefix}{host}", RegistryHiveKind.CurrentUser, LocalDevicesPath, host, LocalDevicesTrustAll);

    /// <summary>Instance the app uses: real registry, settings.json backup, UAC helper.</summary>
    public static RdpWarningPolicyService Default { get; } =
        new(RegistryAccessor.Instance, new SettingsBackupStore(), RunElevatedHelperAsync);

    private readonly IRegistryAccessor _registry;
    private readonly IPolicyBackupStore _backupStore;
    private readonly Func<string, Task<bool>> _elevatedRunner;

    public RdpWarningPolicyService(
        IRegistryAccessor registry,
        IPolicyBackupStore backupStore,
        Func<string, Task<bool>> elevatedRunner)
    {
        _registry = registry;
        _backupStore = backupStore;
        _elevatedRunner = elevatedRunner;
    }

    public RdpWarningPolicyStatus GetStatus()
    {
        var patched = CoreValues.Count(value => TryRead(value) == value.PatchedData);
        if (patched == 0) return RdpWarningPolicyStatus.Default;
        return patched == CoreValues.Length ? RdpWarningPolicyStatus.Patched : RdpWarningPolicyStatus.Partial;
    }

    /// <summary>
    /// Applies the patch. Per-user values are written in-process; the machine-wide policy
    /// value goes through an elevated helper (UAC prompt). Returns false when elevation is
    /// declined — the per-user part still applies, leaving the status Partial.
    /// </summary>
    public async Task<bool> ApplyAsync(IEnumerable<string> hosts)
    {
        var targets = CoreValues
            .Concat(hosts
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(ForHost))
            .ToList();

        // Snapshot first. Values already recorded keep their ORIGINAL entry, so a second
        // Apply can never overwrite the real pre-patch state with our own values.
        var backup = LoadBackup();
        foreach (var value in targets)
        {
            if (!backup.ContainsKey(value.Id))
                backup[value.Id] = TryRead(value)?.ToString() ?? AbsentMarker;
        }
        SaveBackup(backup);

        var elevatedOk = true;
        foreach (var value in targets)
        {
            if (value.RequiresElevation)
                elevatedOk &= await _elevatedRunner($"set:{value.PatchedData}").ConfigureAwait(false);
            else
                TryWrite(value, value.PatchedData);
        }

        Log.Info($"RDP warning patch applied to {targets.Count} value(s) (elevated part ok: {elevatedOk})");
        return elevatedOk;
    }

    /// <summary>
    /// Restores every recorded value to its original state, deleting the ones that did not
    /// exist before. Returns false when elevation is declined; in that case the machine-wide
    /// entry stays in the snapshot so a later attempt can finish the job.
    /// </summary>
    public async Task<bool> RevertAsync()
    {
        var backup = LoadBackup();
        if (backup.Count == 0)
        {
            // Nothing recorded (patched by hand, or settings.json lost) — fall back to
            // removing the values the patch owns, which is the Windows default state.
            foreach (var value in CoreValues)
                backup[value.Id] = AbsentMarker;
        }

        var elevatedOk = true;
        foreach (var (id, original) in backup)
        {
            var value = ResolveById(id);
            if (value is null) continue;

            if (value.RequiresElevation)
            {
                elevatedOk &= await _elevatedRunner(
                    original == AbsentMarker ? "delete" : $"set:{original}").ConfigureAwait(false);
            }
            else if (original == AbsentMarker)
            {
                TryDelete(value);
            }
            else if (int.TryParse(original, out var data))
            {
                TryWrite(value, data);
            }
        }

        if (elevatedOk)
        {
            SaveBackup(new Dictionary<string, string>());
            Log.Info($"RDP warning patch reverted ({backup.Count} value(s))");
        }
        else
        {
            // Keep only what still needs elevation, so retrying finishes the revert
            // without re-touching the per-user values we already restored.
            SaveBackup(backup
                .Where(kv => ResolveById(kv.Key)?.RequiresElevation == true)
                .ToDictionary(kv => kv.Key, kv => kv.Value));
            Log.Warn("RDP warning revert incomplete — elevation declined");
        }

        return elevatedOk;
    }

    /// <summary>
    /// Entry point for the elevated helper instance (<c>--rdp-policy set:1|delete</c>).
    /// Only ever touches the machine-wide policy value.
    /// </summary>
    public static int RunElevatedOperation(string argument, IRegistryAccessor? registry = null)
    {
        registry ??= RegistryAccessor.Instance;
        var policy = CoreValues.First(v => v.RequiresElevation);

        try
        {
            if (argument == "delete")
            {
                registry.DeleteValue(policy.Hive, policy.Path, policy.Name);
                return 0;
            }
            if (argument.StartsWith("set:", StringComparison.Ordinal)
                && int.TryParse(argument[4..], out var data))
            {
                registry.WriteDword(policy.Hive, policy.Path, policy.Name, data);
                return 0;
            }
            return 2; // unrecognized argument
        }
        catch (Exception ex)
        {
            Log.Error($"Elevated RDP policy operation '{argument}' failed", ex);
            return 1;
        }
    }

    internal static PolicyValue? ResolveById(string id)
    {
        var core = CoreValues.FirstOrDefault(v => v.Id == id);
        if (core is not null) return core;
        return id.StartsWith(LocalDevicesPrefix, StringComparison.Ordinal)
            ? ForHost(id[LocalDevicesPrefix.Length..])
            : null;
    }

    /// <summary>Relaunches this exe elevated to perform one machine-wide registry change.</summary>
    private static async Task<bool> RunElevatedHelperAsync(string argument)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas" // triggers the UAC prompt
            };
            psi.ArgumentList.Add("--rdp-policy");
            psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 1223 = the user cancelled the UAC prompt. Not an error worth surfacing.
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start elevated RDP policy helper", ex);
            return false;
        }
    }

    private int? TryRead(PolicyValue value)
    {
        try { return _registry.ReadDword(value.Hive, value.Path, value.Name); }
        catch (Exception ex) { Log.Warn($"Registry read failed for {value.Id}", ex); return null; }
    }

    private void TryWrite(PolicyValue value, int data)
    {
        try { _registry.WriteDword(value.Hive, value.Path, value.Name, data); }
        catch (Exception ex) { Log.Warn($"Registry write failed for {value.Id}", ex); }
    }

    private void TryDelete(PolicyValue value)
    {
        try { _registry.DeleteValue(value.Hive, value.Path, value.Name); }
        catch (Exception ex) { Log.Warn($"Registry delete failed for {value.Id}", ex); }
    }

    internal Dictionary<string, string> LoadBackup()
    {
        var raw = _backupStore.Read();
        if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize(raw, AppJsonContext.Default.DictionaryStringString)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private void SaveBackup(Dictionary<string, string> backup) =>
        _backupStore.Write(JsonSerializer.Serialize(backup, AppJsonContext.Default.DictionaryStringString));
}
