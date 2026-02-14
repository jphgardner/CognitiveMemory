using System.Security.Cryptography;
using System.Text;

namespace CognitiveMemory.Application.AI.Tooling;

public static class MemoryIdentity
{
    public static string ComputeContentHash(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    public static string ComputeClaimHash(string subjectKey, string predicate, string? literalValue)
    {
        var value = $"{subjectKey}|{predicate}|{literalValue}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static Guid ComputeStableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }
}
