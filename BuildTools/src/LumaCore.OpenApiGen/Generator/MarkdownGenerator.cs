// Copyright (c) 2025 LumaCoreTech
// SPDX-License-Identifier: MIT
// Project: https://github.com/LumaCoreTech/build.net

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.OpenApi;

namespace LumaCore.OpenApiGen.Generator;

/// <summary>
/// Generates GitHub-friendly Markdown documentation from OpenAPI documents.
/// </summary>
/// <remarks>
///     <para>
///     This generator produces structured Markdown with the following sections:
///     </para>
///     <list type="bullet">
///         <item>Header with API title, version, and base URLs</item>
///         <item>Table of contents with anchor links</item>
///         <item>Authentication section documenting security schemes</item>
///         <item>Endpoints grouped by tag, with collapsible details</item>
///         <item>Schemas section with property tables and examples</item>
///     </list>
///     <para>
///     The output uses HTML <c>&lt;details&gt;</c> elements for collapsible sections,
///     which render correctly on GitHub and in most Markdown viewers.
///     </para>
/// </remarks>
sealed class MarkdownGenerator
{
	/// <summary>
	/// Invariant culture for consistent string formatting across locales.
	/// </summary>
	private static readonly CultureInfo sInv = CultureInfo.InvariantCulture;

	/// <summary>
	/// Cached JSON serializer options for example generation.
	/// </summary>
	private static readonly JsonSerializerOptions sJsonOptions = new()
	{
		WriteIndented = true
	};

	/// <summary>
	/// Cached JSON serializer options for compact (single-line) example generation.
	/// </summary>
	private static readonly JsonSerializerOptions sJsonOptionsCompact = new()
	{
		WriteIndented = false
	};

	/// <summary>
	/// The API title displayed in the documentation header.
	/// </summary>
	private readonly string mApiTitle;

	/// <summary>
	/// Set of programming languages for which to generate code samples.
	/// Comparison is case-insensitive.
	/// </summary>
	private readonly HashSet<string> mCodeSampleLanguages;

	/// <summary>
	/// Initializes a new instance of the <see cref="MarkdownGenerator"/> class.
	/// </summary>
	/// <param name="apiTitle">
	/// The API title to display in the documentation header. This overrides
	/// the title from the OpenAPI specification's <c>info.title</c> field.
	/// </param>
	/// <param name="codeSampleLanguages">
	/// Languages to generate code samples for. Supported values:
	/// <c>shell</c>, <c>csharp</c>, <c>javascript</c>, <c>python</c>.
	/// Case-insensitive matching is used.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown when <paramref name="apiTitle"/> or <paramref name="codeSampleLanguages"/>
	/// is <see langword="null"/>.
	/// </exception>
	public MarkdownGenerator(string apiTitle, string[] codeSampleLanguages)
	{
		mApiTitle = apiTitle ?? throw new ArgumentNullException(nameof(apiTitle));
		mCodeSampleLanguages = new HashSet<string>(
			codeSampleLanguages ?? throw new ArgumentNullException(nameof(codeSampleLanguages)),
			StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Generates complete Markdown documentation from an OpenAPI document.
	/// </summary>
	/// <param name="document">
	/// The parsed <see cref="OpenApiDocument"/> to generate documentation for.
	/// Must not be <see langword="null"/>.
	/// </param>
	/// <returns>
	/// A string containing the complete Markdown documentation, ready to be
	/// written to a file or displayed.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown when <paramref name="document"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The generated Markdown includes:
	/// <list type="number">
	///     <item>Header with title, version, and server URLs</item>
	///     <item>Table of contents with anchor links</item>
	///     <item>Authentication section</item>
	///     <item>Endpoints grouped by tag</item>
	///     <item>Schema definitions with examples</item>
	///     <item>Footer with generation timestamp</item>
	/// </list>
	/// </remarks>
	public string Generate(OpenApiDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);

		var sb = new StringBuilder();

		AppendHeader(sb, document);
		AppendTableOfContents(sb, document);
		AppendAuthentication(sb, document);
		AppendEndpoints(sb, document);
		AppendSchemas(sb, document);
		AppendFooter(sb);

		return sb.ToString();
	}

	/// <summary>
	/// Appends the documentation header with API title, version, and server URLs.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="document">The OpenAPI document containing API metadata.</param>
	private void AppendHeader(StringBuilder sb, OpenApiDocument document)
	{
		sb.AppendLine(sInv, $"# {mApiTitle}");
		sb.AppendLine();

		if (!string.IsNullOrWhiteSpace(document.Info.Description))
		{
			sb.AppendLine(document.Info.Description);
			sb.AppendLine();
		}

		sb.AppendLine(sInv, $"**Version:** `{document.Info.Version}`");
		sb.AppendLine();

		if (document.Servers is { Count: > 0 })
		{
			sb.AppendLine("**Base URL:**");
			foreach (OpenApiServer server in document.Servers)
			{
				string description = string.IsNullOrWhiteSpace(server.Description)
					                     ? string.Empty
					                     : $" - {server.Description}";
				sb.AppendLine(sInv, $"- `{server.Url}`{description}");
			}

			sb.AppendLine();
		}

		sb.AppendLine("---");
		sb.AppendLine();
	}

	/// <summary>
	/// Appends a table of contents with anchor links to all major sections.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="document">The OpenAPI document to extract tags from.</param>
	private static void AppendTableOfContents(StringBuilder sb, OpenApiDocument document)
	{
		sb.AppendLine("## Table of Contents");
		sb.AppendLine();
		sb.AppendLine("- [Authentication](#authentication)");
		sb.AppendLine("- [Endpoints](#endpoints)");
		sb.AppendLine("  - [Quick Reference](#quick-reference)");

		Dictionary<string, List<(string Path, HttpMethod Method, OpenApiOperation Operation)>> taggedPaths = GetEndpointsByTag(document);
		foreach (string tag in taggedPaths.Keys.OrderBy(t => t, StringComparer.Ordinal))
		{
			string anchor = ToAnchor(tag);
			sb.AppendLine(sInv, $"  - [{tag}](#{anchor})");
		}

		sb.AppendLine("- [Schemas](#schemas)");
		sb.AppendLine();
	}

	/// <summary>
	/// Appends the authentication section documenting all security schemes.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="document">The OpenAPI document containing security definitions.</param>
	private static void AppendAuthentication(StringBuilder sb, OpenApiDocument document)
	{
		sb.AppendLine("## Authentication");
		sb.AppendLine();

		if (document.Components?.SecuritySchemes is null || document.Components.SecuritySchemes.Count == 0)
		{
			sb.AppendLine("No authentication required.");
			sb.AppendLine();
			return;
		}

		foreach ((string name, IOpenApiSecurityScheme scheme) in document.Components.SecuritySchemes)
		{
			sb.AppendLine(sInv, $"### {name}");
			sb.AppendLine();
			sb.AppendLine(sInv, $"- **Type:** `{scheme.Type}`");

			if (scheme.Type == SecuritySchemeType.Http)
			{
				sb.AppendLine(sInv, $"- **Scheme:** `{scheme.Scheme}`");
				if (!string.IsNullOrWhiteSpace(scheme.BearerFormat))
					sb.AppendLine(sInv, $"- **Bearer Format:** `{scheme.BearerFormat}`");
			}
			else if (scheme.Type == SecuritySchemeType.ApiKey)
			{
				sb.AppendLine(sInv, $"- **In:** `{scheme.In}`");
				sb.AppendLine(sInv, $"- **Name:** `{scheme.Name}`");
			}

			if (!string.IsNullOrWhiteSpace(scheme.Description))
			{
				sb.AppendLine();
				sb.AppendLine(scheme.Description);
			}

			sb.AppendLine();
		}
	}

	/// <summary>
	/// Appends all endpoints grouped by their OpenAPI tags.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="document">The OpenAPI document containing path definitions.</param>
	private void AppendEndpoints(StringBuilder sb, OpenApiDocument document)
	{
		sb.AppendLine("## Endpoints");
		sb.AppendLine();

		Dictionary<string, List<(string Path, HttpMethod Method, OpenApiOperation Operation)>> taggedPaths = GetEndpointsByTag(document);

		// Generate quick reference overview table
		AppendEndpointOverview(sb, taggedPaths);

		foreach ((string tag, List<(string Path, HttpMethod Method, OpenApiOperation Operation)> endpoints) in taggedPaths.OrderBy(kv => kv.Key, StringComparer.Ordinal))
		{
			sb.AppendLine(sInv, $"### {tag}");
			sb.AppendLine();

			foreach ((string path, HttpMethod method, OpenApiOperation operation) in
			         endpoints.OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Method))
			{
				AppendEndpoint(sb, path, method, operation);
			}
		}
	}

	/// <summary>
	/// Appends a quick reference table with all endpoints.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="taggedPaths">Endpoints grouped by tag.</param>
	private static void AppendEndpointOverview(
		StringBuilder                                                                          sb,
		Dictionary<string, List<(string Path, HttpMethod Method, OpenApiOperation Operation)>> taggedPaths)
	{
		sb.AppendLine("### Quick Reference");
		sb.AppendLine();
		sb.AppendLine("| Method | Endpoint | Description |");
		sb.AppendLine("|--------|----------|-------------|");

		foreach ((string _, List<(string Path, HttpMethod Method, OpenApiOperation Operation)> endpoints) in taggedPaths.OrderBy(kv => kv.Key, StringComparer.Ordinal))
		{
			foreach ((string path, HttpMethod method, OpenApiOperation operation) in
			         endpoints.OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Method))
			{
				string methodUpper = method.ToString().ToUpperInvariant();
				string methodDot = GetMethodDot(method);
				string anchor = $"{methodUpper.ToLowerInvariant()}-{path.Replace("/", "").Replace("{", "").Replace("}", "")}";
				string summary = GetFirstSentence(operation.Summary);

				sb.AppendLine(sInv, $"| {methodDot} {methodUpper} | [`{path}`](#{anchor}) | {summary} |");
			}
		}

		sb.AppendLine();
	}

	/// <summary>
	/// Extracts the first sentence from a summary and escapes it for table use.
	/// </summary>
	/// <param name="summary">The summary text.</param>
	/// <returns>The first sentence, escaped for Markdown tables.</returns>
	private static string GetFirstSentence(string? summary)
	{
		if (string.IsNullOrWhiteSpace(summary)) return "—";

		// Get the first sentence (up to and including the first period)
		int periodIndex = summary.IndexOf('.');
		string result = periodIndex > 0 ? summary[..(periodIndex + 1)] : summary;

		return SanitizeForTable(result);
	}

	/// <summary>
	/// Sanitizes a string for use in Markdown table cells.
	/// </summary>
	/// <param name="text">The text to sanitize.</param>
	/// <returns>The sanitized text with newlines removed and pipes escaped.</returns>
	/// <remarks>
	/// Line endings are already normalized to LF by <see cref="OpenApiParser"/>,
	/// so only <c>\n</c> needs to be handled here.
	/// </remarks>
	private static string SanitizeForTable(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return "";

		return text
			.Replace("\n", " ")
			.Replace("|", "\\|");
	}

	/// <summary>
	/// Appends documentation for a single endpoint with collapsible sections.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="path">The endpoint path (e.g., <c>/api/users/{id}</c>).</param>
	/// <param name="method">The HTTP method (GET, POST, etc.).</param>
	/// <param name="operation">The OpenAPI operation containing endpoint details.</param>
	private void AppendEndpoint(
		StringBuilder    sb,
		string           path,
		HttpMethod       method,
		OpenApiOperation operation)
	{
		string methodUpper = method.ToString().ToUpperInvariant();
		string methodLower = methodUpper.ToLowerInvariant();
		string methodDot = GetMethodDot(method);
		string anchor = $"{methodLower}-{path.Replace("/", "").Replace("{", "").Replace("}", "")}";

		sb.AppendLine(sInv, $"<a id=\"{anchor}\"></a>");
		sb.AppendLine(sInv, $"#### {methodDot} {methodUpper} `{path}`");
		sb.AppendLine();

		if (!string.IsNullOrWhiteSpace(operation.Summary))
		{
			sb.AppendLine(operation.Summary);
			sb.AppendLine();
		}

		if (!string.IsNullOrWhiteSpace(operation.Description) &&
		    operation.Description != operation.Summary)
		{
			sb.AppendLine(operation.Description);
			sb.AppendLine();
		}

		if (operation.Parameters is { Count: > 0 }) AppendParameters(sb, operation.Parameters);

		if (operation.RequestBody is not null) AppendRequestBody(sb, operation.RequestBody);

		AppendResponses(sb, operation.Responses);
		AppendCodeSamples(sb, path, method, operation);

		sb.AppendLine();
		sb.AppendLine("[↑ Quick Reference](#quick-reference)");
		sb.AppendLine();
		sb.AppendLine("---");
		sb.AppendLine();
	}

	/// <summary>
	/// Gets a colored dot emoji representing the HTTP method.
	/// </summary>
	/// <param name="method">The HTTP method.</param>
	/// <returns>A colored dot emoji.</returns>
	private static string GetMethodDot(HttpMethod method) => method.ToString().ToUpperInvariant() switch
	{
		"GET"    => "🟢",
		"POST"   => "🔵",
		"PUT"    => "🟠",
		"PATCH"  => "🟡",
		"DELETE" => "🔴",
		var _    => "⚪"
	};

	/// <summary>
	/// Appends a collapsible parameters table for an endpoint.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="parameters">The list of parameters to document.</param>
	private static void AppendParameters(StringBuilder sb, IList<IOpenApiParameter> parameters)
	{
		sb.AppendLine("<details>");
		sb.AppendLine("<summary><strong>Parameters</strong></summary>");
		sb.AppendLine();
		sb.AppendLine("| Name | In  | Type | Required | Description |");
		sb.AppendLine("|------|-----|------|----------|-------------|");

		foreach (IOpenApiParameter param in parameters)
		{
			string type = GetTypeString(param.Schema?.Type) ?? "string";
			string required = param.Required ? "✓" : "";
			string description = SanitizeForTable(param.Description);
			sb.AppendLine(sInv, $"| `{param.Name}` | {param.In} | `{type}` | {required} | {description} |");
		}

		sb.AppendLine();
		sb.AppendLine("</details>");
		sb.AppendLine();
	}

	/// <summary>
	/// Appends a request body section.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="requestBody">The request body definition to document.</param>
	private static void AppendRequestBody(StringBuilder sb, IOpenApiRequestBody requestBody)
	{
		sb.AppendLine("**Request Body**");
		sb.AppendLine();

		if (!string.IsNullOrWhiteSpace(requestBody.Description))
		{
			sb.AppendLine(requestBody.Description);
			sb.AppendLine();
		}

		if (requestBody.Content is not null)
		{
			foreach ((string contentType, IOpenApiMediaType mediaType) in requestBody.Content)
			{
				sb.AppendLine(sInv, $"**Content-Type:** `{contentType}`");
				sb.AppendLine();

				if (mediaType.Schema is not null) AppendSchemaReference(sb, mediaType.Schema);
			}
		}
	}

	/// <summary>
	/// Appends a responses section with a status code table.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="responses">The response definitions to document.</param>
	private static void AppendResponses(StringBuilder sb, OpenApiResponses? responses)
	{
		if (responses is null || responses.Count == 0) return;

		sb.AppendLine("**Responses**");
		sb.AppendLine();

		// Build table header
		sb.AppendLine("| Status | Description |");
		sb.AppendLine("|--------|-------------|");

		// Collect schemas to show after the table (for 2xx responses with response bodies)
		var schemasToShow = new List<(string StatusCode, IOpenApiSchema Schema)>();

		foreach ((string statusCode, IOpenApiResponse response) in responses.OrderBy(r => r.Key, StringComparer.Ordinal))
		{
			// Visual status indicators: ✅ success (2xx), ⚠️ client error (4xx), ❌ server error (5xx)
			string emoji = statusCode.StartsWith('2') ? "✅" :
			               statusCode.StartsWith('4') ? "⚠️" :
			               statusCode.StartsWith('5') ? "❌" : "ℹ️";

			// Sanitize description for table cell
			string description = SanitizeForTable(response.Description);

			sb.AppendLine(sInv, $"| {emoji} {statusCode} | {description} |");

			// Collect schemas from success responses to show after the table
			if (statusCode.StartsWith('2') && response.Content?.Count > 0)
			{
				foreach ((string _, IOpenApiMediaType mediaType) in response.Content)
				{
					if (mediaType.Schema is not null)
						schemasToShow.Add((statusCode, mediaType.Schema));
				}
			}
		}

		sb.AppendLine();

		// Show response schemas after the table
		foreach ((string statusCode, IOpenApiSchema schema) in schemasToShow)
		{
			sb.AppendLine(sInv, $"**{statusCode} Response:** ");
			AppendSchemaReference(sb, schema);
		}
	}

	/// <summary>
	/// Appends a schema reference as a link or inline JSON example.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="schema">The schema to reference.</param>
	private static void AppendSchemaReference(StringBuilder sb, IOpenApiSchema schema)
	{
		// OpenAPI v3.x represents $ref as OpenApiSchemaReference (not inline properties).
		// We link to the schema definition section for references, or show inline JSON for anonymous schemas.
		if (schema is OpenApiSchemaReference schemaRef)
		{
			// Named reference: link to schema section (e.g., "#/components/schemas/LoginRequest")
			string? schemaName = GetSchemaReferenceName(schemaRef);
			if (!string.IsNullOrEmpty(schemaName))
			{
				string anchor = ToAnchor(schemaName);
				sb.AppendLine(sInv, $"Schema: [`{schemaName}`](#schema-{anchor})");
				sb.AppendLine();
			}
		}
		else if (schema is OpenApiSchema concreteSchema)
		{
			// Inline/anonymous schema: check for array of references or show JSON example
			if (concreteSchema.Type?.HasFlag(JsonSchemaType.Array) == true &&
			    concreteSchema.Items is OpenApiSchemaReference itemsRef)
			{
				// Array of named types: "Array of User" linking to User schema
				string? schemaName = GetSchemaReferenceName(itemsRef);
				if (!string.IsNullOrEmpty(schemaName))
				{
					string anchor = ToAnchor(schemaName);
					sb.AppendLine(sInv, $"Schema: Array of [`{schemaName}`](#schema-{anchor})");
					sb.AppendLine();
				}
			}
			else if (concreteSchema.Properties?.Count > 0)
			{
				// Anonymous object with properties: show example JSON inline
				sb.AppendLine("```json");
				sb.AppendLine(SchemaToExampleJson(concreteSchema));
				sb.AppendLine("```");
				sb.AppendLine();
			}
		}
	}

	/// <summary>
	/// Extracts the schema name from an OpenAPI schema reference.
	/// </summary>
	/// <param name="schemaRef">The schema reference.</param>
	/// <returns>The schema name, or <see langword="null"/> if not found.</returns>
	private static string? GetSchemaReferenceName(OpenApiSchemaReference schemaRef)
	{
		// Try the Id property first (works in some versions)
		if (!string.IsNullOrEmpty(schemaRef.Id)) return schemaRef.Id;

		// Try to get the name from the RecordType (the actual referenced type)
		// In Microsoft.OpenApi 2.0+, the reference target may be accessible this way
		if (schemaRef.Target is OpenApiSchema)
		{
			// The schema itself doesn't have a name, but we can try to get it
			// from the reference path in the original document
		}

		// Fallback: try to get it from the reference URI if available
		// The reference typically looks like "#/components/schemas/LoginRequest"
		string? refString = schemaRef.Reference.ReferenceV3;
		if (!string.IsNullOrEmpty(refString))
		{
			// Extract the last segment: "LoginRequest" from "#/components/schemas/LoginRequest"
			int lastSlash = refString.LastIndexOf('/');
			if (lastSlash >= 0 && lastSlash < refString.Length - 1)
			{
				return refString[(lastSlash + 1)..];
			}
		}

		return null;
	}

	/// <summary>
	/// Appends collapsible code samples for an endpoint in configured languages.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="path">The endpoint path for the request URL.</param>
	/// <param name="method">The HTTP method to use in samples.</param>
	/// <param name="operation">The operation containing request body information.</param>
	private void AppendCodeSamples(
		StringBuilder    sb,
		string           path,
		HttpMethod       method,
		OpenApiOperation operation)
	{
		if (mCodeSampleLanguages.Count == 0) return;

		sb.AppendLine("<details>");
		sb.AppendLine("<summary><strong>Code Samples</strong></summary>");
		sb.AppendLine();

		string methodLower = method.ToString().ToLowerInvariant();
		string methodUpper = method.ToString().ToUpperInvariant();

		// Check if this endpoint has a request body
		bool hasRequestBody = operation.RequestBody?.Content?.Count > 0;
		string? exampleJson = hasRequestBody ? GetRequestBodyExampleJson(operation.RequestBody!) : null;
		string? exampleJsonCompact = hasRequestBody ? GetRequestBodyExampleJson(operation.RequestBody!, compact: true) : null;

		if (mCodeSampleLanguages.Contains("shell"))
		{
			sb.AppendLine("**Shell (curl)**");
			sb.AppendLine();
			sb.AppendLine("```bash");
			sb.AppendLine(sInv, $"curl -X {methodUpper} \"{{BASE_URL}}{path}\" \\");
			sb.AppendLine("  -H \"Authorization: Bearer $TOKEN\" \\");
			if (hasRequestBody && exampleJsonCompact is not null)
			{
				sb.AppendLine("  -H \"Content-Type: application/json\" \\");
				sb.AppendLine(sInv, $"  -d '{exampleJsonCompact}'");
			}
			else
			{
				sb.AppendLine("  -H \"Content-Type: application/json\"");
			}
			sb.AppendLine("```");
			sb.AppendLine();
		}

		if (mCodeSampleLanguages.Contains("csharp"))
		{
			sb.AppendLine("**C# (HttpClient)**");
			sb.AppendLine();
			sb.AppendLine("```csharp");
			sb.AppendLine("using var client = new HttpClient();");
			sb.AppendLine("client.DefaultRequestHeaders.Authorization =");
			sb.AppendLine("    new AuthenticationHeaderValue(\"Bearer\", token);");
			sb.AppendLine();
			if (hasRequestBody)
			{
				sb.AppendLine("var content = new StringContent(");
				sb.AppendLine("    JsonSerializer.Serialize(requestBody),");
				sb.AppendLine("    Encoding.UTF8,");
				sb.AppendLine("    \"application/json\");");
				sb.AppendLine();
				sb.AppendLine(sInv, $"var response = await client.{ToCSharpMethod(method)}Async(");
				sb.AppendLine(sInv, $"    $\"{{baseUrl}}{path}\",");
				sb.AppendLine("    content);");
			}
			else
			{
				sb.AppendLine(sInv, $"var response = await client.{ToCSharpMethod(method)}Async(");
				sb.AppendLine(sInv, $"    $\"{{baseUrl}}{path}\");");
			}
			sb.AppendLine("```");
			sb.AppendLine();
		}

		if (mCodeSampleLanguages.Contains("javascript"))
		{
			sb.AppendLine("**JavaScript (fetch)**");
			sb.AppendLine();
			sb.AppendLine("```javascript");
			sb.AppendLine(sInv, $"const response = await fetch(`${{BASE_URL}}{path}`, {{");
			sb.AppendLine(sInv, $"  method: '{methodUpper}',");
			sb.AppendLine("  headers: {");
			sb.AppendLine("    'Authorization': `Bearer ${token}`,");
			sb.AppendLine("    'Content-Type': 'application/json'");
			if (hasRequestBody)
			{
				sb.AppendLine("  },");
				sb.AppendLine("  body: JSON.stringify(requestBody)");
			}
			else
			{
				sb.AppendLine("  }");
			}
			sb.AppendLine("});");
			sb.AppendLine("```");
			sb.AppendLine();
		}

		if (mCodeSampleLanguages.Contains("python"))
		{
			sb.AppendLine("**Python (requests)**");
			sb.AppendLine();
			sb.AppendLine("```python");
			sb.AppendLine("import requests");
			sb.AppendLine();
			sb.AppendLine(sInv, $"response = requests.{methodLower}(");
			sb.AppendLine(sInv, $"    f\"{{BASE_URL}}{path}\",");
			if (hasRequestBody)
			{
				sb.AppendLine("    headers={\"Authorization\": f\"Bearer {token}\"},");
				sb.AppendLine("    json=request_body");
			}
			else
			{
				sb.AppendLine("    headers={\"Authorization\": f\"Bearer {token}\"}");
			}
			sb.AppendLine(")");
			sb.AppendLine("```");
			sb.AppendLine();
		}

		sb.AppendLine("</details>");
		sb.AppendLine();
	}

	/// <summary>
	/// Extracts an example JSON string from a request body definition.
	/// </summary>
	/// <param name="requestBody">The request body to extract an example from.</param>
	/// <param name="compact">
	/// If <see langword="true"/>, returns a single-line JSON string; otherwise,
	/// returns formatted JSON with indentation.
	/// </param>
	/// <returns>
	/// A JSON string representing an example request body, or <see langword="null"/>
	/// if no schema is available.
	/// </returns>
	private static string? GetRequestBodyExampleJson(IOpenApiRequestBody requestBody, bool compact = false)
	{
		// Try to find application/json content
		if (requestBody.Content is null) return null;

		foreach ((string contentType, IOpenApiMediaType mediaType) in requestBody.Content)
		{
			if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) continue;
			if (mediaType.Schema is null) continue;

			// Handle both direct schemas and schema references
			OpenApiSchema? schema = mediaType.Schema switch
			{
				OpenApiSchema direct             => direct,
				OpenApiSchemaReference schemaRef => schemaRef.Target as OpenApiSchema,
				var _                            => null
			};

			if (schema is not null) return SchemaToExampleJson(schema, compact);
		}

		return null;
	}

	/// <summary>
	/// Appends the schemas section with property tables and JSON examples.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	/// <param name="document">The OpenAPI document containing schema definitions.</param>
	private static void AppendSchemas(StringBuilder sb, OpenApiDocument document)
	{
		sb.AppendLine("## Schemas");
		sb.AppendLine();

		if (document.Components?.Schemas is null || document.Components.Schemas.Count == 0)
		{
			sb.AppendLine("No schemas defined.");
			sb.AppendLine();
			return;
		}

		foreach ((string name, IOpenApiSchema schemaInterface) in document.Components.Schemas.OrderBy(s => s.Key, StringComparer.Ordinal))
		{
			if (schemaInterface is not OpenApiSchema schema) continue;

			string anchor = ToAnchor(name);
			sb.AppendLine(sInv, $"### <a id=\"schema-{anchor}\"></a>`{name}`");
			sb.AppendLine();

			if (!string.IsNullOrWhiteSpace(schema.Description))
			{
				sb.AppendLine(schema.Description);
				sb.AppendLine();
			}

			if (schema.Properties?.Count > 0)
			{
				sb.AppendLine("| Property | Type | Required | Description |");
				sb.AppendLine("|----------|------|----------|-------------|");

				foreach ((string propName, IOpenApiSchema propSchemaInterface) in schema.Properties)
				{
					string type = GetSchemaTypeString(propSchemaInterface);
					string required = schema.Required?.Contains(propName) == true ? "✓" : "";
					string description = SanitizeForTable((propSchemaInterface as OpenApiSchema)?.Description);
					sb.AppendLine(sInv, $"| `{propName}` | `{type}` | {required} | {description} |");
				}

				sb.AppendLine();

				sb.AppendLine("<details>");
				sb.AppendLine("<summary><strong>Example</strong></summary>");
				sb.AppendLine();
				sb.AppendLine("```json");
				sb.AppendLine(SchemaToExampleJson(schema));
				sb.AppendLine("```");
				sb.AppendLine();
				sb.AppendLine("</details>");
				sb.AppendLine();
			}
			else if (schema.Enum?.Count > 0)
			{
				sb.AppendLine("**Enum Values:**");
				sb.AppendLine();
				foreach (JsonNode value in schema.Enum)
				{
					if (value is JsonValue jsonValue)
						sb.AppendLine(sInv, $"- `{jsonValue}`");
				}

				sb.AppendLine();
			}
		}
	}

	/// <summary>
	/// Appends the documentation footer with generation timestamp.
	/// </summary>
	/// <param name="sb">The <see cref="StringBuilder"/> to append to.</param>
	private static void AppendFooter(StringBuilder sb)
	{
		sb.AppendLine("---");
		sb.AppendLine();
		sb.AppendLine(sInv, $"*Generated by LumaCore.OpenApiGen — Do not edit manually.*");
	}

	/// <summary>
	/// Groups all endpoints in the document by their first tag.
	/// </summary>
	/// <param name="document">The OpenAPI document to process.</param>
	/// <returns>
	/// A dictionary mapping tag names to lists of endpoint tuples containing
	/// path, HTTP method, and operation details.
	/// </returns>
	/// <remarks>
	/// Endpoints without tags are grouped under <c>"Other"</c>.
	/// Only the first tag is used if an operation has multiple tags.
	/// </remarks>
	private static Dictionary<string, List<(string Path, HttpMethod Method, OpenApiOperation Operation)>>
		GetEndpointsByTag(OpenApiDocument document)
	{
		var result = new Dictionary<string, List<(string, HttpMethod, OpenApiOperation)>>();

		// Iterate all paths (e.g., "/api/auth/login", "/api/admin/status")
		foreach ((string path, IOpenApiPathItem pathItem) in document.Paths)
		{
			if (pathItem.Operations is null) continue;

			// Each path can have multiple operations (GET, POST, PUT, DELETE, etc.)
			foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations)
			{
				// Use first tag for grouping, or "Other" if untagged.
				// Tags in OpenAPI are typically used for logical grouping (e.g., "Auth", "Admin", "Health").
				string tag = operation.Tags?.FirstOrDefault()?.Name ?? "Other";

				if (!result.TryGetValue(tag, out List<(string, HttpMethod, OpenApiOperation)>? endpoints))
				{
					endpoints = [];
					result[tag] = endpoints;
				}

				endpoints.Add((path ?? "", method, operation));
			}
		}

		return result;
	}

	/// <summary>
	/// Converts text to a URL-safe anchor identifier.
	/// </summary>
	/// <param name="text">The text to convert.</param>
	/// <returns>
	/// Lowercase text with spaces and dots replaced by hyphens,
	/// suitable for use as an HTML anchor.
	/// </returns>
	private static string ToAnchor(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return "unknown";
		return text.ToLowerInvariant()
			.Replace(" ", "-")
			.Replace(".", "-");
	}

	/// <summary>
	/// Maps an OpenAPI operation type to the corresponding C# HttpClient method name.
	/// </summary>
	/// <param name="method">The HTTP method to map.</param>
	/// <returns>
	/// The C# method name (e.g., <c>"Get"</c>, <c>"Post"</c>), or <c>"Send"</c>
	/// for unsupported methods.
	/// </returns>
	private static string ToCSharpMethod(HttpMethod method)
	{
		return method.Method.ToUpperInvariant() switch
		{
			"GET"    => "Get",
			"POST"   => "Post",
			"PUT"    => "Put",
			"DELETE" => "Delete",
			"PATCH"  => "Patch",
			var _    => "Send"
		};
	}

	/// <summary>
	/// Gets a human-readable type string for an OpenAPI schema.
	/// </summary>
	/// <param name="schema">The schema to describe.</param>
	/// <returns>
	/// A type string like <c>"string"</c>, <c>"integer (int32)"</c>,
	/// <c>"User"</c> (for references), or <c>"User[]"</c> (for arrays).
	/// </returns>
	private static string GetSchemaTypeString(IOpenApiSchema? schema)
	{
		if (schema is null) return "object";

		// OpenAPI v3.x uses OpenApiSchemaReference for $ref pointers.
		// Return the referenced type name directly (e.g., "LoginRequest").
		if (schema is OpenApiSchemaReference schemaRef)
			return GetSchemaReferenceName(schemaRef) ?? "object";

		// Not a reference - must be a concrete schema definition.
		if (schema is not OpenApiSchema concreteSchema) return "object";

		string typeStr = GetTypeString(concreteSchema.Type) ?? "object";

		// Arrays: show as "ItemType[]" (e.g., "User[]", "string[]")
		if (typeStr == "array" && concreteSchema.Items is not null)
		{
			string itemType = concreteSchema.Items is OpenApiSchemaReference itemRef
				                  ? GetSchemaReferenceName(itemRef) ?? "object"
				                  : GetTypeString((concreteSchema.Items as OpenApiSchema)?.Type) ?? "object";
			return $"{itemType}[]";
		}

		// Include format for precision (e.g., "integer (int64)", "string (date-time)")
		if (!string.IsNullOrWhiteSpace(concreteSchema.Format)) return $"{typeStr} ({concreteSchema.Format})";

		return typeStr;
	}

	/// <summary>
	/// Converts a <see cref="JsonSchemaType"/> flags enum to a simple type string.
	/// </summary>
	/// <param name="type">The JSON schema type flags.</param>
	/// <returns>
	/// The primary type as a lowercase string (e.g., <c>"string"</c>, <c>"integer"</c>).
	/// Returns <see langword="null"/> if the type is <see langword="null"/> or unrecognized.
	/// </returns>
	private static string? GetTypeString(JsonSchemaType? type)
	{
		if (type is null) return null;

		// JsonSchemaType is a [Flags] enum - a schema can have multiple types (e.g., "string | null").
		// We return the first concrete type found, prioritizing in order of specificity.
		// Note: JsonSchemaType.Null is intentionally not checked - we treat nullable types as their base type.
		if (type.Value.HasFlag(JsonSchemaType.String)) return "string";
		if (type.Value.HasFlag(JsonSchemaType.Integer)) return "integer";
		if (type.Value.HasFlag(JsonSchemaType.Number)) return "number";
		if (type.Value.HasFlag(JsonSchemaType.Boolean)) return "boolean";
		if (type.Value.HasFlag(JsonSchemaType.Array)) return "array";
		if (type.Value.HasFlag(JsonSchemaType.Object)) return "object";

		return null;
	}

	/// <summary>
	/// Converts an OpenAPI schema to an example JSON string.
	/// </summary>
	/// <param name="schema">The schema to convert.</param>
	/// <param name="compact">
	/// If <see langword="true"/>, generates single-line JSON without indentation.
	/// Defaults to <see langword="false"/> for readable, indented output.
	/// </param>
	/// <returns>A formatted JSON string representing an example instance of the schema.</returns>
	private static string SchemaToExampleJson(OpenApiSchema schema, bool compact = false)
	{
		object? example = GenerateExampleValue(schema);
		return JsonSerializer.Serialize(example, compact ? sJsonOptionsCompact : sJsonOptions);
	}

	/// <summary>
	/// Generates an example value for an OpenAPI schema.
	/// </summary>
	/// <param name="schema">The schema to generate an example for.</param>
	/// <returns>
	/// An example value appropriate for the schema type. Uses the schema's
	/// <c>example</c> property if present, otherwise generates sensible defaults.
	/// </returns>
	/// <remarks>
	/// String formats are mapped to appropriate examples:
	/// <list type="bullet">
	///     <item><c>date-time</c> → <c>"2025-01-01T00:00:00Z"</c></item>
	///     <item><c>email</c> → <c>"user@example.com"</c></item>
	///     <item><c>uuid</c> → <c>"00000000-0000-0000-0000-000000000000"</c></item>
	/// </list>
	/// </remarks>
	private static object? GenerateExampleValue(OpenApiSchema? schema)
	{
		if (schema is null) return null;

		// Prefer explicit example from schema if provided
		if (schema.Example is not null) return JsonNodeToObject(schema.Example);

		string? typeStr = GetTypeString(schema.Type);

		// Generate sensible default values based on type and format.
		// String formats get realistic placeholder values for better documentation.
		return typeStr switch
		{
			"string" => schema.Format switch
			{
				"date-time" => "2025-01-01T00:00:00Z",                 // ISO 8601 format
				"date"      => "2025-01-01",                           // ISO 8601 date only
				"email"     => "user@example.com",                     // RFC 5321 compliant
				"uri"       => "https://example.com",                  // Valid URL
				"uuid"      => "00000000-0000-0000-0000-000000000000", // Nil UUID
				var _       => "string"                                // Generic placeholder
			},
			"integer" => 0,
			"number"  => 0.0,
			"boolean" => true,
			"array" => schema.Items is OpenApiSchema itemSchema
				           ? new[] { GenerateExampleValue(itemSchema) }
				           : Array.Empty<object>(),
			"object" => GenerateExampleObject(schema),
			var _    => null
		};
	}

	/// <summary>
	/// Generates an example object from an OpenAPI schema with properties.
	/// </summary>
	/// <param name="schema">The object schema containing property definitions.</param>
	/// <returns>
	/// A dictionary with property names as keys and generated example values.
	/// </returns>
	private static Dictionary<string, object?> GenerateExampleObject(OpenApiSchema schema)
	{
		var result = new Dictionary<string, object?>();

		if (schema.Properties is null) return result;

		foreach ((string name, IOpenApiSchema propSchemaInterface) in schema.Properties)
		{
			var propSchema = propSchemaInterface as OpenApiSchema;
			result[name] = GenerateExampleValue(propSchema);
		}

		return result;
	}

	/// <summary>
	/// Converts a JSON node to a .NET object for JSON serialization.
	/// </summary>
	/// <param name="node">The JSON node to convert.</param>
	/// <returns>
	/// The equivalent .NET object: primitives, arrays, or dictionaries
	/// for complex types. Returns <see langword="null"/> for <see langword="null"/> nodes.
	/// </returns>
	private static object? JsonNodeToObject(JsonNode? node)
	{
		return node switch
		{
			null            => null,
			JsonValue value => GetJsonValueAsObject(value),
			JsonArray arr   => arr.Select(JsonNodeToObject).ToArray(),
			JsonObject obj  => obj.ToDictionary(kv => kv.Key, kv => JsonNodeToObject(kv.Value)),
			var _           => null
		};
	}

	/// <summary>
	/// Extracts the underlying value from a <see cref="JsonValue"/>.
	/// </summary>
	/// <param name="value">The JSON value to extract.</param>
	/// <returns>The underlying .NET primitive value.</returns>
	private static object GetJsonValueAsObject(JsonValue value)
	{
		if (value.TryGetValue(out string? s)) return s;
		if (value.TryGetValue(out int i)) return i;
		if (value.TryGetValue(out long l)) return l;
		if (value.TryGetValue(out double d)) return d;
		if (value.TryGetValue(out bool b)) return b;

		return value.ToString();
	}
}
