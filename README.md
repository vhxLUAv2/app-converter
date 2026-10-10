# .mrpack to .zip Converter

A minimal Windows desktop utility that converts Modrinth `.mrpack` files into ZIP archives containing Minecraft instance files.

## What conversion does

- Reads `modrinth.index.json` from the archive root.
- Downloads every indexed file from the HTTPS download URLs declared in the index.
- Verifies SHA-1 and/or SHA-512 hashes when the index supplies them.
- Includes files from `overrides/`, `client-overrides/`, and `server-overrides/` at the ZIP root.
- Supports drag-and-drop and batch conversion.
- Never overwrites an existing destination ZIP.
- Writes downloads to temporary files, and removes incomplete output if a conversion fails.

If a required file cannot be downloaded or its hash does not match, that pack's conversion fails and its partial ZIP is deleted. Some indexed downloads may no longer be available. The result is a ZIP of instance files; it is not a Modrinth launcher import package and does not install Minecraft.

## Requirements

- Windows
- .NET 8 SDK to build from source
- Internet access to download indexed files

## Run

From this folder:

```powershell
dotnet run
```

## Publish a standalone Windows executable

```powershell
dotnet publish AppConverter.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The executable is written to the `publish` directory.

## Use

1. Drop one or more `.mrpack` files into the window, or choose **Add files…**.
2. Click **Convert to ZIP**.
3. The resulting `.zip` is created beside each source file.
4. If a destination ZIP already exists, that conversion fails without overwriting it.

The project uses built-in .NET libraries only; no third-party packages are required.
