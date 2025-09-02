using McMaster.Extensions.CommandLineUtils;

namespace SFModDataExtractor;

public class SFModDataExtractProgram {
    public static int Main(string[] args) {
        args = args.Length == 0 ? new string[] { "--help" } : args;
        CommandLineApplication.Execute<SFModDataExtractProgram>(args);
        return 0;
    }

    [Argument(0, Name = "Configuration file", Description = "See the example configuration file for options")]
    private string? ConfigFile { get; } = "config.json";

    private void OnExecute() {
        if (ConfigFile == null || ConfigFile == "") {
            throw new Exception("Configuration file missing");
        }
        SFModDataExtract extractor = new SFModDataExtract(ConfigFile);
        extractor.doTheThing();
    }
}


