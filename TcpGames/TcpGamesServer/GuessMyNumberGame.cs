using System.Net.Sockets;
using Shared;

namespace TcpGamesServer;

public class GuessMyNumberGame : IGame
{
    // Objects for the game
    private readonly TcpGamesServer _server;
    private TcpClient? _player;
    private readonly Random _rng;
    private bool _needToDisconnectClient;

    // Name of the game
    public string Name => "Guess My Number";
    
    // Just needs one player
    public int RequiredPlayers => 1;

    public GuessMyNumberGame(TcpGamesServer server)
    {
        _server = server;
        _rng = new Random();
    }
    
    // Adds only a single player to the game
    public bool AddPlayer(TcpClient player)
    {
        // Make sure only one player was added
        if (_player is not null) return false;
        
        _player = player;
        return true;
    }
    
    // If the client who disconnected is ours, we need to quit our game
    public void DisconnectPlayer(TcpClient player)
    {
        _needToDisconnectClient = (_player == player);
    }
    
    // Main loop of the Game
    // Packet are sent synchronously
    public void Run()
    {
        // Make sure we have a player
        var running = _player is not null;

        if (running)
        {
            // Send instruction packet
            var introPacket = new Packet("message", """
                                                    Welcome player, I want you to guess my number.\n
                                                    It's somewhere between (and including) 1 and 100.\n
                                                    """);
            _server.SendPacket(_player, introPacket).GetAwaiter().GetResult();
        }
        else
            return;
        
        // Should be [1, 100]
        var answer = _rng.Next(1, 101);
        Console.WriteLine($"Our number is: {answer}");
        
        // Some state for the game
        var correct = false;
        var playerConnected = true;
        var playerDisconnectedGracefully = false;
        
        // Main game loop
        while (running)
        {
            // Poll for input
            var inputPacket = new Packet("input", "Your guess: ");
            _server.SendPacket(_player, inputPacket).GetAwaiter().GetResult();
            
            // Read their answer
            Packet? answerPacket = null;
            while (answerPacket is null)
            {
                answerPacket = _server.ReceivePacket(_player).GetAwaiter().GetResult();
                Thread.Sleep(10);
            }
            
            // Check for graceful disconnect
            if (answerPacket.Command == "bye")
            {
                _server.HandleDisconnectedPlayer(_player);
                playerDisconnectedGracefully = true;
            }
            
            // Check for input
            if (answerPacket.Command == "input")
            {
                var responsePacket = new Packet("message");
                if (int.TryParse(answerPacket.Message, out var guessedNumber))
                {
                    // See if they won
                    if (guessedNumber == answer)
                    {
                        correct = true;
                        responsePacket.Message = "Correct! You win!\n";
                    }
                    else if (guessedNumber < answer)
                        responsePacket.Message = "Too low.\n";
                    else
                        responsePacket.Message = "Too high.\n";
                }
                else
                    responsePacket.Message = "That wasn't a valid number, try again.\n";
                
                // Send the message
                _server.SendPacket(_player, responsePacket).GetAwaiter().GetResult();
            }
            
            // Take a small nap
            Thread.Sleep(10);
            
            // If they aren't correct, keep them here
            running &= !correct;
            
            // Check for disconnect, may have happened gracefully before
            if (!_needToDisconnectClient && !playerDisconnectedGracefully)
                playerConnected &= !global::TcpGamesServer.TcpGamesServer.IsDisconnected(_player);
            else
                playerConnected = false;

            running &= playerConnected;
        }

        // Thank the player and disconnect them
        if (playerConnected)
            _server.DisconnectPlayer(_player, "Thanks for playing \"Guess My Number\" Game!");
        else
            Console.WriteLine("Client disconnected from the game");

        Console.WriteLine($"Ending a {Name} game.");
        
    }
}