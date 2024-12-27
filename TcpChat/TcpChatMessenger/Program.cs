using System.Net.Sockets;
using System.Text;

string? name;
// Get a valid name
while (true)
{
    Console.Write("Enter a name to use: ");
    name = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(name))
        break;
}

// Setup the messenger
const string host = "localhost";
const int port = 6000;
var messenger = new TcpChatMessenger(host, port, name!);

// Connect and send messages
messenger.Connect();
messenger.SendMessages();

internal class TcpChatMessenger
{
    private readonly string _serverAddress;
    private readonly int _port;
    private readonly TcpClient _client;
    private bool Running { get; set; }

    // Buffer & Messaging
    private const int BufferSize = 2 * 1024; // 2 KB
    private NetworkStream? _msgStream;

    // Personal data
    private readonly string _name;

    public TcpChatMessenger(string serverAddress, int port, string name)
    {
        // Create a non-connected Tcp Client
        _client = new TcpClient();
        _client.SendBufferSize = BufferSize;
        _client.ReceiveBufferSize = BufferSize;
        Running = false;

        // Set the other things
        _serverAddress = serverAddress;
        _port = port;
        _name = name;
    }

    public void Connect()
    {
        Console.WriteLine("Attempting to connect...");
        
        // Try to connect
        _client.Connect(_serverAddress, _port);
        var endPoint = _client.Client.RemoteEndPoint;

        if (_client.Connected)
        {
            // We're in
            Console.WriteLine($"Connected to the server at {endPoint}");

            // Tell the server we're a messenger
            _msgStream = _client.GetStream();
            var msgBuffer = Encoding.UTF8.GetBytes($"name:{_name}");
            _msgStream.Write(msgBuffer);

            // If we're still connected after sending our name, that means the servers accepts us
            if (!IsDisconnected(_client))
                Running = true;
            else
            {
                // Name was probably taken
                CleanupNetworkResources();
                Console.WriteLine($"The server rejected us; \"{_name}\" is probably taken.");
            }
        }
        else
        {
            CleanupNetworkResources();
            Console.WriteLine($"Wasn't able to connect to the server at {endPoint}");
        }
    }

    public void SendMessages()
    {
        var wasRunning = Running;

        while (Running)
        {
            // Poll for user input
            Console.Write($"{_name}> ");
            var msg = Console.ReadLine();

            // Quit or send a message
            if (msg.ToLower() == "quit" || msg.ToLower() == "exit")
            {
                Console.WriteLine("Disconnecting...");
                Running = false;
            }
            else if (!string.IsNullOrWhiteSpace(msg))
            {
                // Send the message
                var msgBuffer = Encoding.UTF8.GetBytes(msg);
                _msgStream?.Write(msgBuffer);
            }

            // Use less CPU
            Thread.Sleep(10);

            // Check the server didn't disconnect us
            if (IsDisconnected(_client))
            {
                Running = false;
                Console.WriteLine("Server has disconnected us.");
            }
        }

        CleanupNetworkResources();
        if (wasRunning)
            Console.WriteLine("Disconnected");
    }

    // Cleans any leftover network resources
    private void CleanupNetworkResources()
    {
        _msgStream?.Close();
        _msgStream = null;
        _client.Close();
    }

    // Checks if a socket has disconnected
    private static bool IsDisconnected(TcpClient client)
    {
        try
        {
            var socket = client.Client;
            return socket.Poll(10 * 1000, SelectMode.SelectRead) && (socket.Available == 0);
        }
        catch
        {
            return true;
        }
    }
}