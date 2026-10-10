# .mrpack to .zip Converter

A minimal Windows desktop utility that batch-renames `.mrpack` files to `.zip`.

## Requirements

- Windows
- .NET 8 SDK to build from source

## Run

From this folder:

```powershell
dotnet run
```

## Publish a standalone Windows executable

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The executable is created under `bin/Release/net8.0-windows/win-x64/publish/`.

## Use

1. Drag one or more `.mrpack` files into the window, or select **Add files…**.
2. Select **Rename to .zip**.
3. Each file is renamed in its existing folder. The file contents are not extracted or rewritten.
4. If a matching `.zip` file already exists, that item is skipped to prevent overwriting.

The project uses Windows Forms and .NET libraries only; no third-party packages are required.
