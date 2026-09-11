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

    // この部屋を作成したクライアントの接続。Start権限はサーバー側ではなくこの接続に紐付く。
    public NetworkConnectionToClient hostConnection;

    public Room(GameManager gameManager, string password)
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
        playerCom.Setup(this, id);
        playerCom.gameManager = gameManager;
        playerCom.isRoomHost = (player == hostConnection);
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

        // 以前はここでplayerComのSyncVar(inRoom等)がリセットされておらず、切断以外の経路
        // (例: Leaveボタン)でプレイヤーを退出させると、クライアント側UIがロビー画面に
        // 戻らないままになってしまっていた。明示的にリセットする。
        if (playerCom != null)
        {
            playerCom.inRoom = false;
            playerCom.isRoomHost = false;
            playerCom.isReadyToStart = false;
            playerCom.room = null;
            playerCom.gameManager = null;
        }

        // 残ったプレイヤーのIDを詰め直す(playerComponentsのindexとplayerIdが常に一致する前提の設計のため)
        for (int i = 0; i < playerComponents.Count; i++)
        {
            playerComponents[i].playerId = i;
        }

        if (players.Count == 0) { DeleteRoom(); return; }
        gameManager.RefreshLobbyStatus();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // デバッグ専用: 実クライアント接続なしでBotプレイヤーを追加する(複数人プレイのテスト用)
    [Server]
    public Player AddBotPlayer(GameObject playerPrefab)
    {
        var obj = UnityEngine.Object.Instantiate(playerPrefab);

        var playerCom = obj.GetComponent<Player>();
        int id = playerComponents.Count;
        playerCom.Setup(this, id);
        playerCom.gameManager = gameManager;
        // matchIdはSpawnより前に設定する(NetworkMatchはインタレスト管理のため、
        // Spawn時点のmatchIdで配信先が決まる)
        obj.GetComponent<NetworkMatch>().matchId = matchId;

        NetworkServer.Spawn(obj);

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
    public void JoinRoom(NetworkConnectionToClient conn, string password)
    {
        if (this.password != password) return;
        if (gameManager.inProgress) return;
        if (playerComponents.Count >= gameManager.MaxPlayers) return; // 参加人数上限に達している

        AddPlayer(conn);
    }

    [Server]
    public bool AllPlayersReady()
    {
        if (playerComponents.Count < 2) return false;
        foreach (var p in playerComponents) if (!p.isReadyToStart) return false;
        return true;
    }
}
