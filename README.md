SFModDataExtractor: Reads installed Satisfactory Mods and outputs data in the Satisfactory Modeler game data format
```
Usage: SFModDataExtractor [options] <Configuration file>
Arguments:
  Configuration file  See the example configuration file for options
                      Default value is: config.json.
Options:
  -?|-h|--help        Show help information.

No matter the configuration a folder will be created for each detected mod with only that mod's data. This data can be used with the merger.

Configuration file options:
  satisfactory_path: the path to your satisfactory install folder (default: "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Satisfactory\\")
  save_icons: whether to output png icon files for detected items and machines (default: true)
  write_to_modeler_after_extracting: whether to overwrite the modeler game data and move icons to the modeler folder, a backup will always be created (default: false)
  modeler_path: the path to your modeler install folder (default: "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Satisfactory Modeler\\")
```

SFModDataMerger: Merges files in the Sataisfactory Modeler game data format
```
Usage: SFModDataMerger [options] <Output file> <Input files>
Arguments:
  Output file   Output file, if it already exists a backup will be made and it will be combined with inputs
  Input files   Input files which will be combined in the output
```