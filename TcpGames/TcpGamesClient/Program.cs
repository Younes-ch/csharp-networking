const string host = "localhost";
const int port = 6000;
var gamesClient = new TcpGamesClient.TcpGamesClient(host, port);

Console.CancelKeyPress += (_, eventArgs) =>
{
    gamesClient.Disconnect();
    eventArgs.Cancel = true;
};

gamesClient.Connect();
gamesClient.Run();
