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
                _gameState.Players.Add(ip, new(new(0, 0)));
            }

            var gameUpdateTask = Task.Run(async () =>
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    //Might have to filter down the data sent if alot of players playing
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

                    var requestBody = JsonSerializer.Deserialize<Request>(Encoding.UTF8.GetString(buffer, 0, request.Count));
                    if (requestBody is not null && _gameState.Players.TryGetValue(ip, out var player))
                    {
                        player.Position = requestBody.pos;
                        player.GlobalMessage = requestBody.message;
                        _gameState.Players[ip] = player;
                    }
                }
            });

            await Task.WhenAny(listenerTask, gameUpdateTask);
            _gameState.Players.Remove(ip);
            
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
        }
    }

    public async static void StartGame()
    {
        _gameState = new GameState();

        while (true)
        {
            var furthest = _gameState.Obstacles.OrderByDescending(x => x.brCorner.X).FirstOrDefault();
            _gameState.Obstacles.AddRange(await GenerateObstacles(Convert.ToInt32(_gameState.CurrentHighest), furthest?.brCorner.X ?? 0));

            //Generate Obstacles and world entities
            //Save current highest to db as AllTimeHighScore (Maybe do this after CurrentHighest player dies)
            //Validate players aren't cheating (add to ban list or fuck with cheater)
            //Kill players
        }
    }

    private static Random _random = new Random();

    private async static Task<List<Obstacle>> GenerateObstacles(int currentHighest, int furthestObstacle)
    {
        if (currentHighest + 10000 < furthestObstacle)
        {
            return [];
        }

        //Create random function that falls within constraints (max height, max length, position)
        var tasks = new List<Task<Obstacle>>();
        var index = 0;
        while (index < 50)
        {
            index++;
            var taskNum = index;
            tasks.Add(Task.Run<Obstacle>(() =>
            {
                var startX = furthestObstacle  + (taskNum * 250);
                var endX = startX + 200;
                var startY = _random.Next(0, 500);
                var endY = startY + 75;

                return new()
                {
                    tlCorner = new(startX, startY),
                    brCorner = new(endX, endY)
                };
            }));
        }

        var res = await Task.WhenAll(tasks);
        return res.ToList();
    }

    record Request(string? message, SerializableVector2 pos);
    record Response(string message);
}

public class GameState
{
    public Dictionary<string, Player> Players { get; set; } = [];
    public float CurrentHighest { get => Players.Select(x => x.Value).OrderByDescending(x => x.Position.X).FirstOrDefault()?.Position.X ?? 0f; } //This seems slow
    public int CurrentPlayers { get => Players.Count;}
    public int AllTimeHighScore { get; set; }
    public List<Obstacle> Obstacles { get; set; } = []; //Not sure how I want this to be generated because this will get massive as players progress
}

public class Obstacle
{
    public SerializableVector2 tlCorner { get; set; }
    public SerializableVector2 brCorner { get; set; }
}

public class SerializableVector2
{
    public SerializableVector2()
    {
        
    }

    public SerializableVector2(Vector2 vector)
    {
        X = (int)vector.X;
        Y = (int)vector.Y;
    }

    public SerializableVector2(int x, int y)
    {
        X = x;
        Y = y;
    }

    public Vector2 ToVector2()
    {
        return new Vector2(X, Y);
    }

    public int X { get; set; }
    public int Y { get; set; }
}

public class Player
{
    public SerializableVector2 Position { get; set; }
    public string Name { get; } = NameGenerator();
    public string? GlobalMessage { get; set; } //TODO: Set time out for messages to only last 5 seconds before getting cleared (might break out into webRTC or something for prox chat)
    public int Speed { get; set; } //Only set by game state

    public Player(SerializableVector2 position)
    {
        Position = position;
        Speed = 1;
    }

    private static string NameGenerator()
    {
        return "RandomName";
    }
}
