using Microsoft.JSInterop;

namespace Pingo.Frontend.Tests;

// The browser's localStorage with a single value, which is all the outbox uses.
public sealed class FakeLocalStorage : IJSRuntime
{
    public string? Value { get; set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        if (identifier == "localStorage.setItem")
        {
            Value = (string?)args![1];
            return default;
        }

        return new ValueTask<TValue>((TValue)(object)Value!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);
}
