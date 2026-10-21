using System.Diagnostics.CodeAnalysis;

namespace nem.Common;

public static class IOPathManager
{
    // Lazy, so a missing config folder fails the commands that need it with a
    // message, not every use of IOPathManager with a type-initializer error.
    static readonly Lazy<IOPathManagerSystem> _system = new(() => new IOPathManagerSystem(UserConfigDir()));

    public static IOPathManagerSystem System => _system.Value;

    /// <summary>
    /// The per-user config folder (%APPDATA% on Windows, $XDG_CONFIG_HOME or
    /// ~/.config on Unix). DoNotVerify: on Unix the default option returns ""
    /// while ~/.config does not exist yet, which on a fresh account made every
    /// nem path relative to the current directory.
    /// </summary>
    static string UserConfigDir() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>
    /// The config file names nem accepts, the preferred one first. A new env gets
    /// '.jsonc' because the file is hand-edited and may carry comments; 'nem.json'
    /// stays readable so envs created before that keep working untouched.
    /// </summary>
    public static IReadOnlyList<string> ConfigFileNames { get; } = ["nem.jsonc", "nem.json"];

    /// <summary>The accepted names as one phrase for messages, e.g. "nem.jsonc or nem.json".</summary>
    public static string ConfigFileNamesText { get; } = string.Join(" or ", ConfigFileNames);

    /// <summary>
    /// The nem paths of a folder, whether or not it holds an env yet. Use this to
    /// create one ('nem init'); use <see cref="TryGetEnv"/> or <see cref="TryFindEnv"/>
    /// to work with an existing one.
    /// </summary>
    public static IOPathManagerLocal Local(string path)
    {
        return new IOPathManagerLocal(path);
    }

    /// <summary>
    /// The env <paramref name="dirPath"/> itself holds, if any (relative paths
    /// resolve against the current directory). This is the one place that decides
    /// which of the <see cref="ConfigFileNames"/> a project actually uses, so no
    /// caller has to spell out the fallback.
    /// </summary>
    public static bool TryGetEnv(string dirPath, [NotNullWhen(true)] out IOPathManagerEnv? env)
    {
        string fullDirPath = Path.GetFullPath(dirPath);

        foreach (string configFileName in ConfigFileNames)
        {
            if (File.Exists(Path.Combine(fullDirPath, configFileName)))
            {
                env = new IOPathManagerEnv(fullDirPath, configFileName);
                return true;
            }
        }

        env = null;
        return false;
    }

    /// <summary>
    /// Walks up from <paramref name="startDirPath"/> (relative paths resolve
    /// against the current directory) until a config file turns up. The closest
    /// one wins, so a nested env shadows the one above it.
    /// This is the only way to obtain an <see cref="IOPathManagerEnv"/>, so every
    /// command that uses an env finds it the same way.
    /// </summary>
    public static bool TryFindEnv(string startDirPath, [NotNullWhen(true)] out IOPathManagerEnv? env)
    {
        for (string? dir = Path.GetFullPath(startDirPath); dir != null; dir = Path.GetDirectoryName(dir))
        {
            if (TryGetEnv(dir, out env))
                return true;
        }

        env = null;
        return false;
    }

    public class IOPathManagerLocal(string path)
    {
        public string DirPath { get => path; }

        /// <summary>
        /// The name a config created here gets. An <see cref="IOPathManagerEnv"/>
        /// reports the name it actually found instead, so writing back to
        /// <see cref="ConfigFilePath"/> never renames an existing config.
        /// </summary>
        public virtual string ConfigFileName { get => ConfigFileNames[0]; }
        public string ConfigFilePath { get => Path.Combine(path, ConfigFileName); }

        public string EnvDirName { get; } = ".nenv";
        public string EnvDirPath { get => Path.Combine(path, EnvDirName); }
        /// <summary>
        /// Creates the env folder if needed and makes sure git ignores it, so every
        /// command that materializes an env leaves it out of version control.
        /// </summary>
        public string EnsureEnvDirPath()
        {
            Directory.CreateDirectory(EnvDirPath);
            EnsureEnvDirIgnoredByGit(EnvDirPath);
            return EnvDirPath;
        }
    }

    /// <summary>
    /// Writes a .gitignore that ignores everything into the env folder. The env
    /// ignores itself, so it stays out of git with or without a project .gitignore,
    /// including when the repository is only created after 'nem init'.
    /// </summary>
    public static void EnsureEnvDirIgnoredByGit(string envDirPath)
    {
        string path = Path.Combine(envDirPath, ".gitignore");
        if (!File.Exists(path))
            File.WriteAllText(path, "# Created by nem: this env is local, 'nem install' rebuilds it from nem.jsonc.\n*\n");
    }

    /// <summary>
    /// The paths of an env that was actually found. Only <see cref="TryGetEnv"/> and
    /// <see cref="TryFindEnv"/> create one, so holding an instance is proof that the
    /// config file was there - no caller has to re-check, and no command can invent
    /// its own idea of where the env lives or which config name it uses.
    /// </summary>
    public class IOPathManagerEnv : IOPathManagerLocal
    {
        internal IOPathManagerEnv(string dirPath, string configFileName) : base(dirPath)
            => ConfigFileName = configFileName;

        /// <summary>The config file that was found, not the name a new one would get.</summary>
        public override string ConfigFileName { get; }
    }

    /// <summary>
    /// The machine-wide nem folders (caches, proxies) below the user's config folder.
    /// </summary>
    public class IOPathManagerSystem
    {
        readonly string _userConfigDir;

        /// <param name="userConfigDir">
        /// The user's config folder. It must be an absolute path: a relative one would
        /// put the proxy dir (and the PATH entry 'nem setup' writes) relative to
        /// wherever nem happens to run.
        /// </param>
        public IOPathManagerSystem(string userConfigDir)
        {
            if (!Path.IsPathFullyQualified(userConfigDir))
                throw new InvalidOperationException(
                    $"Cannot determine the user config folder (got '{userConfigDir}'). Set HOME or XDG_CONFIG_HOME to an absolute path.");
            _userConfigDir = userConfigDir;
        }

        public string SystemDirName { get; } = "nem";
        public string DirPath { get => Path.Combine(_userConfigDir, SystemDirName); }

        public string DownloadCacheDirName { get; } = "download";
        public string DownloadCacheDirPath { get => Path.Combine(DirPath, DownloadCacheDirName); }
        public string ExtractCacheDirName { get; } = "extract";
        public string ExtractCacheDirPath { get => Path.Combine(DirPath, ExtractCacheDirName); }
        public string ProxyDirName { get; } = "proxy";
        public string ProxyDirPath { get => Path.Combine(DirPath, ProxyDirName); }
        public string LockDirName { get; } = "locks";
        public string LockDirPath { get => Path.Combine(DirPath, LockDirName); }
    }
}
