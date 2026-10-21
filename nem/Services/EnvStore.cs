using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using nem.Common;

namespace nem.Services;

/// <summary>
/// The store every env of this user shares: one entry per Node version and one
/// per tool version (per Node major version, as native addons are built for one).
/// A project carries only its nem.jsonc; what it declares is looked up here.
/// <para>
/// An entry counts once its <see cref="CompleteMarker"/> exists, which an install
/// writes last - so "installed" is a file check, and a crashed install never
/// passes for a finished one. Every tool start refreshes the entries it uses
/// (<see cref="LastUsedFile"/>); <see cref="Prune"/> removes the ones nobody used
/// for long. That needs no list of envs: an env that comes back after its entries
/// were pruned simply installs them again on its next tool start.
/// </para>
/// </summary>
public sealed class EnvStore
{
    public const string CompleteMarker = ".nem-complete";
    public const string LastUsedFile = ".nem-last-used";

    // How often a tool start refreshes an entry's last-used time. Pruning works in
    // months, so once a day is plenty and keeps tool starts from writing every time.
    static readonly TimeSpan TouchInterval = TimeSpan.FromDays(1);

    /// <summary>
    /// How long an install waits for another one working on the same entry. A large
    /// tool over a slow connection takes minutes; longer than this, the other hangs.
    /// </summary>
    public static readonly TimeSpan InstallLockTimeout = TimeSpan.FromMinutes(15);

    // How many times a move or delete is retried, and how long to wait in between:
    // on Windows a virus scanner often still holds freshly extracted files.
    const int FileSystemRetries = 20;
    static readonly TimeSpan FileSystemRetryDelay = TimeSpan.FromMilliseconds(250);

    public EnvStore(string storeDir, string globalsDir)
    {
        StoreDir = storeDir;
        GlobalsDir = globalsDir;
    }

    /// <summary>The store in the user's nem folder.</summary>
    public static EnvStore Default => new(IOPathManager.System.StoreDirPath, IOPathManager.System.GlobalsDirPath);

    public string StoreDir { get; }
    public string GlobalsDir { get; }

    string NodeRoot => Path.Combine(StoreDir, "node");
    string ToolsRoot => Path.Combine(StoreDir, "tools");
    string TempRoot => Path.Combine(StoreDir, ".tmp");
    string LocksRoot => Path.Combine(StoreDir, ".locks");

    public string NodeEntryDir(string version) => Path.Combine(NodeRoot, version);

    /// <summary>A tool entry; '/' of a scoped name becomes '+' so the name stays one folder.</summary>
    public string ToolEntryDir(string packageName, string version, int nodeMajor) =>
        Path.Combine(ToolsRoot, $"{packageName.Replace('/', '+')}@{version}-node{nodeMajor}");

    /// <summary>The npm prefix for 'npm install -g' inside envs on this Node major version.</summary>
    public string GlobalsDirFor(int nodeMajor) => Path.Combine(GlobalsDir, $"node{nodeMajor}");

    public static bool IsComplete(string entryDir) => File.Exists(Path.Combine(entryDir, CompleteMarker));

    /// <summary>Marks a finished install; from now on the entry counts as installed.</summary>
    public static void MarkComplete(string entryDir, DateTime now)
    {
        File.WriteAllText(Path.Combine(entryDir, CompleteMarker), "");
        Touch(entryDir, now, force: true);
    }

    /// <summary>The Node versions the store holds completely.</summary>
    public IReadOnlyList<string> CompleteNodeVersions() =>
        Directory.Exists(NodeRoot)
            ? Directory.EnumerateDirectories(NodeRoot).Where(IsComplete).Select(Path.GetFileName).OfType<string>().ToList()
            : [];

    /// <summary>Takes the install lock of one entry (see <see cref="InstallLock"/>).</summary>
    public InstallLock? Lock(string entryDir, TimeSpan timeout, Action? onWait = null) =>
        InstallLock.AcquireFile(InstallLock.LockFilePath(LocksRoot, entryDir), timeout, onWait);

    /// <summary>A new, empty folder on the store's volume, so it can be moved into place.</summary>
    public string NewTempDir()
    {
        string dir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---------- last used ----------

    /// <summary>
    /// When the entry was last used: the last-used file, else the completion
    /// marker, else the folder itself (an install that never finished).
    /// </summary>
    public static DateTime LastUsed(string entryDir)
    {
        foreach (string name in new[] { LastUsedFile, CompleteMarker })
        {
            string path = Path.Combine(entryDir, name);
            if (File.Exists(path))
                return File.GetLastWriteTimeUtc(path);
        }
        return Directory.GetLastWriteTimeUtc(entryDir);
    }

    /// <summary>
    /// Records that the entry is in use. Best effort: a tool start must not fail
    /// because the store is read-only or another start writes the same file.
    /// </summary>
    public static void Touch(string entryDir, DateTime now, bool force = false)
    {
        try
        {
            if (!force && IsFresh(LastUsed(entryDir), now))
                return;
            string path = Path.Combine(entryDir, LastUsedFile);
            File.WriteAllText(path, "");
            File.SetLastWriteTimeUtc(path, now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The next tool start tries again.
        }
    }

    internal static bool IsFresh(DateTime lastUsed, DateTime now) => now - lastUsed < TouchInterval;

    // ---------- prune ----------

    /// <summary>One store entry and when it was last used.</summary>
    public sealed record Entry(string Dir, string Kind, string Name, DateTime LastUsed, bool Complete);

    /// <summary>Every entry in the store, Node versions first.</summary>
    public IReadOnlyList<Entry> Entries()
    {
        var entries = new List<Entry>();
        foreach ((string root, string kind) in new[] { (NodeRoot, "node"), (ToolsRoot, "tool") })
        {
            if (!Directory.Exists(root))
                continue;
            foreach (string dir in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
                entries.Add(new Entry(dir, kind, Path.GetFileName(dir), LastUsed(dir), IsComplete(dir)));
        }
        return entries;
    }

    /// <summary>The entries nobody used within <paramref name="maxAge"/>.</summary>
    public static IReadOnlyList<Entry> SelectUnused(IEnumerable<Entry> entries, DateTime now, TimeSpan maxAge) =>
        entries.Where(entry => now - entry.LastUsed > maxAge).ToList();

    /// <summary>What a prune removed, and what it had to leave because it was in use.</summary>
    public sealed record PruneResult(IReadOnlyList<Entry> Removed, IReadOnlyList<Entry> Busy);

    /// <summary>
    /// Removes every entry not used within <paramref name="maxAge"/>. An entry an
    /// install holds right now is left for the next prune.
    /// </summary>
    public PruneResult Prune(DateTime now, TimeSpan maxAge)
    {
        var removed = new List<Entry>();
        var busy = new List<Entry>();

        foreach (Entry entry in SelectUnused(Entries(), now, maxAge))
        {
            using InstallLock? entryLock = Lock(entry.Dir, TimeSpan.Zero);
            // Checked again under the lock: a tool start may have used it meanwhile.
            if (entryLock == null || !Directory.Exists(entry.Dir) || now - LastUsed(entry.Dir) <= maxAge)
            {
                busy.Add(entry);
                continue;
            }

            if (TryDelete(entry.Dir))
                removed.Add(entry);
            else
                busy.Add(entry);
        }

        ClearTemp(now);
        return new PruneResult(removed, busy);
    }

    /// <summary>Removes leftovers of interrupted installs and deletes, once they are a day old.</summary>
    void ClearTemp(DateTime now)
    {
        if (!Directory.Exists(TempRoot))
            return;
        foreach (string dir in Directory.EnumerateDirectories(TempRoot))
        {
            if (!IsFresh(Directory.GetLastWriteTimeUtc(dir), now))
                TryDeleteInPlace(dir);
        }
    }

    /// <summary>
    /// Deletes an entry: first moved out of the way, so a half-deleted entry is never
    /// seen under its name, then removed. What cannot be removed yet (a file still
    /// open) stays in the temp folder for a later prune.
    /// </summary>
    public bool TryDelete(string entryDir)
    {
        string trash = Path.Combine(TempRoot, "trash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);
        if (!TryMoveDirectory(entryDir, trash))
            return false;
        TryDeleteInPlace(trash);
        return true;
    }

    /// <summary>Moves a folder, retrying while a scanner still holds files in it.</summary>
    public static bool TryMoveDirectory(string from, string to)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return true;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < FileSystemRetries)
            {
                Thread.Sleep(FileSystemRetryDelay);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    static void TryDeleteInPlace(string dir)
    {
        try
        {
            // npm leaves read-only files now and then; Windows refuses to delete those.
            if (OperatingSystem.IsWindows())
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the next prune.
        }
    }
}
