using System;
using System.IO;
using System.Threading.Tasks;
using SharedTypes;

public static class FileProcessor
{
    public static async Task ProcessFile(string inputPath, string outputPath = null)
    {
        if (string.IsNullOrEmpty(inputPath))
        {
            throw new ArgumentException("Input path is required");
        }

        var extension = Path.GetExtension(inputPath).ToLower();
        switch (extension)
        {
            case ".dem":
                await ProcessDemoFile(inputPath, outputPath);
                break;
            case ".csv":
                var csvType = GetCsvType(inputPath);
                switch (csvType)
                {
                    case "DEMO_INFO":
                        await ProcessKillCollectionCsv(inputPath, outputPath);
                        break;
                    case "KILL_COLLECTION":
                        await POVWriter.RunParser(inputPath, outputPath);
                        break;
                    default:
                        throw new InvalidOperationException($"CSV file has unknown header type: {csvType}");
                }
                break;
            default:
                throw new InvalidOperationException($"Unsupported file type: {extension}");
        }
    }

    private static string GetCsvType(string csvPath)
    {
        using (var reader = new StreamReader(csvPath, System.Text.Encoding.UTF8, true))
        {
            var firstLine = reader.ReadLine()?.Trim().Replace("\uFEFF", "");  // Remove BOM if present
            if (firstLine == "[DEMO_INFO]")
                return "DEMO_INFO";
            if (firstLine == "[KILL_COLLECTION]")
                return "KILL_COLLECTION";
            return "UNKNOWN";
        }
    }

    private static async Task ProcessDemoFile(string demoPath, string outputPath)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            var config = Config.LoadConfig();
            var demoName = Path.GetFileNameWithoutExtension(demoPath);
            var outputDir = config.project.KillCollectionParse;
            Directory.CreateDirectory(outputDir);
            outputPath = Path.Combine(outputDir, $"{demoName}.csv");
        }

        Console.WriteLine($"Processing demo: {demoPath}");
        Console.WriteLine($"Output file: {outputPath}");

        await KillCollectionWriter.ParseDemo(demoPath, outputPath);

        var fileInfo = new FileInfo(outputPath);
        Console.WriteLine($"Output file size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
    }

    private static async Task ProcessKillCollectionCsv(string csvPath, string outputDir)
    {
        if (string.IsNullOrEmpty(outputDir))
        {
            var config = Config.LoadConfig();
            outputDir = config.project.TickByTickParse;
        }

        Console.WriteLine($"Processing kill collection: {csvPath}");
        Console.WriteLine($"Output directory: {outputDir}");

        await POVWriter.RunParser(csvPath, outputDir);
    }
}
