using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace SFModDataMerger;

public class Program {
    public static int Main(string[] args) {
        args = args.Length == 0 ? new string[] { "--help" } : args;
        CommandLineApplication.Execute<Program>(args);
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
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string? directoryPortion = Path.GetDirectoryName(OutputFilePath);
            if (directoryPortion == null) {
                throw new Exception("Output file does not exist at a valid path, could not make backup file");
            }
            string BackupFilePath = Path.Combine(
                directoryPortion,
                Path.GetFileNameWithoutExtension(OutputFilePath) + timestamp + Path.GetExtension(OutputFilePath)
            );
            File.Copy(OutputFilePath, BackupFilePath);
            FilePaths = FilePaths.Append(OutputFilePath);
        }

        GameData data = GameData.ReadGameData(FilePaths.First());
        foreach (string filePath in FilePaths.Skip(1)) {
            // Console.WriteLine($"{data.Machines.Count()},{data.MultiMachines.Count()},{data.Parts.Count()},{data.Recipes.Count()}");
            data = data.Union(GameData.ReadGameData(filePath));
        }
        data.WriteGameData(OutputFilePath);
    }
}

public class GameDataRecipePart {
    public required string? Part;
    public required string? Amount;
}

public class GameDataMMMachine {
    public required string Name;
    public string? PartsRatio;
    public bool? Default;
}

public class GameDataMMCapacity {
    public required string Name;
    public string? PartsRatio;
    public bool? Default;
    public int? Color;
}

public class GameDataMachine {
    public required string Name;
    public required string Tier;
    public string? AveragePower;
    public string? OverclockPowerExponent;
    public int? MaxProductionShards;
    public string? ProductionShardMultiplier;
    public string? ProductionShardPowerExponent;
    public IEnumerable<GameDataRecipePart>? Cost;
    public string? MinPower;
    public string? BasePower;
    public string? BasePowerBoost;
    public string? FueledBasePowerBoost;
}

public class GameDataMultiMachine {
    public required string Name;
    public bool? ShowPpm;
    public bool? AutoRound;
    public string? DefaultMax;
    public IEnumerable<GameDataMMMachine>? Machines;
    public IEnumerable<GameDataMMCapacity>? Capacities;
}

public class GameDataItem {
    public required string Name;
    public required string Tier;
    public required int? SinkPoints;
}

public class GameDataRecipe {
    public required string Name;
    public required string Tier;
    public string? Machine;
    public string? BatchTime;
    public IEnumerable<GameDataRecipePart>? Parts;
    public string? MinPower;
    public bool? Alternate;
    public bool? Ficsmas;
}

public class GameData {
    public required IEnumerable<GameDataMachine> Machines;
    public required IEnumerable<GameDataMultiMachine> MultiMachines;
    public required IEnumerable<GameDataItem> Parts;
    public required IEnumerable<GameDataRecipe> Recipes;

    public static GameData ReadGameData(string filename) {
        try {
            string text = File.ReadAllText(filename);
            GameData? gameData = JsonConvert.DeserializeObject<GameData>(text);

            if (gameData == null)
                throw new Exception("Failed to parse game_data.json: root object is null.");

            return gameData;
        }
        catch (Exception ex) {
            throw new Exception($"Error parsing {filename}: {ex.Message}", ex);
        }
    }

    public void WriteGameData(string filename) {
        File.WriteAllText(filename, JsonConvert.SerializeObject(this, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
    }

    public GameData Union(GameData data) {
        return new GameData {
            Machines = Machines.Union(data.Machines),
            MultiMachines = MultiMachines.Union(data.MultiMachines),
            Parts = Parts.Union(data.Parts),
            Recipes = Recipes.Union(data.Recipes),
        };
    }
}