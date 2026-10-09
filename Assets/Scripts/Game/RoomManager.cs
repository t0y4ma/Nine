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

    // 部屋IDの最大文字数
    public const int ROOM_ID_MAX = 12;

    public Dictionary<string, RoomInfo> roomDict = new();
    // 部屋ID → 部屋の要約(Room.Summary の形式)。部屋一覧の表示に使う。
    // パスワードそのものは送らない(鍵が付いているかどうかだけ)。
    public readonly SyncDictionary<string, string> roomNames = new();

    // 部屋の人数や状態が変わったら、一覧用の要約を更新する
    [Server]
    public void UpdateRoomSummary(Room room)
    {
        if (room == null || string.IsNullOrEmpty(room.roomId)) return;
        if (!roomNames.ContainsKey(room.roomId)) return;
        string s = room.Summary;
        if (roomNames[room.roomId] != s) roomNames[room.roomId] = s;
    }

    // clientToken: ブラウザごとの識別子。ゲーム中に抜けた人が、自分の席に戻るために使う。
    [Command(requiresAuthority = false)]
    public void CmdJoinRoom(string roomId, string password, string clientToken, NetworkConnectionToClient sender = null)
    {
        roomId = (roomId ?? "" ).Trim();
        if (!roomDict.ContainsKey(roomId))
        {
            TargetNotice(sender, "部屋「" + roomId + "」が見つかりません。");
            return;
        }
        if (string.IsNullOrEmpty(password)) password = "****";
        Debug.Log("Join to the room with id of " + roomId);
        if (!roomDict[roomId].room.JoinRoom(sender, password, clientToken))
            TargetNotice(sender, "パスワードが違います。");
    }

    // 部屋を作り、作った人をそのまま入室させる。
    [Command(requiresAuthority = false)]
    public void CmdCreateRoom(string roomId, string password, string clientToken, NetworkConnectionToClient sender = null)
    {
        roomId = (roomId ?? "").Trim();
        if (roomId.Length == 0 || roomId.Length > ROOM_ID_MAX || roomId.Contains("|"))
        {
            TargetNotice(sender, "部屋IDは1〜" + ROOM_ID_MAX + "文字で入力してください。");
            return;
        }
        if (roomDict.ContainsKey(roomId))
        {
            TargetNotice(sender, "部屋ID「" + roomId + "」はすでに使われています。");
            return;
        }
        if (string.IsNullOrEmpty(password)) password = "****";
        Debug.Log("Create a room with id of " + roomId);

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
        roomNames.Add(roomId, room.Summary);

        if (sender != null) room.JoinRoom(sender, password, clientToken);
    }

    [Command(requiresAuthority = false)]
    public void CmdStartGame(string roomId, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.TryGetValue(roomId, out var info)) return;
        // 開始できるのはホストだけ。ホスト自身が観戦していても開始できる。
        if (sender != info.room.hostConnection) return;
        if (info.room.gameManager.inProgress) return;

        // 参加人数が設定の上限を超えているときは、その場で理由を知らせる
        // (上限は後から下げられるので、超えた状態になり得る)
        if (!info.room.ParticipantsWithinLimit)
        {
            TargetNotice(sender, "参加者が上限を超えています(" + info.room.PresentCount + " / " + info.room.gameManager.MaxPlayers + "人)。上限を上げるか、誰かが観戦に切り替えてください。");
            return;
        }
        if (info.room.PresentCount < 2)
        {
            TargetNotice(sender, "開始には参加者が2人以上必要です(観戦者は数えません)。");
            return;
        }
        if (!info.room.AllPlayersReady())
        {
            TargetNotice(sender, "まだ準備ができていない参加者がいます。");
            return;
        }

        Debug.Log("Start the game in the room with id of " + roomId);
        info.room.gameManager.StartGame();
    }

    [Command(requiresAuthority = false)]
    public void CmdLeaveRoom(string roomId, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.TryGetValue(roomId, out var info)) return;
        // ホストが抜けたら、残っている人に自動で引き継がれる(Room.RemovePlayer)。
        // 誰もいなくなったら部屋ごと消える。
        info.room.RemovePlayer(sender, true);
    }

    // 参加/観戦の切り替え。本人の意思でのみ切り替わる。
    [Command(requiresAuthority = false)]
    public void CmdSetSpectating(string roomId, bool spectate, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.TryGetValue(roomId, out var info)) return;
        string error = info.room.SetSpectating(sender, spectate);
        if (!string.IsNullOrEmpty(error)) TargetNotice(sender, error);
    }

    // ホスト権限の譲渡。譲渡先は部屋にいる人なら誰でもよい(観戦者も含む)。
    [Command(requiresAuthority = false)]
    public void CmdTransferHost(string roomId, uint targetNetId, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.TryGetValue(roomId, out var info)) return;
        if (sender != info.room.hostConnection) return;
        if (!NetworkServer.spawned.TryGetValue(targetNetId, out var identity)) return;
        var target = identity != null ? identity.GetComponent<Player>() : null;
        if (target == null || !info.room.TransferHost(target))
            TargetNotice(sender, "その人にはホストを渡せません。");
    }

    // 本人だけに理由を知らせる(他の人には見せない)
    [TargetRpc]
    private void TargetNotice(NetworkConnection target, string message)
    {
        var uiManager = UIEventsManager.Current;
        uiManager?.ShowNotice(message);
    }

    [Command(requiresAuthority = false)]
    // roundTimeLimit: 選択時間(秒)。0は無制限。
    public void CmdUpdateSettings(string roomId, int cardCount, int scoringMode, int maxPlayers, int roundTimeLimit, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.TryGetValue(roomId, out var info)) return;
        if (sender != info.room.hostConnection) return; // ホストのみ変更可能
        if (info.room.gameManager.inProgress) return; // ゲーム中は変更不可

        info.room.gameManager.UpdateSettings(cardCount, scoringMode, maxPlayers, roundTimeLimit);
    }

    [ClientCallback]
    public override void OnStartClient()
    {
        roomNames.OnChange += OnRoomsChanged;
        var uiManager = UIEventsManager.Current;
        uiManager?.RefreshRoomList();
    }

    [ClientCallback]
    public override void OnStopClient()
    {
        roomNames.OnChange -= OnRoomsChanged;
    }

    [ClientCallback]
    public void OnRoomsChanged(SyncIDictionary<string, string>.Operation op, string key, string item)
    {
        var uiManager = UIEventsManager.Current;
        uiManager?.RefreshRoomList();
    }
}
