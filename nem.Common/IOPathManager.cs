using System.Diagnostics.CodeAnalysis;

namespace nem.Common;

public static class IOPathManager
{
    public static IOPathManagerSystem System { get; } = new IOPathManagerSystem();

    /// <summary>
    /// The nem paths of a folder, whether or not it holds an env yet. Use this to
    /// create one ('nem init'); use <see cref="TryFindEnv"/> to work with an
    /// existing one.
    /// </summary>
    public static IOPathManagerLocal Local(string path)
    {
        return new IOPathManagerLocal(path);
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
            if (File.Exists(Local(dir).ConfigFilePath))
            {
                env = new IOPathManagerEnv(dir);
                return true;
            }
        }

        env = null;
        return false;
    }

    public class IOPathManagerLocal(string path)
    {
        public string DirPath { get => path; }

        public string ConfigFileName { get; } = "nem.json";
        public string ConfigFilePath { get => Path.Combine(path, ConfigFileName); }

        public string EnvDirName { get; } = ".nenv";
        public string EnvDirPath { get => Path.Combine(path, EnvDirName); }
        public string EnsureEnvDirPath() { if (!Directory.Exists(EnvDirPath)) Directory.CreateDirectory(EnvDirPath); return EnvDirPath; }
    }

    /// <summary>
    /// The paths of an env that was actually found. Only <see cref="TryFindEnv"/>
    /// creates one, so holding an instance is proof that the config file was
    /// there - no caller has to re-check, and no command can invent its own idea
    /// of where the env lives.
    /// </summary>
    public class IOPathManagerEnv : IOPathManagerLocal
    {
        internal IOPathManagerEnv(string dirPath) : base(dirPath) { }
    }

    public class IOPathManagerSystem
    {
        public string SystemDirName { get; } = "nem";
        public string DirPath { get => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), SystemDirName); }

        public string DownloadCacheDirName { get; } = "download";
        public string DownloadCacheDirPath { get => Path.Combine(DirPath, DownloadCacheDirName); }
        public string ExtractCacheDirName { get; } = "extract";
        public string ExtractCacheDirPath { get => Path.Combine(DirPath, ExtractCacheDirName); }
        public string ProxyDirName { get; } = "proxy";
        public string ProxyDirPath { get => Path.Combine(DirPath, ProxyDirName); }
    }
}
