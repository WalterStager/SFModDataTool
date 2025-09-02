using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace SFModDataMerger;

public class SFModDataMergerProgram {
    public static int Main(string[] args) {
        args = args.Length == 0 ? new string[] { "--help" } : args;
        CommandLineApplication.Execute<SFModDataMergerProgram>(args);
        return 0;
    }

    [Argument(0, Name = "Output file", Description = "Output file, if it already exists a backup will be made and it will be combined with inputs")]
    [Required]
    private string? OutputFilePath { get; }

    [Argument(1, Name = "Input files", Description = "Input files which will be combined in the output")]
    [Required]
    private string[]? InputFilePaths { get; }

    private void OnExecute() {
        if (OutputFilePath == null || InputFilePaths == null) {
            throw new Exception("Argument missing");
        }
        IEnumerable<string> FilePaths = InputFilePaths.ToList();

        bool outputExists = File.Exists(OutputFilePath);
        if (outputExists) {
            if (InputFilePaths?.Length < 1) {
                throw new Exception("Not enough input files");
            }
        }
        else {
            if (InputFilePaths?.Length < 2) {
                throw new Exception("Not enough input files");
            }
        }

        if (outputExists) {
            FilePaths = FilePaths.Append(OutputFilePath);
        }

        GameData data = GameData.ReadGameData(FilePaths.First());
        foreach (string filePath in FilePaths.Skip(1)) {
            // Console.WriteLine($"{data.Machines.Count()},{data.MultiMachines.Count()},{data.Parts.Count()},{data.Recipes.Count()}");
            data = data.Union(GameData.ReadGameData(filePath));
        }
        data.WriteGameData(OutputFilePath, outputExists);
    }
}