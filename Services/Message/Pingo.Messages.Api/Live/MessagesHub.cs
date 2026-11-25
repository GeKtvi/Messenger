using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Pingo.Messages.Api.Live;

// Pushes every saved message to the open chats (SignalR). Clients only listen: they still send
// through PUT /api/messages/{id}.
[Authorize]
public class MessagesHub : Hub
{
    // Under /api/messages, so a proxy that routes that path sends the hub to this service too.
    public const string Path = "/api/messages/hub";

    // The client method that receives a MessageResponse.
    public const string MessageSaved = "MessageSaved";
}
