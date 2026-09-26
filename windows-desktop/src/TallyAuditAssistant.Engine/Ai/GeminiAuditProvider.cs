using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Ai;

public class GeminiAuditProvider : IAuditAiProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<GeminiAuditProvider> _logger;

    public string ProviderName => "Google Gemini AI";

    public bool IsConfigured { get; private set; } = false;

    public GeminiAuditProvider(
        IHttpClientFactory httpClientFactory,
        ISettingsService settingsService,
        ILogger<GeminiAuditProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    private async Task<string> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        // 1. Check environment variable
        var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrEmpty(envKey))
        {
            IsConfigured = true;
            return envKey;
        }

        // 2. Check local settings database
        var settingsKey = await _settingsService.GetSettingAsync("GeminiApiKey", "", cancellationToken);
        if (!string.IsNullOrEmpty(settingsKey))
        {
            IsConfigured = true;
            return settingsKey;
        }

        IsConfigured = false;
        return string.Empty;
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return "The AI operation was cancelled.";
        }

        try
        {
            var apiKey = await GetApiKeyAsync(cancellationToken);
            if (string.IsNullOrEmpty(apiKey))
            {
                return "AI assistance is not configured. Please supply a valid Google Gemini API key in Settings.";
            }

            _logger.LogInformation("Sending prompt to Gemini AI...");

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // Using the recommended standard model 'gemini-2.5-flash' for basic text tasks
            const string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
            var requestUrl = $"{url}?key={apiKey}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"{systemPrompt}\n\nUser Input Context:\n{userPrompt}" }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 2048
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            using var response = await client.PostAsync(
                requestUrl,
                new StringContent(jsonContent, Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Gemini API call failed with status {Status}: {Error}", response.StatusCode, errContent);
                return $"Gemini API request failed ({response.StatusCode}). Please verify your internet connection, billing, or API key.";
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);

            // Navigate safety structure: candidates -> content -> parts -> text
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textProp))
            {
                return textProp.GetString() ?? "No content returned from AI assistant.";
            }

            return "The AI assistant returned an empty response. Verify structure compatibility.";
        }
        catch (OperationCanceledException)
        {
            return "The AI operation was cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Gemini API");
            return $"AI Error: {ex.Message}";
        }
    }
}
