using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Pingo.Messages.Api.Contracts;

namespace Pingo.Messages.ComponentTests;

public static class MessagesHttpClientExtensions
{
    public static Task<HttpResponseMessage> SendMessageAsync(this HttpClient client, Guid id, string text) =>
        client.PutAsJsonAsync($"/api/messages/{id}", new SendMessageRequest { Text = text });

    // Sends a body exactly as given, for requests a typed client can't produce.
    public static Task<HttpResponseMessage> SendMessageJsonAsync(this HttpClient client, Guid id, string json) =>
        client.PutAsync($"/api/messages/{id}", new StringContent(json, Encoding.UTF8, "application/json"));

    public static async Task<List<MessageResponse>> GetMessagesAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/messages");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<MessageResponse>>())!;
    }

    public static async Task<MessageResponse> ReadMessageAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<MessageResponse>())!;

    // Checks the response is a 400 validation problem and returns its errors by field.
    public static async Task<IDictionary<string, string[]>> ReadValidationErrorsAsync(this HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        return problem!.Errors;
    }
}
