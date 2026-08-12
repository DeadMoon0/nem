namespace nem.Common;

public static class IOPathManager
{
    public static IOPathManagerSystem System { get; } = new IOPathManagerSystem();

    public static IOPathManagerLocal Local(string path)
    {
        return new IOPathManagerLocal(path);
    }

    public class IOPathManagerLocal(string path)
    {
        public string ConfigFileName { get; } = "nem.json";
        public string ConfigFilePath { get => Path.Combine(path, ConfigFileName); }

        public string EnvDirName { get; } = ".nenv";
        public string EnvDirPath { get => Path.Combine(path, EnvDirName); }
        public IOPathManagerLocalEnvDir EnvDir { get => new IOPathManagerLocalEnvDir(EnvDirPath); }

        public class IOPathManagerLocalEnvDir(string envPath)
        {
            public string NodeDirName { get; } = "node";
            public string NodeDirPath { get => Path.Combine(envPath, NodeDirName); }
        }
    }

    public class IOPathManagerSystem
    {
        public string SystemDirName { get; } = "nem";
        public string DirPath { get => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), SystemDirName); }

        public string DownloadCacheDirName { get; } = "download";
        public string DownloadCacheDirPath { get => Path.Combine(DirPath, DownloadCacheDirName); }
        public string ExtractCacheDirName { get; } = "extract";
        public string ExtractCacheDirPath { get => Path.Combine(DirPath, ExtractCacheDirName); }
    }
}