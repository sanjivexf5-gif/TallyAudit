using System.Threading;
using System.Threading.Tasks;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IAuditAiProvider
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
