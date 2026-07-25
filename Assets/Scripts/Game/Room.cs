using System;
using System.Collections.Generic;
using Mirror;

public class Room
{
    public GameManager gameManager;
    public List<NetworkConnectionToClient> players;
    private string password;
    public Guid matchId;
    public string roomId;

    public Room(GameManager gameManager,string password)
    {
        this.gameManager = gameManager;
        this.gameManager.room = this;
        this.password = password;
        players = new List<NetworkConnectionToClient>();
    }

    [Server]
    public void AddPlayer(NetworkConnectionToClient player)
    {
        var playerCom = player.identity.GetComponent<Player>();
        playerCom.Setup(this,players.Count);
        player.identity.GetComponent<NetworkMatch>().matchId = matchId;
        players.Add(player);
    }

    [Server]
    public void RemovePlayer(NetworkConnectionToClient player)
    {
        players.Remove(player);
        if(players.Count == 0) DeleteRoom();
    }

    [Server]
    public void DeleteRoom()
    {
        RoomManager.Instance.roomDict.Remove(roomId);
        RoomManager.Instance.roomNames.Remove(roomId);
        gameManager.DeleteMatch();
        
    }

    [Server]
    public void JoinRoom(NetworkConnectionToClient conn,string password)
    {
        if(this.password != password) return;

        AddPlayer(conn);
    }
}
