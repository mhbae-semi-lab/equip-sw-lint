# Example SARIF files

Synthetic equipment code in `samples/Equipment`, analyzed by the CLI. Regenerate from the repository root after changing rules or samples:

```
dotnet build src/EquipSwLint.Cli -c Release
dotnet src/EquipSwLint.Cli/bin/Release/net8.0/equip-sw-lint.dll samples/Equipment --lang ko --fail-on none --sarif viewer/samples/equipment.ko.sarif
dotnet src/EquipSwLint.Cli/bin/Release/net8.0/equip-sw-lint.dll samples/Equipment --lang en --fail-on none --sarif viewer/samples/equipment.en.sarif
```

`equipment-v1.*.sarif` is the same code with "rule set v1": a copy of `samples/Equipment` with this `.editorconfig` next to it, analyzed the same way.

```ini
root = true
[*.cs]
dotnet_diagnostic.ESL002.severity = none
dotnet_diagnostic.ESL005.severity = none
dotnet_diagnostic.ESL007.severity = none
dotnet_diagnostic.ESL010.severity = none
dotnet_diagnostic.ESL011.severity = none
```
