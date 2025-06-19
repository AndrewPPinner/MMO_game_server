using MMOPlatformer.API;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseWebSockets();
app.UseHttpsRedirection();

Task.Run(() => GameLoop.StartGame());

app.MapGet("/ws/gameloop", GameLoop.Connect);

app.Run();
