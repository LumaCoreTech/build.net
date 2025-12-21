// Copyright (c) 2025 LumaCoreTech
// SPDX-License-Identifier: MIT
// Project: https://github.com/LumaCoreTech/build.net

using System.Text;

using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace LumaCore.OpenApiGen.Generator;

/// <summary>
/// Parses OpenAPI specification files into <see cref="OpenApiDocument"/> objects.
/// </summary>
/// <remarks>
///     <para>
///     This class wraps
///     <see cref="OpenApiDocument.LoadAsync(Stream, string?, OpenApiReaderSettings?, CancellationToken)"/>
///     from the <c>Microsoft.OpenApi</c> package (v3.x) and provides simplified error handling
///     for CLI usage.
///     </para>
///     <para>
///     Supports OpenAPI 3.0, 3.1, and 3.2 specifications in JSON format. YAML files are not
///     directly supported but can be converted to JSON before parsing.
///     </para>
///     <para>
///     Line endings are normalized to LF (<c>\n</c>) during parsing to ensure consistent
///     handling across platforms.
///     </para>
/// </remarks>
sealed class OpenApiParser
{
	/// <summary>
	/// Parses an OpenAPI JSON file asynchronously.
	/// </summary>
	/// <param name="filePath">
	/// Absolute or relative path to the OpenAPI JSON file. The file must exist and contain a valid
	/// OpenAPI 3.x specification.
	/// </param>
	/// <returns>
	/// The parsed <see cref="OpenApiDocument"/> containing the API specification, or
	/// <see langword="null"/> if parsing failed due to validation errors. Parse errors are written
	/// to <see cref="Console.Error"/>.
	/// </returns>
	/// <exception cref="FileNotFoundException">
	/// Thrown when the file specified by <paramref name="filePath"/> does not exist.
	/// </exception>
	/// <exception cref="IOException">
	/// Thrown when the file cannot be read due to I/O errors.
	/// </exception>
	/// <remarks>
	///     <para>
	///     Validation warnings (non-fatal issues) are written to <see cref="Console.Out"/>
	///     but do not prevent successful parsing. Only validation errors cause
	///     <see langword="null"/> to be returned.
	///     </para>
	///     <para>
	///     Example usage:
	///     </para>
	///     <code>
	/// var document = await OpenApiParser.ParseAsync("openapi.json");
	/// if (document is not null)
	/// {
	///     Console.WriteLine($"Parsed: {document.Info.Title}");
	/// }
	/// </code>
	/// </remarks>
	public static async Task<OpenApiDocument?> ParseAsync(string filePath)
	{
		// Read file content and normalize line endings to LF.
		// This ensures consistent handling of descriptions across Windows/Unix.
		string content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
		content = NormalizeLineEndings(content);

		// Create a memory stream from the normalized content.
		await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

		// Parse the OpenAPI document using the v3 API (tuple deconstruction).
		(OpenApiDocument? document, OpenApiDiagnostic? diagnostic) =
			await OpenApiDocument.LoadAsync(stream).ConfigureAwait(false);

		// Log validation errors.
		if (diagnostic?.Errors is { Count: > 0 })
		{
			foreach (OpenApiError error in diagnostic.Errors)
			{
				await Console.Error.WriteLineAsync($"OpenAPI Parse Error: {error.Message}").ConfigureAwait(false);
			}

			return null;
		}

		// Log validation warnings.
		if (diagnostic?.Warnings is { Count: > 0 })
		{
			foreach (OpenApiError warning in diagnostic.Warnings)
			{
				await Console.Out.WriteLineAsync($"OpenAPI Warning: {warning.Message}").ConfigureAwait(false);
			}
		}

		// Return the successfully parsed document.
		return document;
	}

	/// <summary>
	/// Normalizes line endings to LF (<c>\n</c>).
	/// </summary>
	/// <param name="text">The text to normalize.</param>
	/// <returns>The text with all line endings converted to LF.</returns>
	/// <remarks>
	/// Handles both actual line breaks in the file and JSON-escaped sequences
	/// (e.g., <c>\r\n</c> stored as <c>\\r\\n</c> in JSON strings).
	/// </remarks>
	private static string NormalizeLineEndings(string text) => text
		// Normalize JSON-escaped line endings within string values
		.Replace("\\r\\n", "\\n")
		.Replace("\\r", "\\n")
		// Normalize actual line endings in the file
		.Replace("\r\n", "\n")
		.Replace("\r", "\n");
}
