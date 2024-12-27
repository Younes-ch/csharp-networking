using System.Net.Sockets;
using System.Text;

const string host = "localhost";
const int port = 6000;


var viewer = new TcpChatViewer(host, port);
viewer.Connect();
viewer.ListenForMessages();

Console.CancelKeyPress += (_, eventArgs) =>
{
    viewer.Disconnect();
    eventArgs.Cancel = true;
};

internal class TcpChatViewer
{
    // Connection objects
    private readonly string _serverAddress;
    private readonly int _port;
    private readonly TcpClient _client;
    private bool Running { get; set; }
    private bool _disconnectRequested = false;
    
    // Buffer & messaging
    private const int BufferSize = 2 * 1024;
    private NetworkStream? _msgStream;

    public TcpChatViewer(string serverAddress, int port)
    {
        // Create a non-connected TcpClient
        _client = new TcpClient();
        _client.SendBufferSize = BufferSize;
        _client.ReceiveBufferSize = BufferSize;
        Running = false;
        
        // Set the other things
        _serverAddress = serverAddress;
        _port = port;
    }

    public void Connect()
    {
        // Now let's try to connect
        Console.WriteLine("Attempting to connect...");
        _client.Connect(_serverAddress, _port);
        var endPoint = _client.Client.RemoteEndPoint;

        // check that we're connected
        if (_client.Connected)
        {
            // We're in
            Console.WriteLine($"Connected to the server at {endPoint}.");
            
            // Send them the message that we're a viewer
            _msgStream = _client.GetStream();
            var msgBuffer = Encoding.UTF8.GetBytes("viewer");
            _msgStream.Write(msgBuffer);

            if (!_isDisconnected(_client))
            {
                Running = true;
                Console.WriteLine("Press Ctrl-C to exit the Viewer at any time.");
            }
            else
            {
                _cleanupNetworkResources();
                Console.WriteLine("The server didn't recognise us as a Viewer.");
            }
        }
        else
        {
            _cleanupNetworkResources();
            Console.WriteLine("Wasn't able to connect to the server at {0}.", endPoint);
        }
    }

    public void ListenForMessages()
    {
        var wasRunning = Running;
        while (Running)
        {
            var messageLength = _client.Available;
            if (messageLength > 0)
            {
                Console.WriteLine($"New incoming message of {messageLength} bytes");
                
                // Read the whole message
                var msgBuffer = new byte[messageLength];
                var bytesRead = _msgStream?.Read(msgBuffer, 0, messageLength);
                var msg = Encoding.UTF8.GetString(msgBuffer);

                Console.WriteLine(msg);
            }
            
            // Use less CPU
            Thread.Sleep(10);
            
            // Check the server didn't disconnect us
            if (_isDisconnected(_client))
            {
                Running = false;
                Console.WriteLine("Server has disconnected from us.");
            }
            
            // Check that a cancel has been requested
            Running &= !_disconnectRequested;
        }
        
        // Cleanup
        _cleanupNetworkResources();
        if (wasRunning)
            Console.WriteLine("Disconnected.");
    }

    // Requests a disconnect
    public void Disconnect()
    {
        Running = false;
        _disconnectRequested = true;
        Console.WriteLine("Disconnecting from the chat...");
    }

    private static bool _isDisconnected(TcpClient client)
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

    private void _cleanupNetworkResources()
    {
        _msgStream?.Close();
        _msgStream = null;
        _client.Close();
    }
}
