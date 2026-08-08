using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BotMaster;

/// <summary>
/// Signs the JWTs the game client presents to game-server instances.
/// The instance validates with: issuer "MasterServer", audience "ServerInstance",
/// and the symmetric key this master hands out via Instance.Ping / Master.GetConfig
/// (see NetworkManagerMMO.OnServerLogin + PingPong in the decompiled client).
/// </summary>
public class JwtTokens
{
    public const string Issuer = "MasterServer";
    public const string Audience = "ServerInstance";

    public byte[] Key { get; }
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokens(string keyFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
        if (File.Exists(keyFile))
        {
            Key = Convert.FromBase64String(File.ReadAllText(keyFile).Trim());
        }
        else
        {
            Key = RandomNumberGenerator.GetBytes(48); // HS256 needs >= 32 bytes
            File.WriteAllText(keyFile, Convert.ToBase64String(Key));
        }
        _signingKey = new SymmetricSecurityKey(Key);
    }

    /// <summary>Create a token carrying the account id in the "uid" claim.</summary>
    public string Sign(long accountId, TimeSpan? ttl = null)
    {
        ttl ??= TimeSpan.FromDays(7);
        var handler = new JsonWebTokenHandler();
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object> { ["uid"] = accountId },
            NotBefore = now,
            Expires = now.Add(ttl.Value),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        };
        return handler.CreateToken(descriptor);
    }
}
