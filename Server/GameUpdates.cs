using Microsoft.AspNetCore.SignalR;

// Each player receives their own view. A room-wide snapshot would leak secret
// cards and other players' unrevealed opinions.
internal static class GameUpdates
{
    internal static string PlayerGroup(string code, string id) => $"room:{code}:{id}";

    internal static Task Publish(IHubContext<GameHub> hub, Room room, CancellationToken cancellationToken = default)
    {
        var views = room.MemberViews();
        return Task.WhenAll(views.Select(view => hub.Clients.Group(PlayerGroup(room.Code, view.Id))
            .SendAsync("roomState", view.View, cancellationToken)));
    }
}
