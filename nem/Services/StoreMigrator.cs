using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace nem.Services;

/// <summary>One change of the store's layout: turns format <see cref="From"/> into the next one.</summary>
/// <param name="Apply">Idempotent, so a migration interrupted halfway can simply run again.</param>
public sealed record StoreMigration(int From, Action<EnvStore> Apply);

/// <summary>
/// Keeps the store's layout in step with this nem. The store records its format
/// (<see cref="MarkerFileName"/>); an older one is migrated step by step, a newer
/// one - written by a newer nem - is refused instead of being misread.
/// </summary>
public static class StoreMigrator
{
    public const string MarkerFileName = ".nem-store-version";

    /// <summary>
    /// The layout changes, oldest first. None yet: format 1 is the layout nem 1.4.0
    /// introduced, and the marker is what a future change migrates from.
    /// </summary>
    public static IReadOnlyList<StoreMigration> Steps { get; } = [];

    /// <summary>The store, migrated to <see cref="EnvStore.FormatVersion"/>.</summary>
    /// <exception cref="UnsupportedStoreVersionException">The store was written by a newer nem.</exception>
    public static EnvStore Open(EnvStore store)
    {
        Migrate(store, Steps, EnvStore.FormatVersion);
        return store;
    }

    internal static void Migrate(EnvStore store, IReadOnlyList<StoreMigration> steps, int currentVersion)
    {
        if (ReadVersion(store) == currentVersion)
            return;

        // Another nem may be migrating the same store right now; take turns.
        using InstallLock? formatLock = store.Lock(MarkerPath(store), EnvStore.InstallLockTimeout)
            ?? throw new IOException($"Gave up waiting for another nem to migrate the store at {store.StoreDir}.");

        // A store without a marker is either new or from nem 1.4.0/1.4.1, which
        // wrote format 1 without saying so.
        int version = ReadVersion(store) ?? (store.HoldsEntries ? 1 : currentVersion);
        if (version > currentVersion)
            throw new UnsupportedStoreVersionException(version, currentVersion, store.StoreDir);

        while (version < currentVersion)
        {
            StoreMigration step = steps.SingleOrDefault(s => s.From == version)
                ?? throw new InvalidOperationException($"nem has no step to migrate the store from format {version}.");
            step.Apply(store);
            version++;
            // After every step, so an interrupted migration resumes where it stopped.
            WriteVersion(store, version);
        }

        WriteVersion(store, version);
    }

    static string MarkerPath(EnvStore store) => Path.Combine(store.StoreDir, MarkerFileName);

    static int? ReadVersion(EnvStore store)
    {
        string marker = MarkerPath(store);
        return File.Exists(marker) && int.TryParse(File.ReadAllText(marker).Trim(), out int version) ? version : null;
    }

    static void WriteVersion(EnvStore store, int version)
    {
        Directory.CreateDirectory(store.StoreDir);
        File.WriteAllText(MarkerPath(store), version + "\n");
    }
}
