namespace TallyAuditAssistant.Core.Interfaces;

public interface ISecureStorage
{
    void SetSecret(string key, string value);
    string? GetSecret(string key);
    void RemoveSecret(string key);
    bool HasSecret(string key);
}
