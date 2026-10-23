namespace Synapse.Blocks.Models;

public sealed record AdminLoginRequest(string Password);
public sealed record AdminSessionInfo(bool Authenticated);
