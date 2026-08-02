using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class Room
{
    public GameManager gameManager;
    public List<NetworkConnectionToClient> players;
    public List<Player> playerComponents = new();
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
        int id = playerComponents.Count;
        playerCom.Setup(this,id);
        playerCom.gameManager = gameManager;
        player.identity.GetComponent<NetworkMatch>().matchId = matchId;
        players.Add(player);
        playerComponents.Add(playerCom);
        gameManager.AddPlayer();
        gameManager.RefreshLobbyStatus();
    }

[Server]
    public void RemovePlayer(NetworkConnectionToClient player)
    {
        var playerCom = player.identity.GetComponent<Player>();
        players.Remove(player);
        playerComponents.Remove(playerCom);
        player.identity.GetComponent<NetworkMatch>().matchId = Guid.Empty;
        if(players.Count == 0) { DeleteRoom(); return; }
        gameManager.RefreshLobbyStatus();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // デバッグ専用: 実クライアント接続なしでBotプレイヤーを追加する(複数人プレイのテスト用)
    [Server]
    public Player AddBotPlayer(GameObject playerPrefab)
    {
        var obj = UnityEngine.Object.Instantiate(playerPrefab);
        NetworkServer.Spawn(obj);

        var playerCom = obj.GetComponent<Player>();
        int id = playerComponents.Count;
        playerCom.Setup(this, id);
        playerCom.gameManager = gameManager;
        obj.GetComponent<NetworkMatch>().matchId = matchId;

        playerComponents.Add(playerCom);
        gameManager.AddPlayer();
        gameManager.RefreshLobbyStatus();

        return playerCom;
    }
#endif


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
        if(gameManager.inProgress) return;

        AddPlayer(conn);
    }


[Server]
    public bool AllPlayersReady()
    {
        if(playerComponents.Count < 2) return false;
        foreach(var p in playerComponents) if(!p.isReadyToStart) return false;
        return true;
    }
}
