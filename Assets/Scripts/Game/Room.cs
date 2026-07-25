using System.Collections.Generic;
using Mirror;

public class Room
{
    public GameManager gameManager;
    public List<NetworkConnectionToClient> players;
    private string password;

    public Room(GameManager gameManager,string password)
    {
        this.gameManager = gameManager;
        this.password = password;
        players = new List<NetworkConnectionToClient>();
    }

    [Server]
    public void AddPlayer(NetworkConnectionToClient player)
    {
        var playerCom = player.identity.GetComponent<Player>();
        playerCom.Setup(this,players.Count);
        players.Add(player);
    }

    [Server]
    public void JoinRoom(NetworkConnectionToClient conn,string password)
    {
        if(this.password != password) return;

        AddPlayer(conn);
    }
}
