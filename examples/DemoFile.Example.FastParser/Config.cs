using System;
using System.IO;
using System.Text.Json;

public class Config
{
    public class ProjectConfig
    {
        public string directory { get; set; }
        public string public_demos_directory { get; set; }
        public string KillCollectionParse { get; set; }
        public string TickByTickParse { get; set; }
    }

    public class ProjectSettings
    {
        public ProjectConfig project { get; set; }
    }

    private static ProjectSettings settings;

    public static ProjectSettings LoadConfig()
    {
        if (settings != null)
        {
            return settings;
        }

        var configPath = Path.Combine("C:", "demofetch", "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file not found at: {configPath}");
        }

        var jsonString = File.ReadAllText(configPath);
        settings = JsonSerializer.Deserialize<ProjectSettings>(jsonString);
        return settings;
    }
}
