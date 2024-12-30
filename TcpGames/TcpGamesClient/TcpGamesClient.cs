using System.Net.Sockets;
using System.Text;
using Shared;

namespace TcpGamesClient;

public class TcpGamesClient
{
    // Connection Objects
    private readonly string _serverAddress;
    private readonly int _port;
    private bool Running { get; set; }
    private readonly TcpClient _client;
    private bool _clientRequestedDisconnect;

    // Messaging
    private NetworkStream? _msgStream;
    private readonly Dictionary<string, Func<string, Task>> _commandHandlers = new();

    public TcpGamesClient(string serverAddress, int port)
    {
        // Create a non-connected Client
        _client = new TcpClient();
        Running = false;

        _serverAddress = serverAddress;
        _port = port;
    }

    // Cleans up any leftover network resources
    private void _cleanupNetworkResources()
    {
        _msgStream?.Close();
        _msgStream = null;
        _client.Close();
    }

    // Connects to the games server
    public void Connect()
    {
        // Connect to the server
        try
        {
            _client.Connect(_serverAddress, _port);
        }
        catch (SocketException se)
        {
            Console.WriteLine($"[ERROR] {se.Message}");
        }

        // Check that we've connected
        if (_client.Connected)
        {
            Console.WriteLine($"Connected to the server at {_client.Client.RemoteEndPoint}");
            Running = true;

            // Get the message stream
            _msgStream = _client.GetStream();

            // Hook up some packet packet command handlers
            _commandHandlers["bye"] = _handleBye;
            _commandHandlers["message"] = _handleMessage;
            _commandHandlers["input"] = _handleInput;
        }
        else
        {
            _cleanupNetworkResources();
            Console.WriteLine($"Wasn't able to connect to the server at {_serverAddress}:{_port}.");
        }
    }

    // Requests a disconnect, will send a "bye" message to the server
    // This should be only called by the user
    public void Disconnect()
    {
        Console.WriteLine("Disconnecting from the server...");
        Running = false;
        _clientRequestedDisconnect = true;
        _sendPacket(new Packet("bye")).GetAwaiter().GetResult();
    }

    // Main loop for the Games Client
    public void Run()
    {
        var wasRunning = Running;

        // Listen for messages
        List<Task> tasks = [];

        while (Running)
        {
            // Check for new packets
            tasks.Add(_handleIncomingPackets());

            // Use less CPU
            Thread.Sleep(10);

            // Make sure that we didn't have graceless disconnect
            if (_isDisconnected(_client) && !_clientRequestedDisconnect)
            {
                Running = false;
                Console.WriteLine("The server has disconnected from us ungracefully.");
            }
        }

        // Just in case we have anymore packets, give them one second to be processed
        Task.WaitAll(tasks.ToArray(), 1000);

        // Cleanup
        _cleanupNetworkResources();
        if (wasRunning)
            Console.WriteLine("Disconnected.");
    }

    // Sends packets to the server asynchronously
    private async Task _sendPacket(Packet packet)
    {
        try
        {
            // convert JSON to buffer and its length to a 16-bit unsigned integer buffer
            var jsonBuffer = Encoding.UTF8.GetBytes(packet.ToJson());
            var lengthBuffer = BitConverter.GetBytes(Convert.ToUInt16(jsonBuffer.Length));

            // Join the buffers
            var packetBuffer = new byte[lengthBuffer.Length + jsonBuffer.Length];
            lengthBuffer.CopyTo(packetBuffer, 0);
            jsonBuffer.CopyTo(packetBuffer, lengthBuffer.Length);

            // Send to packet
            await _msgStream.WriteAsync(packetBuffer, 0, packetBuffer.Length);
        }
        catch (Exception)
        {
            // ignored
        }
    }

    // Check for new incoming messages and handles them
    // This method will handle one Packet at a time, even if more than one is in the memory stream
    private async Task _handleIncomingPackets()
    {
        try
        {
            // Check for new incoming messages
            if (_client.Available > 0)
            {
                // There must be some incoming data, the first two bytes are the size of the Packet
                var lengthBuffer = new byte[2];
                await _msgStream.ReadExactlyAsync(lengthBuffer, 0, 2);
                var packetByteSize = BitConverter.ToUInt16(lengthBuffer, 0);

                // Now read that many bytes from what's left in the stream, it must be the Packet
                var jsonBuffer = new byte[packetByteSize];
                await _msgStream.ReadExactlyAsync(jsonBuffer, 0, jsonBuffer.Length);

                var jsonString = Encoding.UTF8.GetString(jsonBuffer);
                var packet = Packet.FromJson(jsonString);

                // Dispatch it
                try
                {
                    await _commandHandlers[packet?.Command!](packet?.Message!);
                }
                catch (KeyNotFoundException)
                {
                    // ignored
                }
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }

    #region Command Handlers

    private Task _handleBye(string message)
    {
        // Print the message
        Console.WriteLine("The server is disconnecting us with this message:");
        Console.WriteLine(message);

        // Will start the disconnection process in Run()
        Running = false;
        return Task.CompletedTask;
    }

    // Just prints out a message sent from the server
    private Task _handleMessage(string message)
    {
        Console.Write(message);
        return Task.CompletedTask;
    }

    // Gets input from the user and sends it to the server
    private async Task _handleInput(string message)
    {
        // Print the prompt and get a response to send
        Console.Write(message);
        var responseMsg = Console.ReadLine();

        // Send the response
        var resp = new Packet("input", responseMsg!);
        await _sendPacket(resp);
    }

    #endregion

    #region Helper Methods

    // Checks if a client has disconnected ungracefully
    private static bool _isDisconnected(TcpClient client)
    {
        try
        {
            var socket = client.Client;
            return socket.Poll(10 * 1000, SelectMode.SelectRead) && socket.Available == 0;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    #endregion
}