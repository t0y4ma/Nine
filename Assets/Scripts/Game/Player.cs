using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

public class Player : NetworkBehaviour
{
    public Room room;   // サーバー側のみ有効(クライアントではnull)

    // 自分がどの部屋にいるかを、本人にだけ伝える。
    // SyncVarにすると全クライアントに配信され、他人の部屋IDまで
    // 読めてしまうため、TargetRpcで所有者だけに送る。
    /*[HideInInspector]*/ public string myRoomId = "";

    // 部屋に戻ってきたとき、どの席の持ち主かを見分けるための識別子。
    // ブラウザごとに1つ作って保存しておき、Join時に送ってもらう。
    // 他人の席を奪えてしまわないよう、サーバー内だけで持つ(SyncVarにしない)。
    [HideInInspector] public string clientToken = "";

    // 時間切れ時の自動提出で使う「選択中のカード」。
    // 確定はしていないが選んではいる状態をサーバーに伝えておき、
    // タイムアウト時にランダムではなくこのカードを出す。
    [HideInInspector] public int pendingSelection = -1;

    [Command]
    public void CmdSetPendingSelection(int cardIndex)
    {
        pendingSelection = cardIndex;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Server]
    public void DebugSetPendingSelection(int cardIndex)
    {
        pendingSelection = cardIndex;
    }
#endif

    // 確定したカードを取り消す。
    // ラウンドが解決する前なら選び直せる。
    [Command]
    public void CmdCancelCard()
    {
        if (gameManager == null) return;
        gameManager.CancelCard(playerId);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Server]
    public void DebugCancelCard()
    {
        if (gameManager == null) return;
        gameManager.CancelCard(playerId);
    }
#endif

    // 退出した本人を部屋選択画面に戻す。
    // inRoomの同期だけでは、ゲーム中に抜けた場合に盤面が残ることがあるため、明示的に切り替える。
    [TargetRpc]
    public void TargetLeftRoom(NetworkConnection target)
    {
        myRoomId = "";
        var uiManager = UIEventsManager.Current;
        uiManager?.ShowRoomSelectAfterLeave();
    }

    [TargetRpc]
    public void TargetSetRoomId(NetworkConnection target, string roomId)
    {
        myRoomId = roomId;
    }

    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName;

    private void OnNameChanged(string oldVal, string newVal)
    {
        var uiManager = UIEventsManager.Current;
        uiManager?.RefreshLobbyPanels();
        uiManager?.RefreshBoardViews();
    }

    public const int NAME_MAX = 12;

    // 表示名を設定する。タグ記号は表示を壊すので取り除き、長さも制限する。
    [Command]
    public void CmdSetName(string name)
    {
        playerName = SanitizeName(name);
    }

    public static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        name = name.Replace("<", "").Replace(">", "").Replace("\n", "").Replace("\r", "").Trim();
        if (name.Length > NAME_MAX) name = name.Substring(0, NAME_MAX);
        return name;
    }
    [SyncVar]
    public int playerId;
    [SyncVar]
    public GameManager gameManager;

    public readonly SyncList<int> cards = new();
    public readonly SyncList<bool> used = new();

    [SyncVar(hook = nameof(OnIsReadytoTurnChanged))]
    public bool isReadytoTurn;

    [SyncVar(hook = nameof(OnInRoomChanged))]
    public bool inRoom;

    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReadyToStart;

    [SyncVar]
    public bool isReadyForNextRound;

    [SyncVar(hook = nameof(OnIsRoomHostChanged))]
    public bool isRoomHost;

    // 観戦しているか。trueなら席を持たず、ゲームには参加しない。
    // 人数の数え方(参加人数・一覧の表示数)や画面の出し分けは、この値に従う。
    [SyncVar(hook = nameof(OnIsSpectatorChanged))]
    public bool isSpectator;

    private void OnIsSpectatorChanged(bool oldVal, bool newVal)
    {
        var uiManager = UIEventsManager.Current;
        uiManager?.RefreshLobbyPanels();
        uiManager?.RefreshBoardViews();
    }

    public int GetPlayerId()
    {
        return playerId;
    }

    public void Setup(Room room, int playerId)
    {
        // 部屋IDは本人にだけ通知する(他人には見せない)
        if (room != null && connectionToClient != null)
        {
            myRoomId = room.roomId;   // サーバー側にも保持
            TargetSetRoomId(connectionToClient, room.roomId);
        }
        this.room = room;
        this.playerId = playerId;
        isSpectator = false;
        int cardCnt = room.gameManager.CARDCOUNT;
        // 前に入っていた部屋の手札が残っていると、Addで後ろに継ぎ足されて枚数が増えてしまう。
        // 毎回作り直す。
        cards.Clear();
        used.Clear();
        for (int i = 1; i <= cardCnt; i++) { cards.Add(i); used.Add(false); }
        inRoom = true;
    }

    // 観戦者として部屋に入る。手札は持たない。
    [Server]
    public void SetupSpectator(Room room)
    {
        if (room != null && connectionToClient != null)
        {
            myRoomId = room.roomId;
            TargetSetRoomId(connectionToClient, room.roomId);
        }
        this.room = room;
        cards.Clear();
        used.Clear();
        inRoom = true;
    }

    [Command]
    public void CmdSetReady(bool ready)
    {
        if (room == null || isSpectator) return;
        if (gameManager != null && gameManager.inProgress) return;
        isReadyToStart = ready;
        if (gameManager != null) gameManager.RefreshLobbyStatus();
    }

    private void OnInRoomChanged(bool oldVal, bool newVal)
    {
        if (!isOwned) return;
        var uiManager = UIEventsManager.Current;
        uiManager?.RefreshLobbyPanels();
    }

    private void OnIsRoomHostChanged(bool oldVal, bool newVal)
    {
        if (!isOwned) return;
        var uiManager = UIEventsManager.Current;
        uiManager?.RefreshLobbyPanels();
    }

    private void OnReadyChanged(bool oldVal, bool newVal)
    {
        var uiManager = UIEventsManager.Current;
        uiManager?.RefreshLobbyPanels();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // usedはSyncListなので、変更を購読しないとUIに反映されない。
        // (これが無かったため、自分がカードを出しても手札がグレーにならず、
        //  他プレイヤーの更新で盤面が再描画されたときに初めて反映されていた)
        used.Callback += OnUsedChanged;
    }

    public override void OnStopClient()
    {
        used.Callback -= OnUsedChanged;
        base.OnStopClient();
    }

    private void OnUsedChanged(SyncList<bool>.Operation op, int index, bool oldItem, bool newItem)
    {
        var uiManager = UIEventsManager.Current;
        uiManager?.RefreshBoardViews();
    }

    private void OnIsReadytoTurnChanged(bool oldVal, bool newVal)
    {
        // 提出状態が変わったので、盤面の表示を更新する。
        // 以前は廃止済みのRoundResultPanelだけを更新しており、
        // 手札のグレーアウトや「今出したカード」に反映されていなかった。
        var uiManager = UIEventsManager.Current;
        uiManager?.RefreshBoardViews();
    }

    [Command]
    public void CmdUseCard(int cardindex)
    {
        if (cardindex < 0 || cardindex >= cards.Count) return;
        if (used[cardindex]) return;
        if (isReadytoTurn) return;

        int id = GetPlayerId();
        if (room.gameManager.UseCard(id, cardindex))
        {
            used[cardindex] = true;
        }
    }

    [Command]
    public void CmdReadyForNextRound()
    {
        if (gameManager == null) return;
        isReadyForNextRound = true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // デバッグ専用: Command(オーナー権限)を経由せず、サーバー上で直接どのプレイヤーとしても操作できる
    [Server]
    public bool DebugUseCard(int cardindex)
    {
        if (cardindex < 0 || cardindex >= cards.Count) return false;
        if (used[cardindex]) return false;
        if (isReadytoTurn) return false;

        int id = GetPlayerId();
        if (room.gameManager.UseCard(id, cardindex))
        {
            used[cardindex] = true;
            return true;
        }
        return false;
    }

    // デバッグ専用: オーナー権限のないBotプレイヤーのReady状態を直接設定する
    [Server]
    public void DebugSetReady(bool ready)
    {
        if (room == null) return;
        if (gameManager != null && gameManager.inProgress) return;
        isReadyToStart = ready;
        if (gameManager != null) gameManager.RefreshLobbyStatus();
    }

    // デバッグ専用: オーナー権限のないBotプレイヤーの「次へ」準備状態を直接設定する
    [Server]
    public void DebugReadyForNextRound()
    {
        isReadyForNextRound = true;
    }
#endif
}
