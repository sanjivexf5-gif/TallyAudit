using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Ai;

public class NoAiProvider : IAuditAiProvider
{
    public string ProviderName => "None (AI Disabled)";
    public bool IsConfigured => false;

    public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("AI assistance is not configured.");
    }
}
