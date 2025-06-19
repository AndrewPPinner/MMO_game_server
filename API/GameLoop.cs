using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MMOPlatformer.API;

public static class GameLoop
{
    private static GameState _gameState;

    public async static Task Connect(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var ip = context.Connection.RemoteIpAddress?.ToString();

            if (!_gameState.Players.TryGetValue(ip, out _))
            {
                var responseBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Response("Success")));
                await webSocket.SendAsync(responseBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                _gameState.Players.Add(ip, new(new(0f, 0f)));
            }

            var gameUpdateTask = Task.Run(async () =>
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var gameBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_gameState));
                    await webSocket.SendAsync(gameBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                    Thread.Sleep(500);
                }
            });

            var listenerTask = Task.Run(async () =>
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var buffer = new byte[1024 * 4];
                    var request = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (request.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                }
            });

            await Task.WhenAny(listenerTask, gameUpdateTask);
            _gameState.Players.Remove(ip);
            
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
        }
    }

    public static void StartGame()
    {
        _gameState = new GameState();

        while (true)
        {
        }
    }

    record Request();
    record Response(string message);
}

public class GameState
{
    public Dictionary<string, Player> Players { get; set; } = [];
}

public class Player
{
    public Vector2 Position { get; set; }
    public string Name { get; set; } = NameGenerator();
    public string? GlobalMessage { get; set; }

    public Player(Vector2 position)
    {
        Position = position;
    }

    private static string NameGenerator()
    {
        return "RandomName";
    }
}
