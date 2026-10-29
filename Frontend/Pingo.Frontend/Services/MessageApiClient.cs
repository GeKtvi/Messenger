using System.Net.Http.Json;
using Pingo.Frontend.Models;

namespace Pingo.Frontend.Services;

public class MessageApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<MessageDto>> GetAllAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<MessageDto>>("api/messages", ct) ?? [];

    // PUT is idempotent: sending again with the same id never creates a duplicate.
    public async Task<MessageDto> SendAsync(Guid id, string text, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"api/messages/{id}", new SendMessageRequest(text), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MessageDto>(ct))!;
    }
}
