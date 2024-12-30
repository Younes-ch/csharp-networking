using System.Net.Sockets;

namespace Shared;

public interface IGame
{
    #region Properties

    // Name of the game
    string Name { get; }

    // How many players needed to start
    int RequiredPlayers { get; }

    #endregion

    #region Functions

    // Adds a player to the game (should be before it starts)
    bool AddPlayer(TcpClient player);

    // Tells the server to disconnect a player
    void DisconnectPlayer(TcpClient player);

    // The main game loop
    void Run();

    #endregion
}