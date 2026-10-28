using System.Net.Http.Json;
using Messenger.Frontend.Models;

namespace Messenger.Frontend.Services;

public class MessageApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<MessageDto>> GetAllAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<MessageDto>>("api/messages", ct) ?? [];

    public async Task<MessageDto> SendAsync(string text, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/messages", new SendMessageRequest(text), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MessageDto>(ct))!;
    }
}
