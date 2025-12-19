// Copyright (c) 2025 LumaCoreTech
// SPDX-License-Identifier: MIT
// Project: https://github.com/LumaCoreTech/build.net

using System.CommandLine;

using LumaCore.OpenApiGen.Generator;

using Microsoft.OpenApi;

namespace LumaCore.OpenApiGen;

/// <summary>
/// Entry point for the OpenAPI to Markdown generator CLI tool.
/// </summary>
/// <remarks>
///     <para>
///     This tool converts OpenAPI JSON specifications into GitHub-friendly Markdown
///     documentation with collapsible sections and multi-language code samples.
///     </para>
///     <para>
///     Usage: <c>LumaCore.OpenApiGen --input openapi.json --output README.md</c>
///     </para>
/// </remarks>
static class Program
{
	/// <summary>
	/// Application entry point that configures and executes the CLI.
	/// </summary>
	/// <param name="args">
	/// Command line arguments. Supported options:
	/// <list type="bullet">
	///     <item><c>--input, -i</c>: Path to the OpenAPI JSON file (required)</item>
	///     <item><c>--output, -o</c>: Path for the generated Markdown file (required)</item>
	///     <item><c>--title, -t</c>: Custom API title (optional, defaults to spec title)</item>
	///     <item><c>--code-samples, -c</c>: Languages for code samples (optional, defaults to shell,csharp)</item>
	/// </list>
	/// </param>
	/// <returns>
	/// Exit code: <c>0</c> for success, non-zero for errors.
	/// </returns>
	public static Task<int> Main(string[] args)
	{
		// Define input option.
		var inputOption = new Option<FileInfo>(
			["--input", "-i"],
			"Path to the OpenAPI JSON file")
		{
			IsRequired = true
		};

		// Define output option.
		var outputOption = new Option<FileInfo>(
			["--output", "-o"],
			"Path for the generated Markdown file")
		{
			IsRequired = true
		};

		// Define title option.
		var titleOption = new Option<string?>(
			["--title", "-t"],
			"API title (defaults to title from OpenAPI spec)");

		// Define code samples option (comma-separated string).
		var codeSamplesOption = new Option<string>(
			["--code-samples", "-c"],
			"Languages for code samples, comma-separated (shell, csharp, javascript, python)");
		codeSamplesOption.SetDefaultValue("shell,csharp");

		// Create root command.
		var rootCommand = new RootCommand("Generate GitHub-friendly Markdown from OpenAPI specifications")
		{
			inputOption,
			outputOption,
			titleOption,
			codeSamplesOption
		};

		// Set the handler for the command.
		// Bind options to parameters.
		rootCommand.SetHandler(
			GenerateDocumentationAsync,
			inputOption,
			outputOption,
			titleOption,
			codeSamplesOption);

		return rootCommand.InvokeAsync(args);
	}

	/// <summary>
	/// Generates Markdown documentation from an OpenAPI specification file.
	/// </summary>
	/// <param name="input">
	/// The OpenAPI JSON input file. Must exist and contain a valid OpenAPI 3.x specification.
	/// </param>
	/// <param name="output">
	/// The target path for the generated Markdown file. Parent directories are created if needed.
	/// </param>
	/// <param name="title">
	/// Optional custom title for the documentation. If <see langword="null"/>, the title
	/// from the OpenAPI specification's <c>info.title</c> field is used.
	/// </param>
	/// <param name="codeSamples">
	/// Comma-separated languages to generate code samples for. Supported values: <c>shell</c>, <c>csharp</c>,
	/// <c>javascript</c>, <c>python</c>.
	/// </param>
	/// <returns>A task representing the asynchronous operation.</returns>
	/// <remarks>
	///     <para>
	///     This method orchestrates the documentation generation process:
	///     </para>
	///     <list type="number">
	///         <item>Validates input file existence</item>
	///         <item>Parses the OpenAPI JSON using <see cref="OpenApiParser"/></item>
	///         <item>Generates Markdown using <see cref="MarkdownGenerator"/></item>
	///         <item>Writes the output file, creating parent directories as needed</item>
	///     </list>
	///     <para>
	///     Errors are written to <see cref="Console.Error"/> and set <see cref="Environment.ExitCode"/>
	///     to <c>1</c> rather than throwing exceptions, for better CLI behavior.
	///     </para>
	/// </remarks>
	private static async Task GenerateDocumentationAsync(
		FileInfo input,
		FileInfo output,
		string?  title,
		string   codeSamples)
	{
		try
		{
			// Log input file path.
			await Console.Out.WriteLineAsync($"Reading OpenAPI spec from: {input.FullName}").ConfigureAwait(false);

			// Validate input file existence.
			if (!input.Exists)
			{
				await Console.Error.WriteLineAsync($"Error: Input file not found: {input.FullName}").ConfigureAwait(false);
				Environment.ExitCode = 1;
				return;
			}

			// Parse OpenAPI document.
			OpenApiDocument? document = await OpenApiParser.ParseAsync(input.FullName).ConfigureAwait(false);
			if (document is null)
			{
				await Console.Error.WriteLineAsync("Error: Failed to parse OpenAPI document.").ConfigureAwait(false);
				Environment.ExitCode = 1;
				return;
			}

			// Log parsed document info.
			await Console.Out.WriteLineAsync($"Parsed: {document.Info.Title} {document.Info.Version}").ConfigureAwait(false);

			// Generate Markdown documentation.
			// Split comma-separated languages into array.
			string[] languages = codeSamples.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			var generator = new MarkdownGenerator(title ?? document.Info.Title ?? "API Documentation", languages);
			string markdown = generator.Generate(document);

			// Ensure output directory exists.
			DirectoryInfo? outputDir = output.Directory;
			if (outputDir is not null && !outputDir.Exists) outputDir.Create();

			// Write output file.
			await File.WriteAllTextAsync(output.FullName, markdown).ConfigureAwait(false);

			// Log output file path.
			await Console.Out.WriteLineAsync($"Generated: {output.FullName}").ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			// Log any unexpected errors.
			await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
			Environment.ExitCode = 1;
		}
	}
}
