# LumaCore.OpenApiGen

A .NET CLI tool that generates GitHub-friendly Markdown documentation from OpenAPI specifications.

## Features

- Parses OpenAPI 3.x JSON specifications
- **Quick Reference** table with all endpoints at a glance
- Color-coded HTTP methods (🟢 GET, 🔵 POST, 🟠 PUT, 🟡 PATCH, 🔴 DELETE)
- Multi-language code samples (Shell, C#, JavaScript, Python) with request body examples
- Schema documentation with JSON examples
- Clean table-based response status documentation
- Navigation links back to Quick Reference
- No external dependencies (pure .NET)

## Usage

```bash
# Basic usage
dotnet run --project LumaCore.OpenApiGen.csproj -- \
    --input openapi.json \
    --output README.md

# With all code sample languages
dotnet run --project LumaCore.OpenApiGen.csproj -- \
    --input openapi.json \
    --output README.md \
    --code-samples shell,csharp,javascript,python

# Custom title
dotnet run --project LumaCore.OpenApiGen.csproj -- \
    --input openapi.json \
    --output README.md \
    --title "My API Documentation"
```

## CLI Options

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--input` | `-i` | Path to OpenAPI JSON file | *required* |
| `--output` | `-o` | Path for generated Markdown | *required* |
| `--title` | `-t` | API title override | From spec |
| `--code-samples` | `-c` | Languages for code samples | `shell,csharp` |

## Output Structure

The generated Markdown includes:

1. **Header** – Title, version, base URL
2. **Table of Contents** – Links to all sections including Quick Reference
3. **Authentication** – Security schemes
4. **Endpoints**
   - **Quick Reference** – Overview table with all endpoints, methods, and descriptions
   - **By Tag** – Detailed documentation grouped by tag:
     - Parameters (collapsible)
     - Request body
     - Responses (table format)
     - Code samples (collapsible) – includes request body examples for POST/PUT/PATCH
     - Navigation link back to Quick Reference
5. **Schemas** – All component schemas with property tables and examples

## Dependencies

- [Microsoft.OpenApi.Readers](https://www.nuget.org/packages/Microsoft.OpenApi.Readers/) – OpenAPI parsing
- [System.CommandLine](https://www.nuget.org/packages/System.CommandLine/) – CLI framework

## Building

```bash
dotnet build -c Release
```

## Integration

This tool is designed to be called from the `generate-api-docs.ps1` script in the parent `OpenApi/` directory. It's built on-demand – no pre-compiled binaries are committed.

---

© 2025 LumaCoreTech
