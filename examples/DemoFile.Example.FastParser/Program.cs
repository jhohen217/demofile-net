﻿﻿using System;
using System.Threading.Tasks;

internal class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: <input_file> [output_path]");
            Console.WriteLine("  input_file: .dem file for demo parsing or .csv file for POV writing");
            Console.WriteLine("  output_path: (optional) output file/directory path");
            return;
        }

        string inputPath = args[0];
        string outputPath = args.Length > 1 ? args[1] : null;

        try
        {
            await FileProcessor.ProcessFile(inputPath, outputPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing file: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}
