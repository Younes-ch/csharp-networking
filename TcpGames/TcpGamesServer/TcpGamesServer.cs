using System.Net;
using System.Net.Sockets;
using System.Text;
using Shared;

namespace TcpGamesServer;

public class TcpGamesServer
{
    // Listen for new incoming connections
    private readonly TcpListener _listener;

    // Clients objects
    private readonly List<TcpClient> _players = [];
    private readonly List<TcpClient> _waitingLobby = [];

    // Game stuff
    private readonly Dictionary<TcpClient, IGame> _gameClientIsIn = new();
    private readonly List<IGame> _games = [];
    private readonly List<Thread> _gameThreads = [];
    private IGame _nextGame;

    // Other data
    private readonly string _name;
    private readonly int _port;
    private bool Running { get; set; }

    // Construct to create a new Games Server
    public TcpGamesServer(string name, int port)
    {
        // Set some of the basic data
        _name = name;
        _port = port;
        Running = false;

        // Create the listener
        _listener = new TcpListener(IPAddress.Any, _port);
    }

    public void Shutdown()
    {
        if (Running)
        {
            Running = false;
            Console.WriteLine("Shutting down the Game(s) Server...");
        }
    }

    // The main loop for the games server
    public void Run()
    {
        Console.WriteLine($"Starting the \"{_name}\" Game(s) Server on port {_port}");
        Console.WriteLine("Press Ctrl-C to shutdown the server at any time.");

        // Start the next game
        _nextGame = new GuessMyNumberGame(this);


        // Starting running the server
        _listener.Start();
        Running = true;
        List<Task> newConnectionTasks = [];
        Console.WriteLine("Waiting for incoming connections...");

        while (Running)
        {
            // Handle new clients
            if (_listener.Pending())
                newConnectionTasks.Add(_handleNewConnection());

            // Once we have enough clients for the next game, add them in and start the game
            if (_waitingLobby.Count >= _nextGame.RequiredPlayers)
            {
                // Get that many players from the waiting lobby and start the game
                var numPlayers = 0;
                while (numPlayers < _nextGame.RequiredPlayers)
                {
                    // Pop the first one off
                    var player = _waitingLobby[0];
                    _waitingLobby.RemoveAt(0);

                    // Try adding it to the game. If failure, put it back in the lobby
                    if (_nextGame.AddPlayer(player))
                        numPlayers++;
                    else
                        _waitingLobby.Add(player);
                }

                // Start the game in a new thread!
                Console.WriteLine($"Starting a \"{_nextGame.Name}\" game.");
                var gameThread = new Thread(_nextGame.Run);
                gameThread.Start();
                _games.Add(_nextGame);
                _gameThreads.Add(gameThread);

                // Create a new game
                _nextGame = new GuessMyNumberGame(this);
            }

            // Check if any clients have disconnected in waiting, gracefully or not
            // NOTE: This could (and should) be parallelized
            foreach (var player in _waitingLobby)
            {
                var endPoint = player.Client.RemoteEndPoint;

                // Check for graceful first
                var p = ReceivePacket(player).GetAwaiter().GetResult();
                var disconnected = (p?.Command == "bye");

                // Then ungraceful
                disconnected |= IsDisconnected(player);

                if (disconnected)
                {
                    HandleDisconnectedPlayer(player);
                    Console.WriteLine($"Player {endPoint} has disconnected from the Game(s) Server.");
                }
            }

            // Use less CPU
            Thread.Sleep(10);
        }

        // In the chance a client connected, but we exited the loop, give them 1 second to finish
        Task.WaitAll(newConnectionTasks.ToArray(), 1000);

        // Shutdown all the threads, regardless if they are done or not
        foreach (var thread in _gameThreads)
            thread.Interrupt();

        // Disconnect any clients still here
        Parallel.ForEach(_players, (player) => { DisconnectPlayer(player, "The Game(s) Server is being Shutdown."); });

        // Cleanup resources
        _listener.Stop();

        // Info
        Console.WriteLine("The server has been shut down.");
    }

    // Awaits for a new connection and then adds them to the waiting lobby
    private async Task _handleNewConnection()
    {
        // Get the new client using a Future
        var newPlayer = await _listener.AcceptTcpClientAsync();
        Console.WriteLine($"New connection from {newPlayer.Client.RemoteEndPoint}");

        // Store them and put them in the waiting lobby
        _players.Add(newPlayer);
        _waitingLobby.Add(newPlayer);

        // Send a welcome message
        var msg = $"Welcome to the \"{_name}\" Games Server.\n";
        await SendPacket(newPlayer, new Packet("message", msg));
    }

    // Will attempt to gracefully disconnect a TcpClient
    // This should be used for clients that may be in a game, or the waiting lobby
    public void DisconnectPlayer(TcpClient player, string message = "")
    {
        Console.WriteLine($"Disconnecting the client from {player.Client.RemoteEndPoint}.");

        // If there wasn't a message set, use the default "Goodbye."
        if (message == "")
            message = "Goodbye.";

        // Send the "bye," message
        var byePacket = SendPacket(player, new Packet("bye", message));

        // Notify a game that might have them
        try
        {
            _gameClientIsIn[player]?.DisconnectPlayer(player);
        }
        catch (KeyNotFoundException)
        {
        }

        // Give the client some time to send and process the graceful disconnect
        Thread.Sleep(100);

        // Cleanup resources on our end
        byePacket.GetAwaiter().GetResult();
        HandleDisconnectedPlayer(player);
    }

    // Cleans up the resources if a player has disconnected,
    // gracefully or not.  Will remove them from clint list and lobby
    public void HandleDisconnectedPlayer(TcpClient player)
    {
        // Remove from collections and free resources
        _players.Remove(player);
        _waitingLobby.Remove(player);
        _cleanupPlayer(player);
    }

    #region Packet Transmission Methods

    // Send a packet to a client asynchronously
    public async Task SendPacket(TcpClient player, Packet packet)
    {
        try
        {
            // Convert JSON to buffer and its length to a 16-bit unsigned integer buffer
            var jsonBuffer = Encoding.UTF8.GetBytes(packet.ToJson());
            var lengthBuffer = BitConverter.GetBytes(Convert.ToUInt16(jsonBuffer.Length));

            // Join the buffers
            var msgBuffer = new byte[lengthBuffer.Length + jsonBuffer.Length];
            lengthBuffer.CopyTo(msgBuffer, 0);
            jsonBuffer.CopyTo(msgBuffer, lengthBuffer.Length);

            // Send the packet
            await player.GetStream().WriteAsync(msgBuffer);

            Console.WriteLine($"[SENT]\n{packet}");
        }
        catch (Exception e)
        {
            // There was an issue in sending
            Console.WriteLine("There was an issue receiving a packet.");
            Console.WriteLine($"Reason: {e.Message}");
        }
    }

    // Will get a single packet from a TcpClient
    // Will return null if there isn't any data available or some other
    // issue getting data from the client
    public async Task<Packet?> ReceivePacket(TcpClient player)
    {
        Packet? packet = null;
        try
        {
            // First check there is data available
            if (player.Available == 0)
                return null;

            var msgStream = player.GetStream();

            // There must be some incoming data, the first two bytes are the size of the Packet
            var lengthBuffer = new byte[2];
            await msgStream.ReadExactlyAsync(lengthBuffer, 0, 2);
            var packetByteSize = BitConverter.ToUInt16(lengthBuffer, 0);

            // Now read that many bytes from what's left in the stream, it must be the packet
            var jsonBuffer = new byte[packetByteSize];
            await msgStream.ReadExactlyAsync(jsonBuffer, 0, jsonBuffer.Length);

            // Convert it into a packet datatype
            var jsonString = Encoding.UTF8.GetString(jsonBuffer);
            packet = Packet.FromJson(jsonString);

            Console.WriteLine($"[RECEIVED]\n{packet}");
        }
        catch (Exception e)
        {
            // There was an issue in receiving
            Console.WriteLine("There was an issue sending a packet to {0}.", player.Client.RemoteEndPoint);
            Console.WriteLine("Reason: {0}", e.Message);
        }

        return packet;
    }

    #endregion

    #region TcpClient Helper Methods

    public static bool IsDisconnected(TcpClient player)
    {
        try
        {
            var socket = player.Client;
            return socket.Poll(10 * 1000, SelectMode.SelectRead) && socket.Available == 0;
        }
        catch
        {
            return true;
        }
    }

    private static void _cleanupPlayer(TcpClient player)
    {
        player?.GetStream().Close();
        player?.Close();
    }

    #endregion
}