using System.Net.Http.Json;
using Synapse.Blocks.Models;
using Synapse.Blocks.Serialization;

namespace Synapse.Blocks.Services;

public sealed class AdminSession(HttpClient http)
{
    public async Task<bool> IsAuthenticatedAsync()
        => (await http.GetFromJsonAsync("api/admin/session", AppJsonSerializerContext.Default.AdminSessionInfo))?.Authenticated == true;

    public async Task LoginAsync(string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/admin/login")
        {
            Content = JsonContent.Create(new AdminLoginRequest(password), AppJsonSerializerContext.Default.AdminLoginRequest)
        };
        request.Headers.Add("X-Synapse-Request", "1");
        using var response = await http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Слишком много попыток входа. Повторите через минуту.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Неверный пароль." : "Не удалось войти. Проверьте доступность сервера.");
    }

    public async Task LogoutAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/admin/logout");
        request.Headers.Add("X-Synapse-Request", "1");
        using var response = await http.SendAsync(request);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized) response.EnsureSuccessStatusCode();
    }
}
