const string name = "Game Server";
const int port = 6000;

var gameServer = new TcpGamesServer.TcpGamesServer(name, port);

Console.CancelKeyPress += (_, eventArgs) =>
{
    gameServer.Shutdown();
    eventArgs.Cancel = true;
};

gameServer.Run();