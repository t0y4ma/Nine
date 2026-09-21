using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

public class RoomManager : NetworkBehaviour
{
    #region singleton
    public static RoomManager Instance { get; private set; }

    [ServerCallback]
    public override void OnStartServer()
    {
        if (Instance == null) Instance = this;
    }

    [ServerCallback]
    public override void OnStopServer()
    {
        if (Instance == this) Instance = null;
    }
    #endregion

    [SerializeField] private GameObject GM;

    public Dictionary<string, RoomInfo> roomDict = new();
    public readonly SyncDictionary<string, string> roomNames = new();

    // clientToken: ブラウザごとの識別子。ゲーム中に抜けた人が、自分の席に戻るために使う。
    [Command(requiresAuthority = false)]
    public void CmdJoinRoom(string roomId, string password, string clientToken, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.ContainsKey(roomId)) return;
        if (password == "") password = "****";
        Debug.Log("Join to the room with id of " + roomId);
        roomDict[roomId].room.JoinRoom(sender, password, clientToken);
    }

    [Command(requiresAuthority = false)]
    public void CmdCreateRoom(string roomId, string password, NetworkConnectionToClient sender = null)
    {
        if (roomDict.ContainsKey(roomId)) return;
        if (password == "") password = "****";
        Debug.Log("Create a room with id of " + roomId + ", password of " + password);

        var gm = Instantiate(GM);

        Room room = new Room(gm.GetComponent<GameManager>(), password);
        room.matchId = Guid.NewGuid();
        room.roomId = roomId;
        room.hostConnection = sender; // このコマンドを送ってきたクライアントがホスト権限を持つ

        // matchIdはSpawnより前に設定する。
        // NetworkMatchはインタレスト管理なので、Spawn時点のmatchIdで配信先が決まる。
        // 後から設定しても、その時点で観測対象外だったクライアントには
        // オブジェクトが送られず、OnStartClientも呼ばれない
        // (=SyncListのCallbackが登録されず履歴やゲーム結果が届かない)。
        gm.GetComponent<NetworkMatch>().matchId = room.matchId;
        NetworkServer.Spawn(gm);

        RoomInfo roomInfo = new RoomInfo();
        roomInfo.name = roomId;
        roomInfo.password = password;
        roomInfo.room = room;
        roomDict[roomId] = roomInfo;
        roomNames.Add(roomId, roomId);
    }

    [Command(requiresAuthority = false)]
    public void CmdStartGame(string roomId, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.TryGetValue(roomId, out var info)) return;
        if (sender != info.room.hostConnection) return; // 部屋を作成したクライアントのみ開始可能(サーバー自体には権限がない)
        if (!info.room.AllPlayersReady()) return; // 全員準備完了するまで開始不可

        Debug.Log("Start the game in the room with id of " + roomId);
        info.room.gameManager.StartGame();
    }

[Command(requiresAuthority = false)]
    public void CmdLeaveRoom(string roomId, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.TryGetValue(roomId, out var info)) return;
        // 退出者がホストだった場合、部屋を作成したクライアントが誰もいなくなるが、
        // Room.RemovePlayerは残り0人になった時点で自動的に部屋自体を削除するため、
        // 残ったメンバーがいる場合の「ホスト権限の引き継ぎ」は別途今後の課題とする。
        // (ゲーム中に抜けたホストが戻ってきた場合は、ホスト権限も戻る)
        info.room.RemovePlayer(sender, true);
    }

    [Command(requiresAuthority = false)]
    public void CmdUpdateSettings(string roomId, int cardCount, int scoringMode, int maxPlayers, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.TryGetValue(roomId, out var info)) return;
        if (sender != info.room.hostConnection) return; // ホストのみ変更可能
        if (info.room.gameManager.inProgress) return; // ゲーム中は変更不可

        info.room.gameManager.UpdateSettings(cardCount, scoringMode, maxPlayers);
    }

    [ClientCallback]
    public override void OnStartClient()
    {
        roomNames.OnChange += OnRoomsChanged;
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.RefreshRoomList();
    }

    [ClientCallback]
    public void OnRoomsChanged(SyncIDictionary<string, string>.Operation op, string key, string item)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.RefreshRoomList();
    }
}
