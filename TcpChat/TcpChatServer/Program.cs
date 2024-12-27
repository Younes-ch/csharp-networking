using System.Net;
using System.Net.Sockets;
using System.Text;

// Create the server
const string name = "Chill";
const int port = 6000;
var chat = new TcpChatServer(name, port);

// Add a handler for a Ctrl-C press
Console.CancelKeyPress += (_, eventArgs) =>
{
    chat.Shutdown();
    eventArgs.Cancel = true;
};

// run the server
chat.Run();

internal class TcpChatServer
{
    private const int BufferSize = 2 * 1024; // 2KB
    private readonly TcpListener _listener;

    // Extra data
    private readonly string _chatName;
    private readonly int _port;
    private bool Running { get; set; }

    // Messages that need to be sent
    private readonly Queue<string> _messageQueue = new();
    
    // Types of clients connected
    private readonly List<TcpClient> _viewers = [];
    private readonly List<TcpClient> _messengers = [];

    // Name that are taken by messengers
    private readonly Dictionary<TcpClient, string> _names = [];

    // Make a new TCP chat server, with the provided name
    public TcpChatServer(string chatName, int port)
    {
        _chatName = chatName;
        _port = port;
        Running = false;

        // Make the listener listen for connections on any network device
        _listener = new TcpListener(IPAddress.Any, _port);
    }

    public void Shutdown()
    {
        Running = false;
        Console.WriteLine("Shutting down server...");
    }

    public void Run()
    {
        Console.WriteLine($"Starting the {_chatName} TCP Chat Server on port {_port}.");
        Console.WriteLine("Press Ctrl-C to shut down the server at any time.");

        _listener.Start();
        Running = true;

        // Main loop
        while (Running)
        {
            if (_listener.Pending())
                _handleNewConnection();

            _checkForDisconnects();
            _checkForNewMessages();
            _sendMessages();

            Thread.Sleep(10);
        }

        // Stop the server, and clean up any connected clients
        foreach (var viewer in _viewers)
            _cleanupClient(viewer);

        foreach (var messenger in _messengers)
            _cleanupClient(messenger);

        _listener.Stop();

        Console.WriteLine("Server is shut down.");
    }

    private void _handleNewConnection()
    {
        var good = false;
        var newClient = _listener.AcceptTcpClient(); // Blocks Thread
        var networkStream = newClient.GetStream();

        // Modify the default buffer sizes
        newClient.SendBufferSize = BufferSize;
        newClient.ReceiveBufferSize = BufferSize;

        // Print some info
        var endPoint = newClient.Client.RemoteEndPoint;
        Console.WriteLine($"Handling a new client from {endPoint}");


        var msgBuffer = new byte[BufferSize];
        var bytesRead = networkStream.Read(msgBuffer);
        Console.WriteLine($"Got {bytesRead} bytes.");

        if (bytesRead > 0)
        {
            var msg = Encoding.UTF8.GetString(msgBuffer, 0, bytesRead);

            if (msg == "viewer")
            {
                // They just want to watch
                good = true;
                _viewers.Add(newClient);

                Console.WriteLine($"{endPoint} is a Viewer.");

                // Send them a hello message
                msg = $"Welcome to the {_chatName} Chat Server!";

                msgBuffer = Encoding.UTF8.GetBytes(msg);

                networkStream.Write(msgBuffer);
            }
            else if (msg.StartsWith("name:"))
            {
                //  they might be a messenger
                var name = msg[(msg.IndexOf(':') + 1)..];

                if (!string.IsNullOrWhiteSpace(name) && !_names.ContainsValue(name))
                {
                    // They're new here, let's add them
                    good = true;
                    _names.Add(newClient, name);
                    _messengers.Add(newClient);

                    Console.WriteLine($"{endPoint} is a Messenger with the name {name}");

                    // Tell the viewers we have a new messenger
                    _messageQueue.Enqueue($"{name} has joined the chat.");
                }
            }
            else
            {
                // Wasn't either a viewer or messenger, clean up anyway.
                Console.WriteLine($"Wasn't able to identify {endPoint} as a Viewer or Messenger.");
                _cleanupClient(newClient);
            }
        }

        // Do we really want them?
        if (!good)
            newClient.Close();
    }

    // Clears out the message queue and sends it to all viewers
    private void _sendMessages()
    {
        foreach (var msg in _messageQueue)
        {
            // Encode the message
            var msgBuffer = Encoding.UTF8.GetBytes(msg);

            // Send the message to each viewer
            foreach (var viewer in _viewers) viewer.GetStream().Write(msgBuffer);
        }

        _messageQueue.Clear();
    }

    // See if any of our messengers have sent us a new message, put it in the queue
    private void _checkForNewMessages()
    {
        foreach (var messenger in _messengers)
        {
            var messageLength = messenger.Available;
            if (messageLength > 0)
            {
                var msgBuffer = new byte[messageLength];
                var _ = messenger.GetStream().Read(msgBuffer, 0, messageLength);

                var msg = $"{_names[messenger]}: {Encoding.UTF8.GetString(msgBuffer)}";
                _messageQueue.Enqueue(msg);
            }
        }
    }

    private void _checkForDisconnects()
    {
        // Check the viewers first
        foreach (var viewer in _viewers.ToList())
            if (_isDisconnected(viewer))
            {
                Console.WriteLine($"Viewer {viewer.Client.RemoteEndPoint} has left.");

                _viewers.Remove(viewer);
                _cleanupClient(viewer);
            }

        // Check the messengers second
        foreach (var messenger in _messengers.ToList())
            if (_isDisconnected(messenger))
            {
                // Get info about the messenger
                var name = _names[messenger];

                // Tell the viewers someone has left
                Console.WriteLine($"Messenger {messenger.Client.RemoteEndPoint} has left.");
                _messageQueue.Enqueue($"{name} has left the chat.");

                // Clean up on our end
                _messengers.Remove(messenger);
                _names.Remove(messenger);
                _cleanupClient(messenger);
            }
    }

    // Checks if a socket has disconnected
    private static bool _isDisconnected(TcpClient client)
    {
        try
        {
            var socket = client.Client;
            return socket.Poll(10 * 1000, SelectMode.SelectRead) && socket.Available == 0;
        }
        catch
        {
            return true;
        }
    }

    private static void _cleanupClient(TcpClient client)
    {
        client.GetStream().Close();
        client.Close();
    }
}