using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Nine.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 画面全体(タイトル・部屋一覧・ルーム・対局・結果・履歴)を受け持つ。
/// 通信側(GameManager / Player / RoomManager)からはここの公開メソッドが呼ばれる。
/// 呼ばれた時点では「描き直しが必要」という印を付けるだけにして、実際の描画は LateUpdate でまとめて1回行う。
/// (Rpc と SyncVar / SyncList の到着順は保証されないため、届いた順に部分的に描くと表示が食い違う)
/// 接続前から動けるよう、常にアクティブな UI オブジェクトに置く(ネットワークのオブジェクトではない)。
/// 画面はすべてコードで組み立てる(Build / Title / Room / Game / Overlays の各 partial)。
/// </summary>
public partial class UIEventsManager : MonoBehaviour
{
    [SerializeField] private RoomManager roomManager;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private GameObject debugPlayerPrefab;

    static UIEventsManager _current;
    public static UIEventsManager Current
    {
        get
        {
            if (_current == null) _current = FindFirstObjectByType<UIEventsManager>(FindObjectsInactive.Include);
            return _current;
        }
    }

    RoomManager RM
    {
        get
        {
            if (roomManager == null) roomManager = FindFirstObjectByType<RoomManager>(FindObjectsInactive.Include);
            return roomManager;
        }
    }

    // ================= 描画の印 =================
    bool dirty = true, layoutDirty = true, roomListDirty = true, historyDirty = true;
    Vector2 lastCanvasSize;
    bool lastConnected;
    void MarkDirty() { dirty = true; }

    // ================= 状態 =================
    enum View { Title, Room, Game }
    View view = View.Title;
    bool wasInGame;
    Player debugTargetPlayer;

    int selectedIdx = -1;          // 手札で選んでいるカード(0始まり)
    bool roundResolved;            // このラウンドの結果が出たか
    float transition = -1f;        // 次のラウンドまでの残り(1→0)。-1は遷移中でない
    readonly HashSet<Player> nextPressedBy = new HashSet<Player>();

    // Rpc の引数で届いた値。SyncList より優先する(到着順が保証されないため)
    int[] revealedOverride, scoresOverride, spectatorPicks;

    // 得点カード
    int pointCard; int[] pointCarried = new int[0]; int pointLeft; bool pointKnown;

    // このラウンドの勝者(結果発表から次のラウンド開始まで場に金色で示す)
    int[] lastWinners; string lastWinnerLabel = "";

    // 残り時間バー
    bool timerVisible; float timerTarget = 1f, timerShown = 1f;

    // ラウンド結果
    bool resultOpen; float resultUntil;
    int[] resultPicks = new int[0], resultWinners = new int[0];
    string resultLabel = "", resultWinnerLabel = ""; int resultRound, resultTotal;

    // ゲーム結果
    bool gameOverOpen; int[] goScores = new int[0], goWinners = new int[0];

    bool historyOpen, leaveConfirmOpen;

    // 通知
    string noticeText = ""; float noticeUntil;
    string connectError = "";

    // カットイン
    string cutInMain = "", cutInSub = ""; float cutInStart = -10f;
    const float CUT_IN_SECONDS = 1.6f;

    // ルール設定の手元の値(送信してから同期が届くまでの間、表示が古い値に戻らないように)
    int shadowCards = -1, shadowMode = -1, shadowMax = -1, shadowTime = -999; float shadowUntil;
    float timeSliderSendAt = -1f;
    bool suppressSlider;

    // 名前
    const string NAME_KEY = "Nine.PlayerName";
    Player nameSentTo;

    // 数字キーでのカード選択(2桁を溜める)
    string numberBuffer = ""; float numberBufferUntil;
    const float NUMBER_INPUT_WINDOW = 0.8f;

    // 部屋のメンバー(描画のたびに集め直す)
    readonly List<Player> members = new List<Player>();
    Player[] seatPlayers = new Player[0];

    // ================= Unity =================
    void Awake()
    {
        _current = this;
        NineUi.Font = font != null ? font : TMP_Settings.defaultFontAsset;
        BuildUi();
    }

    void OnDestroy()
    {
        if (_current == this) _current = null;
    }

    void Update()
    {
        bool connected = NetworkClient.isConnected;
        if (connected != lastConnected)
        {
            lastConnected = connected;
            if (connected) connectError = "";
            roomListDirty = true;
            layoutDirty = true;
            MarkDirty();
        }

        // 名前は接続して自分のPlayerができたら送る(名前を変えたら送り直す)
        var local = LocalPlayer;
        if (local != null && local.isOwned && nameSentTo != local)
        {
            nameSentTo = local;
            local.CmdSetName(PlayerName);
        }

        // 見ている席が部屋を抜けたら自分の視点に戻す
        if (debugTargetPlayer != null && !debugTargetPlayer.inRoom) { debugTargetPlayer = null; MarkDirty(); }
        if (debugTargetPlayer == null && !ReferenceEquals(debugTargetPlayer, null)) { debugTargetPlayer = null; MarkDirty(); }

        // フックで印は付くが、取りこぼしても追いつくように少しずつ描き直す
        if (Time.frameCount % 20 == 0) MarkDirty();

        HandleShortcuts();
        UpdateTimeSliderSend();
        AnimateOverlays();
    }

    void LateUpdate()
    {
        UpdateScalerOrientation();
        var size = canvasRt.rect.size;
        if (size.x < 10 || size.y < 10) return;   // まだ画面の大きさが決まっていない(WebGLの最初のフレームなど)
        if ((size - lastCanvasSize).sqrMagnitude > 4f)
        {
            lastCanvasSize = size;
            layoutDirty = true;
            historyDirty = true;
            dirty = true;
        }
        if (!dirty) return;
        dirty = false;
        Render();
        layoutDirty = false;
    }

    // ================= 通信側から呼ばれる窓口 =================
    public void RefreshLobbyPanels() { MarkDirty(); }
    public void RefreshBoardViews() { MarkDirty(); }
    public void RequestFullLayoutRebuild() { layoutDirty = true; MarkDirty(); }
    public void RefreshRoomList() { roomListDirty = true; MarkDirty(); }
    public void RefreshMyCardView(List<bool> used, int cnt) { MarkDirty(); }
    public void RefreshAllCardView(List<bool> usedAll, int cnt) { MarkDirty(); }
    public void UpdateLobbyStatus(int readyCount, int totalCount) { MarkDirty(); }

    public void SetRevealedPicksOverride(int[] picks) { revealedOverride = picks; MarkDirty(); }
    public void SetScoresOverride(int[] scores) { scoresOverride = scores; MarkDirty(); }

    // 観戦者だけに届く、各席が今のラウンドで確定したカード(1始まり、0=未確定)
    public void ShowSpectatorPicks(int[] picks) { spectatorPicks = picks; MarkDirty(); }

    public void ShowPointCard(int card, int[] carriedCards, int cardsLeft)
    {
        pointCard = card;
        pointCarried = carriedCards ?? new int[0];
        pointLeft = cardsLeft;
        pointKnown = card != 0;
        layoutDirty = true;
        MarkDirty();
    }

    // 得点カードの SyncVar が届いたとき(ラウンド開始の Rpc より先に画面ができていなかった場合の追いつき)
    public void RefreshPointCardFromGame(GameManager gm)
    {
        if (gm == null || !gm.inProgress || gm.ScoringMode != GameManager.SCORING_POINT_CARDS || gm.currentPointCard == 0) return;
        var local = LocalPlayer;
        if (local != null && local.gameManager != gm) return;
        ShowPointCard(gm.currentPointCard, gm.carriedPointCards.ToArray(), gm.pointCardsLeft);
    }

    public void ShowRoundCutIn(int roundNumber, int totalRounds, bool pointMode, int pointCardValue)
    {
        roundResolved = false;
        transition = -1f;
        selectedIdx = -1;
        lastWinners = null;
        lastWinnerLabel = "";
        nextPressedBy.Clear();
        resultOpen = false;
        cutInMain = "第 " + roundNumber + " 戦";
        cutInSub = roundNumber >= totalRounds ? "最終戦" : "のこり " + (totalRounds - roundNumber) + " 戦";
        if (pointMode) cutInSub = "得点カード  " + GameManager.FormatPoints(pointCardValue) + "   ・   " + cutInSub;
        cutInStart = Time.unscaledTime;
        layoutDirty = true;
        MarkDirty();
    }

    public void HideRoundResultPopup() { resultOpen = false; MarkDirty(); }

    // 互換用。以前はラウンドの結果文をここに出していた(今は結果ポップアップと上部バーに出す)
    public void ShowResult(string message) { MarkDirty(); }

    public void UpdateRoundTimerBar(float remainingFraction)
    {
        if (remainingFraction < 0f) { timerVisible = false; return; }
        timerVisible = true;
        timerTarget = Mathf.Clamp01(remainingFraction);
        if (Mathf.Abs(timerShown - timerTarget) > 0.3f) timerShown = timerTarget;
    }

    public void UpdateTransitionBar(float remainingFraction)
    {
        if (remainingFraction > 0f)
        {
            roundResolved = true;
            transition = remainingFraction;
        }
        else
        {
            transition = -1f;
            nextPressedBy.Clear();
        }
        MarkDirty();
    }

    public void ShowRoundResult(int roundNumber, int totalRounds, int[] picks, int[] winners, string label, string winnerLabel, float seconds)
    {
        roundResolved = true;
        resultRound = roundNumber;
        resultTotal = totalRounds;
        resultPicks = picks ?? new int[0];
        resultWinners = winners ?? new int[0];
        resultLabel = label ?? "";
        resultWinnerLabel = winnerLabel ?? "";
        lastWinners = resultWinners;
        lastWinnerLabel = WinnerTag(resultWinnerLabel);
        revealedOverride = resultPicks;
        resultOpen = true;
        resultUntil = Time.unscaledTime + seconds;
        layoutDirty = true;
        MarkDirty();
    }

    public void ShowGameOver(int[] finalScores, int[] winnerIds)
    {
        goScores = finalScores ?? new int[0];
        goWinners = winnerIds ?? new int[0];
        scoresOverride = goScores;
        gameOverOpen = true;
        resultOpen = false;
        timerVisible = false;
        transition = -1f;
        layoutDirty = true;
        MarkDirty();
    }

    public bool IsHistoryPanelOpen() { return historyOpen; }
    public void RebuildHistoryPanel() { historyDirty = true; MarkDirty(); }
    public void AppendHistoryLine(int roundNumber, List<int> playedCards, List<int> winners, string label) { historyDirty = true; MarkDirty(); }
    public void ClearHistoryPanel() { historyDirty = true; MarkDirty(); }

    // 自分が部屋から抜けたとき(サーバーから本人にだけ届く)
    public void ShowRoomSelectAfterLeave()
    {
        debugTargetPlayer = null;
        ResetGameState();
        gameOverOpen = false;
        historyOpen = false;
        leaveConfirmOpen = false;
        roomListDirty = true;
        layoutDirty = true;
        MarkDirty();
    }

    // サーバーからの理由の通知(本人にだけ届く)
    public void ShowNotice(string message)
    {
        noticeText = message ?? "";
        noticeUntil = Time.unscaledTime + 4.5f;
        layoutDirty = true;
        MarkDirty();
    }

    // 接続に失敗したとき(myNetworkManager から)
    public void ShowConnectError(string message)
    {
        connectError = message ?? "";
        MarkDirty();
    }

    void ResetGameState()
    {
        revealedOverride = null;
        scoresOverride = null;
        spectatorPicks = null;
        selectedIdx = -1;
        roundResolved = false;
        transition = -1f;
        nextPressedBy.Clear();
        lastWinners = null;
        lastWinnerLabel = "";
        resultOpen = false;
        timerVisible = false;
        pointKnown = false;
        pointCarried = new int[0];
        historyDirty = true;
    }

    // ================= 便利な取得 =================
    static Player LocalPlayer
    {
        get
        {
            var c = NetworkClient.connection;
            if (c == null || c.identity == null) return null;
            return c.identity.GetComponent<Player>();
        }
    }

    public Player GetDebugOrLocalPlayer()
    {
        if (debugTargetPlayer != null) return debugTargetPlayer;
        return LocalPlayer;
    }

    static int SeatCount(GameManager gm)
    {
        if (gm == null) return 0;
        int n = gm.roundWins.Count;
        if (gm.CARDCOUNT > 0) n = Mathf.Max(n, gm.used_Players.Count / gm.CARDCOUNT);
        return n;
    }

    void CollectMembers(GameManager gm)
    {
        members.Clear();
        int n = SeatCount(gm);
        if (seatPlayers.Length != n) seatPlayers = new Player[n];
        else System.Array.Clear(seatPlayers, 0, n);
        if (gm == null) return;
        foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p == null || !p.inRoom || p.gameManager != gm) continue;
            members.Add(p);
            if (!p.isSpectator && p.playerId >= 0 && p.playerId < n) seatPlayers[p.playerId] = p;
        }
        members.Sort((a, b) =>
        {
            int sa = a.isSpectator ? 100 : a.playerId, sb = b.isSpectator ? 100 : b.playerId;
            if (sa != sb) return sa.CompareTo(sb);
            return a.netId.CompareTo(b.netId);
        });
    }

    Player SeatPlayer(int seat) { return seat >= 0 && seat < seatPlayers.Length ? seatPlayers[seat] : null; }

    public static string NameOf(Player p, int seat)
    {
        if (p != null && !string.IsNullOrEmpty(p.playerName)) return p.playerName;
        return seat >= 0 ? "プレイヤー" + (seat + 1) : "観戦者";
    }

    static string SeatTag(int seat) { return "P" + (seat + 1); }

    int ScoreOf(GameManager gm, int seat)
    {
        if (scoresOverride != null && seat < scoresOverride.Length) return scoresOverride[seat];
        if (gm != null && seat < gm.roundWins.Count) return gm.roundWins[seat];
        return 0;
    }

    int RevealedOf(GameManager gm, int seat)
    {
        if (revealedOverride != null) return seat < revealedOverride.Length ? revealedOverride[seat] : 0;
        if (gm != null && seat < gm.lastRevealedPicks.Count) return gm.lastRevealedPicks[seat];
        return 0;
    }

    static bool Contains(int[] arr, int v)
    {
        if (arr == null) return false;
        for (int i = 0; i < arr.Length; i++) if (arr[i] == v) return true;
        return false;
    }

    // ラウンドの結果ラベル(サーバーの記録の形式)を日本語の表示にする
    public static string ResultLabelJa(string label)
    {
        if (string.IsNullOrEmpty(label)) return "";
        if (label == "Tie") return "引き分け";
        if (label.StartsWith("Carry ")) return "持ち越し " + PtJa(label.Substring(6));
        if (label.StartsWith("Lost ")) return "流れ " + PtJa(label.Substring(5));
        return PtJa(label);
    }

    static string PtJa(string s) { return s.EndsWith("pt") ? s.Substring(0, s.Length - 2) + "点" : s; }

    static string WinnerTag(string winnerLabel)
    {
        if (string.IsNullOrEmpty(winnerLabel) || winnerLabel == "WIN") return "勝ち";
        return PtJa(winnerLabel);
    }

    static string ModeName(int mode)
    {
        switch (mode)
        {
            case GameManager.SCORING_FIXED: return "1勝1点";
            case GameManager.SCORING_POINT_CARDS: return "得点カード";
            default: return "数字の合計";
        }
    }

    static string ModeDescription(int mode)
    {
        switch (mode)
        {
            case GameManager.SCORING_FIXED:
                return "一番大きい数字を出した人が1点。全員が同じ数字なら引き分け。";
            case GameManager.SCORING_POINT_CARDS:
                return "毎回めくる得点カードを、ほかの人とかぶらずに一番大きい数字(得点がマイナスなら一番小さい数字)を出した人が取る。全員かぶったら次へ持ち越し。";
            default:
                return "一番大きい数字を出した人が、ほかの人が出した数字の合計を得点にする。";
        }
    }

    static string TimeLabel(int seconds)
    {
        if (seconds <= 0) return "無制限";
        if (seconds < 60) return seconds + "秒";
        int m = seconds / 60, s = seconds % 60;
        return s == 0 ? m + "分" : m + "分" + s + "秒";
    }

    // ================= 名前・識別子 =================
    static string PlayerName
    {
        get { return PlayerPrefs.GetString(NAME_KEY, ""); }
    }

    void SaveName(string name)
    {
        name = Player.SanitizeName(name);
        if (name == PlayerName) return;
        PlayerPrefs.SetString(NAME_KEY, name);
        PlayerPrefs.Save();
        nameSentTo = null;   // 次のUpdateで送り直す
        MarkDirty();
    }

    // このブラウザ(端末)の識別子。ゲーム中に抜けた後、同じ部屋に入り直すとこの値で元の席に戻れる。
    const string CLIENT_TOKEN_KEY = "Nine.ClientToken";
    public static string GetClientToken()
    {
        string token = PlayerPrefs.GetString(CLIENT_TOKEN_KEY, "");
        if (string.IsNullOrEmpty(token))
        {
            token = System.Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(CLIENT_TOKEN_KEY, token);
            PlayerPrefs.Save();
        }
        return token;
    }

    // 履歴の1行: ""round|c0,c1,..|winnerId|tie|resultLabel|w0,w1,..""
    public static bool TryParseHistoryEntry(string entry, out int roundNumber,
        out List<int> cards, out List<int> winners, out string resultLabel)
    {
        roundNumber = 0;
        cards = new List<int>();
        winners = new List<int>();
        resultLabel = "";
        if (string.IsNullOrEmpty(entry)) return false;

        var parts = entry.Split('|');
        if (parts.Length < 5) return false;

        int.TryParse(parts[0], out roundNumber);
        if (!string.IsNullOrEmpty(parts[1]))
            foreach (var t in parts[1].Split(','))
            {
                int v;
                cards.Add(int.TryParse(t, out v) ? v : 0);
            }
        resultLabel = parts[4];

        if (parts.Length >= 6)
        {
            if (!string.IsNullOrEmpty(parts[5]))
                foreach (var t in parts[5].Split(','))
                {
                    int w;
                    if (int.TryParse(t, out w)) winners.Add(w);
                }
        }
        else
        {
            int best = 0;
            foreach (var v in cards) if (v > best) best = v;
            for (int i = 0; i < cards.Count; i++) if (best > 0 && cards[i] == best) winners.Add(i);
        }
        return true;
    }

    // ================= 操作 =================
    static bool IsHostingSupported() { return Application.platform != RuntimePlatform.WebGLPlayer; }

    static bool IsServerModeAllowed()
    {
        if (!IsHostingSupported()) return false;
#if UNITY_SERVER
        return true;
#else
        foreach (var arg in System.Environment.GetCommandLineArgs()) if (arg == "-server") return true;
        return false;
#endif
    }

    public void ButtonHost()
    {
        if (!IsHostingSupported() || NetworkClient.active || NetworkServer.active) return;
        SaveName(title.name.text);
        NetworkManager.singleton.StartHost();
    }

    public void ButtonConnect()
    {
        if (NetworkClient.active) return;
        SaveName(title.name.text);
        string addr = title.address.text.Trim();
        NetworkManager.singleton.networkAddress = string.IsNullOrEmpty(addr) ? "localhost" : addr;
        connectError = "";
        NetworkManager.singleton.StartClient();
        MarkDirty();
    }

    public void ButtonServer()
    {
        if (!IsServerModeAllowed() || NetworkServer.active) return;
        NetworkManager.singleton.StartServer();
    }

    public void ButtonCreateRoom()
    {
        SaveName(title.name.text);
        string id = title.roomId.text.Trim();
        // 部屋IDが空なら、使われていない4桁の番号を自動で入れる
        if (id.Length == 0)
        {
            do { id = Random.Range(1000, 10000).ToString(); }
            while (RM != null && RM.roomNames.ContainsKey(id));
            title.roomId.text = id;
        }
        if (!NetworkClient.isConnected && !NetworkServer.active)
        {
            if (!IsHostingSupported()) { ShowNotice("先にサーバーへ接続してください。"); return; }
            // サーバーがなければ自分がホストになってから作る
            StartCoroutine(AutoHostThenCreateRoom(id));
            return;
        }
        if (RM != null) RM.CmdCreateRoom(id, title.password.text, GetClientToken());
    }

    IEnumerator AutoHostThenCreateRoom(string id)
    {
        NetworkManager.singleton.StartHost();
        float limit = Time.unscaledTime + 10f;
        while (!NetworkClient.ready && Time.unscaledTime < limit) yield return null;
        if (!NetworkClient.ready) { ShowNotice("ホストを開始できませんでした。"); yield break; }
        if (RM != null) RM.CmdCreateRoom(id, title.password.text, GetClientToken());
    }

    public void ButtonJoinRoom() { JoinRoom(null); }

    void JoinRoom(string id)
    {
        SaveName(title.name.text);
        if (id == null) id = title.roomId.text.Trim();
        if (id.Length == 0) { ShowNotice("部屋IDを入力するか、一覧から部屋を選んでください。"); return; }
        if (!NetworkClient.isConnected) { ShowNotice("サーバーに接続していません。"); return; }
        if (RM != null) RM.CmdJoinRoom(id, title.password.text, GetClientToken());
    }

    public void ButtonReady()
    {
        var v = GetDebugOrLocalPlayer();
        if (v == null || !v.inRoom || v.isSpectator) return;
        if (v.gameManager != null && v.gameManager.inProgress) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugTargetPlayer != null)
        {
            if (NetworkServer.active) debugTargetPlayer.DebugSetReady(!debugTargetPlayer.isReadyToStart);
            MarkDirty();
            return;
        }
#endif
        v.CmdSetReady(!v.isReadyToStart);
    }

    public void ButtonStartGame()
    {
        var local = LocalPlayer;
        if (local == null || !local.inRoom || !local.isRoomHost || RM == null) return;
        if (local.gameManager != null && local.gameManager.inProgress) return;
        RM.CmdStartGame(local.myRoomId);
    }

    public void ButtonToggleSpectate()
    {
        var local = LocalPlayer;
        if (local == null || !local.inRoom || RM == null) return;
        if (local.gameManager != null && local.gameManager.inProgress) return;
        RM.CmdSetSpectating(local.myRoomId, !local.isSpectator);
    }

    void TransferHostTo(Player target)
    {
        var local = LocalPlayer;
        if (local == null || !local.isRoomHost || target == null || RM == null) return;
        RM.CmdTransferHost(local.myRoomId, target.netId);
    }

    public void ButtonLeaveRoom()
    {
        var local = LocalPlayer;
        if (local == null || !local.inRoom) return;
        var gm = local.gameManager;
        // 対局中に抜けるときは確認する(抜けた後はランダムに出される)
        if (gm != null && gm.inProgress && !local.isSpectator && !leaveConfirmOpen)
        {
            leaveConfirmOpen = true;
            layoutDirty = true;
            MarkDirty();
            return;
        }
        DoLeave();
    }

    void DoLeave()
    {
        leaveConfirmOpen = false;
        var local = LocalPlayer;
        if (local == null || RM == null) return;
        debugTargetPlayer = null;
        RM.CmdLeaveRoom(local.myRoomId);
        MarkDirty();
    }

    void SendPending(Player v, int idx)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugTargetPlayer != null) { if (NetworkServer.active) v.DebugSetPendingSelection(idx); return; }
#endif
        v.CmdSetPendingSelection(idx);
    }

    public void SelectCard(int idx)
    {
        var v = GetDebugOrLocalPlayer();
        if (v == null || v.isSpectator || v.gameManager == null || !v.gameManager.inProgress) return;
        if (roundResolved || v.isReadytoTurn) return;
        if (idx < 0 || idx >= v.used.Count || v.used[idx]) return;
        selectedIdx = selectedIdx == idx ? -1 : idx;
        SendPending(v, selectedIdx);
        MarkDirty();
    }

    public void ButtonConfirmCard()
    {
        var v = GetDebugOrLocalPlayer();
        if (v == null || v.isSpectator || v.gameManager == null || !v.gameManager.inProgress || roundResolved) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugTargetPlayer != null)
        {
            if (!NetworkServer.active) return;
            if (v.isReadytoTurn) v.DebugCancelCard();
            else if (selectedIdx >= 0) v.DebugUseCard(selectedIdx);
            MarkDirty();
            return;
        }
#endif
        if (v.isReadytoTurn) v.CmdCancelCard();
        else if (selectedIdx >= 0 && selectedIdx < v.used.Count && !v.used[selectedIdx]) v.CmdUseCard(selectedIdx);
        MarkDirty();
    }

    public void ButtonNextRound()
    {
        var v = GetDebugOrLocalPlayer();
        if (v == null || v.isSpectator || transition <= 0f) return;
        nextPressedBy.Add(v);
        resultOpen = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugTargetPlayer != null) { if (NetworkServer.active) v.DebugReadyForNextRound(); MarkDirty(); return; }
#endif
        v.CmdReadyForNextRound();
        MarkDirty();
    }

    public void ButtonToggleHistory()
    {
        var local = LocalPlayer;
        if (!historyOpen && (local == null || !local.inRoom)) return;
        historyOpen = !historyOpen;
        historyDirty = true;
        layoutDirty = true;
        MarkDirty();
    }

    void CloseGameOver()
    {
        gameOverOpen = false;
        historyOpen = false;
        layoutDirty = true;
        MarkDirty();
    }

    // ---- ルール設定(ホストのみ。変えたらすぐ送る) ----
    bool ShadowLive { get { return Time.unscaledTime < shadowUntil; } }
    int EffCards(GameManager gm) { return ShadowLive && shadowCards >= 0 ? shadowCards : gm.CARDCOUNT; }
    int EffMode(GameManager gm) { return ShadowLive && shadowMode >= 0 ? shadowMode : gm.ScoringMode; }
    int EffMax(GameManager gm) { return ShadowLive && shadowMax >= 0 ? shadowMax : gm.MaxPlayers; }
    int EffTime(GameManager gm) { return ShadowLive && shadowTime > -999 ? shadowTime : gm.RoundTimeLimit; }

    void SendSettings(GameManager gm, int cards, int mode, int max, int time)
    {
        var local = LocalPlayer;
        if (local == null || !local.isRoomHost || gm == null || gm.inProgress || RM == null) return;
        cards = Mathf.Clamp(cards, 3, GameManager.CARD_COUNT_LIMIT);
        max = Mathf.Clamp(max, 2, GameManager.MAX_PLAYERS_LIMIT);
        shadowCards = cards; shadowMode = mode; shadowMax = max; shadowTime = time;
        shadowUntil = Time.unscaledTime + 1.5f;
        RM.CmdUpdateSettings(local.myRoomId, cards, mode, max, time);
        MarkDirty();
    }

    void SelectMode(int mode)
    {
        var gm = LocalPlayer != null ? LocalPlayer.gameManager : null;
        if (gm == null) return;
        int cards = EffCards(gm);
        int old = EffMode(gm);
        if (mode == old) return;
        // 得点カード(ハゲタカのえじき)は15枚で遊ぶ。戻したら標準の9枚に戻す
        if (mode == GameManager.SCORING_POINT_CARDS) cards = 15;
        else if (old == GameManager.SCORING_POINT_CARDS && cards == 15) cards = GameManager.CARD_COUNT_DEFAULT;
        SendSettings(gm, cards, mode, EffMax(gm), EffTime(gm));
    }

    void StepCards(int delta)
    {
        var gm = LocalPlayer != null ? LocalPlayer.gameManager : null;
        if (gm == null) return;
        SendSettings(gm, EffCards(gm) + delta, EffMode(gm), EffMax(gm), EffTime(gm));
    }

    void StepMax(int delta)
    {
        var gm = LocalPlayer != null ? LocalPlayer.gameManager : null;
        if (gm == null) return;
        SendSettings(gm, EffCards(gm), EffMode(gm), EffMax(gm) + delta, EffTime(gm));
    }

    // 制限時間スライダー: 0..18 = 30秒〜5分(15秒刻み)、19 = 無制限
    const int TIME_STEPS = (GameManager.ROUND_TIME_MAX - GameManager.ROUND_TIME_MIN) / GameManager.ROUND_TIME_STEP + 1;
    static int SliderToSeconds(int idx) { return idx >= TIME_STEPS ? 0 : GameManager.ROUND_TIME_MIN + idx * GameManager.ROUND_TIME_STEP; }
    static int SecondsToSlider(int sec)
    {
        if (sec <= 0) return TIME_STEPS;
        return Mathf.Clamp(Mathf.RoundToInt((sec - GameManager.ROUND_TIME_MIN) / (float)GameManager.ROUND_TIME_STEP), 0, TIME_STEPS - 1);
    }

    void OnTimeSlider(float v)
    {
        if (suppressSlider) return;
        shadowTime = SliderToSeconds(Mathf.RoundToInt(v));
        var gm = LocalPlayer != null ? LocalPlayer.gameManager : null;
        if (gm != null)
        {
            if (shadowCards < 0 || !ShadowLive) { shadowCards = gm.CARDCOUNT; shadowMode = gm.ScoringMode; shadowMax = gm.MaxPlayers; }
        }
        shadowUntil = Time.unscaledTime + 1.5f;
        timeSliderSendAt = Time.unscaledTime + 0.25f;   // ドラッグ中に送りすぎない
        MarkDirty();
    }

    void UpdateTimeSliderSend()
    {
        if (timeSliderSendAt < 0f || Time.unscaledTime < timeSliderSendAt) return;
        timeSliderSendAt = -1f;
        var gm = LocalPlayer != null ? LocalPlayer.gameManager : null;
        if (gm != null) SendSettings(gm, EffCards(gm), EffMode(gm), EffMax(gm), shadowTime);
    }

    // ================= ショートカット =================
    void HandleShortcuts()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        // 入力欄に文字を打っている間は無効
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        if (sel != null && sel.GetComponent<TMP_InputField>() != null) return;

        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (leaveConfirmOpen) { leaveConfirmOpen = false; MarkDirty(); return; }
            if (historyOpen) { historyOpen = false; MarkDirty(); return; }
            if (resultOpen) { resultOpen = false; MarkDirty(); return; }
        }

        if (kb.rKey.wasPressedThisFrame) ButtonReady();
        if (kb.cKey.wasPressedThisFrame) ButtonConfirmCard();
        if (kb.hKey.wasPressedThisFrame) ButtonToggleHistory();

        // Space は、今押せる一番大事なボタン
        if (kb.spaceKey.wasPressedThisFrame)
        {
            if (game.next != null && game.next.gameObject.activeInHierarchy && game.next.Interactable) ButtonNextRound();
            else if (game.confirm != null && game.confirm.gameObject.activeInHierarchy && game.confirm.Interactable) ButtonConfirmCard();
            else if (view == View.Room) ButtonStartGame();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (kb.leftArrowKey.wasPressedThisFrame) CycleDebugPlayer(-1);
        if (kb.rightArrowKey.wasPressedThisFrame) CycleDebugPlayer(1);
        if (kb.aKey.wasPressedThisFrame) OnClickDebugAddBot();
        if (kb.dKey.wasPressedThisFrame) OnClickDebugRemoveBot();
#endif

        // 数字キー: 桁を溜めてカードを選ぶ(1, 2 と続けて押せば 12)
        if (Time.unscaledTime > numberBufferUntil) numberBuffer = "";
        for (int d = 0; d <= 9; d++)
        {
            var dk = kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + (d == 0 ? 9 : d - 1))];
            var nk = kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Numpad0 + d)];
            if (!dk.wasPressedThisFrame && !nk.wasPressedThisFrame) continue;
            numberBuffer += d.ToString();
            numberBufferUntil = Time.unscaledTime + NUMBER_INPUT_WINDOW;
            int num;
            if (int.TryParse(numberBuffer, out num) && num >= 1)
            {
                var pl = GetDebugOrLocalPlayer();
                int max = pl != null ? pl.used.Count : 0;
                if (num <= max) SelectCard(num - 1);
                if (num * 10 > max) numberBuffer = "";
            }
            break;
        }
    }

    // ================= デバッグ =================
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void OnClickDebugAddBot()
    {
        var local = LocalPlayer;
        if (local == null || local.room == null || debugPlayerPrefab == null) return;
        // ゲーム中に席を増やすと得点や手札の並びが壊れるので、ルームでのみ追加する
        var gm = local.room.gameManager;
        if (gm == null || gm.inProgress) return;
        if (local.room.playerComponents.Count >= GameManager.MAX_PLAYERS_LIMIT) return;
        if (local.room.playerComponents.Count >= gm.MaxPlayers)
            gm.UpdateSettings(gm.CARDCOUNT, gm.ScoringMode, local.room.playerComponents.Count + 1, gm.RoundTimeLimit);
        local.room.AddBotPlayer(debugPlayerPrefab);
        MarkDirty();
    }

    public void OnClickDebugRemoveBot()
    {
        var local = LocalPlayer;
        var room = local != null ? local.room : null;
        if (room == null) return;
        var gm = room.gameManager;
        if (gm == null || gm.inProgress) return;
        for (int i = room.playerComponents.Count - 1; i >= 0; i--)
        {
            var p = room.playerComponents[i];
            if (p == null || !Room.IsBot(p)) continue;
            if (NineDebugBots.Exists) NineDebugBots.Instance.Leave(p, 0);
            else room.DebugBotLeave(p);
            MarkDirty();
            return;
        }
    }

    void CycleDebugPlayer(int dir)
    {
        var local = LocalPlayer;
        if (local == null || local.room == null) return;
        var list = local.room.playerComponents.Where(p => p != null).ToList();
        if (list.Count == 0) return;
        var current = debugTargetPlayer != null ? debugTargetPlayer : local;
        int i = list.IndexOf(current);
        int next = ((i + dir) % list.Count + list.Count) % list.Count;
        DebugSetTargetPlayer(list[next]);
    }

    // 画面を別の席の視点に切り替える(手札・選択中のカード・出す/取り消す・次へ・準備・(あなた) が切り替わる)
    void ApplyViewSwitch()
    {
        var target = GetDebugOrLocalPlayer();
        selectedIdx = (target != null && !target.isReadytoTurn) ? target.pendingSelection : -1;
        layoutDirty = true;
        MarkDirty();
        if (OnViewSwitched != null) OnViewSwitched(target);
    }

    public event System.Action<Player> OnViewSwitched;

    public Player DebugTargetPlayer { get { return debugTargetPlayer; } }

    public void DebugSetTargetPlayer(Player p)
    {
        var local = LocalPlayer;
        debugTargetPlayer = (p == local) ? null : p;
        ApplyViewSwitch();
    }

    public void DebugSetRoomInput(string roomId)
    {
        if (title != null && title.roomId != null) title.roomId.text = roomId;
    }

    public GameObject DebugBotPrefab { get { return debugPlayerPrefab; } }
#endif
}
