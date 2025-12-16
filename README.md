# build.net

Common .NET development and build related tools and configurations.

## Structure

```
build.net/
├── BuildTools/                    # .NET tool source code
│   ├── BuildTools.sln
│   └── src/
│       ├── Directory.Build.props  # Shared build properties
│       └── LumaCore.OpenApiGen/   # OpenAPI → Markdown generator
├── OpenApi/                       # OpenAPI documentation integration
│   ├── generate-api-docs.ps1      # Generate Markdown from openapi.json
│   └── verify-api-docs.ps1        # CI verification
├── ReSharper.DotSettings          # Shared ReSharper/Rider settings
└── LICENSE
```

## Features

### OpenAPI Documentation

Generate GitHub-friendly Markdown API documentation from OpenAPI specifications.

```bash
pwsh ./build.net/OpenApi/generate-api-docs.ps1
```

See [OpenApi/README.md](OpenApi/README.md) for details.

### Build Tools

The `BuildTools/` directory contains source code for .NET tools used by the build system:

- **LumaCore.OpenApiGen** – Converts OpenAPI JSON to Markdown with collapsible sections and code samples.

Tools are built on-demand via `dotnet run` – no pre-compiled binaries in the repository.

## Usage as Git Submodule

```bash
git submodule add https://github.com/LumaCoreTech/build.net.git build.net
```

## Requirements

- .NET SDK 8.0 or later
- PowerShell Core 7.0+ (`pwsh`)

## License

MIT License – see [LICENSE](LICENSE) for details.
