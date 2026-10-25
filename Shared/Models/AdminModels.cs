namespace Synapse.Blocks.Models;

public sealed record AdminLoginRequest(string Username, string Password);
public sealed record AdminSessionInfo(bool Authenticated, string? Username = null);
