#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

// テスト用ボットの出し方
public enum NineBotMode
{
    Random,   // 残りからランダム
    Lowest,   // いちばん小さい数字
    Highest,  // いちばん大きい数字
    Smart,    // ハゲタカ: 得点カード(+持ち越し)の大きさに見合った数字。通常モードはランダム
    Idle,     // 何もしない(時間切れ→自動提出を試す)
}

[Serializable]
public class NineBotSettings
{
    public NineBotMode mode = NineBotMode.Random;
    public float minDelay = 0.4f;
    public float maxDelay = 1.5f;
    public bool autoReady = true;
    public bool autoNext = true;
    [Range(0f, 1f)] public float cancelChance = 0f;   // 確定した後に取り消して選び直す確率

    public NineBotSettings Clone() => (NineBotSettings)MemberwiseClone();
}

// エディタ(とDevelopment Build)専用のテスト用ボット。
// Botは接続を持たないので、Commandの代わりにサーバー上のDebug系メソッドで操作する。
// 軍人将棋の OnlineTestBot と同じく「人がやる操作」だけを行い、ゲーム側の判定には手を入れない。
// 窓「Nine/オンライン デバッグ」から設定する。
public sealed class NineDebugBots : MonoBehaviour
{
    static NineDebugBots instance;
    public static NineDebugBots Instance
    {
        get
        {
            if (instance == null && Application.isPlaying)
            {
                var go = new GameObject("[NineDebugBots]") { hideFlags = HideFlags.DontSave };
                DontDestroyOnLoad(go);
                instance = go.AddComponent<NineDebugBots>();
            }
            return instance;
        }
    }
    public static bool Exists => instance != null;

    // ---- 全体の設定 ----
    public bool autoPlay = true;              // Botを自動で動かす
    public bool autoPlayLocal = false;        // 自分(ホスト)も自動で動かす(通しの動作確認用)
    public bool skipControlledBot = true;     // 画面で操作中のBotは自動で動かさない
    public bool autoRestart = false;          // ゲーム終了後、全員Readyなら次のゲームを自動で始める
    public NineBotSettings defaults = new NineBotSettings();

    // ---- 画面の切り替え(軍人将棋のホットシートと同じ考え方) ----
    // 手動の席: 自動で動かさず、その席の画面に切り替えて自分で操作する席。
    // 自分(ホスト)は「自分も自動で動かす」がオフなら手動。Botは既定で自動、窓から手動にできる。
    public readonly HashSet<Player> manualBots = new HashSet<Player>();
    public bool newBotsManual = false;        // 新しく入ったBotを手動にする
    public bool autoSwitchView = false;       // 表示中の席が確定(Ready/Next)したら、次のまだの手動の席へ画面を切り替える
    readonly HashSet<Player> seenBots = new HashSet<Player>();
    float switchAt = -1f;

    public bool IsManual(Player p)
    {
        if (p == null) return false;
        if (Room.IsBot(p)) return manualBots.Contains(p);
        return !(autoPlayLocal && p == LocalPlayer);
    }

    public void SetManual(Player bot, bool manual)
    {
        if (!Room.IsBot(bot)) return;
        if (manual) manualBots.Add(bot); else manualBots.Remove(bot);
    }

    class BotState
    {
        public NineBotSettings overrideSettings;
        public string phase = "";
        public float due;
        public bool rolledCancel;
        public bool pendingCancel;
        public string lastAction = "";
    }
    readonly Dictionary<Player, BotState> states = new Dictionary<Player, BotState>();

    // 抜けているBot(戻るときに使う)。値 = (元の部屋, 何ラウンド目の終了後に戻るか。-1なら手動)
    class Away { public Room room; public int returnAfterRound = -1; }
    readonly Dictionary<Player, Away> away = new Dictionary<Player, Away>();

    readonly Dictionary<Room, float> restartAt = new Dictionary<Room, float>();

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // ---- 個別設定 ----
    BotState State(Player p)
    {
        if (!states.TryGetValue(p, out var s)) { s = new BotState(); states[p] = s; }
        return s;
    }

    public NineBotSettings SettingsFor(Player p) => State(p).overrideSettings ?? defaults;
    public bool HasOverride(Player p) => states.TryGetValue(p, out var s) && s.overrideSettings != null;
    public void SetOverride(Player p, NineBotSettings s) => State(p).overrideSettings = s;
    public string LastAction(Player p) => states.TryGetValue(p, out var s) ? s.lastAction : "";

    public IEnumerable<Player> AwayBots(Room room) => away.Where(kv => kv.Key != null && kv.Value.room == room).Select(kv => kv.Key);
    public int ReturnAfterRound(Player p) => away.TryGetValue(p, out var a) ? a.returnAfterRound : -1;

    static UIEventsManager UI => UIEventsManager.Current;
    static Player LocalPlayer => NetworkClient.connection?.identity?.GetComponent<Player>();

    // ---- 抜ける/戻る ----

    // returnAfterRounds: 何ラウンド後に自動で戻るか。0以下なら手動で戻す
    public bool Leave(Player bot, int returnAfterRounds)
    {
        var room = bot != null ? bot.room : null;
        if (room == null) return false;
        var gm = room.gameManager;
        bool inGame = gm != null && gm.inProgress;
        int target = returnAfterRounds > 0 && gm != null ? gm.DebugRoundsPlayed + returnAfterRounds : -1;

        var ui = UI;
        if (ui != null && ui.DebugTargetPlayer == bot) ui.DebugSetTargetPlayer(null);

        if (!room.DebugBotLeave(bot)) return false;
        if (inGame) away[bot] = new Away { room = room, returnAfterRound = target };
        else states.Remove(bot);
        return true;
    }

    public bool Rejoin(Player bot)
    {
        if (bot == null || !away.TryGetValue(bot, out var a)) return false;
        if (!a.room.DebugBotRejoin(bot)) return false;
        away.Remove(bot);
        if (states.TryGetValue(bot, out var s)) s.phase = "";   // 戻った直後から考え直す
        return true;
    }

    // ---- 操作(窓のボタンから) ----

    public bool PlayNow(Player p, NineBotMode mode)
    {
        var gm = p != null ? p.gameManager : null;
        if (gm == null || !gm.inProgress || !gm.DebugAcceptingCards || p.isReadytoTurn) return false;
        int idx = ChooseCard(gm, p, mode);
        if (idx < 0) return false;
        p.DebugSetPendingSelection(idx);
        bool ok = p.DebugUseCard(idx);
        if (ok) State(p).lastAction = "確定 " + p.cards[idx] + "(手動)";
        return ok;
    }

    // ---- 毎フレーム ----

    void Update()
    {
        if (!NetworkServer.active || RoomManager.Instance == null) return;
        float now = Time.unscaledTime;

        // 片付け: 消えたPlayer
        foreach (var dead in states.Keys.Where(k => k == null).ToList()) states.Remove(dead);
        foreach (var dead in away.Keys.Where(k => k == null).ToList()) away.Remove(dead);
        manualBots.RemoveWhere(k => k == null);
        seenBots.RemoveWhere(k => k == null);

        UpdateAway();

        var controlled = UI?.DebugTargetPlayer;
        var local = LocalPlayer;

        foreach (var info in RoomManager.Instance.roomDict.Values.ToList())
        {
            var room = info.room;
            var gm = room?.gameManager;
            if (gm == null) continue;

            if (autoPlay)
            {
                foreach (var p in room.playerComponents.ToList())
                {
                    if (p == null) continue;
                    bool bot = Room.IsBot(p);
                    if (bot && seenBots.Add(p) && newBotsManual) manualBots.Add(p);
                    if (!bot && !(autoPlayLocal && p == local)) continue;
                    if (bot && manualBots.Contains(p)) continue;
                    if (bot && skipControlledBot && p == controlled) continue;
                    Tick(gm, p, now);
                }
            }

            // 自動で次のゲーム
            if (autoRestart && !gm.inProgress && room.AllPlayersReady())
            {
                if (!restartAt.TryGetValue(room, out float at)) restartAt[room] = now + 1.5f;
                else if (now >= at)
                {
                    restartAt.Remove(room);
                    NineDebugLog.Add(room.roomId, "自動で次のゲームを開始");
                    gm.StartGame();
                }
            }
            else restartAt.Remove(room);
        }

        UpdateAutoSwitch(now);
    }

    // 今の段階で、その席がやることを済ませたか
    static Func<Player, bool> DoneCondition(GameManager gm)
    {
        if (!gm.inProgress) return p => p.isReadyToStart;
        if (gm.DebugAcceptingCards) return p => p.isReadytoTurn;
        if (gm.DebugInTransition) return p => p.isReadyForNextRound;
        return null;
    }

    // 表示中の席が済んだら、席順で次の「まだの手動の席」の画面へ切り替える
    void UpdateAutoSwitch(float now)
    {
        if (!autoSwitchView) { switchAt = -1f; return; }
        var ui = UI;
        var local = LocalPlayer;
        if (ui == null || local == null || local.room == null) return;
        // 観戦中は席の視点に入らない(観戦者は誰の手札も持たない)
        if (local.isSpectator) { switchAt = -1f; return; }
        var room = local.room;
        var gm = room.gameManager;
        if (gm == null) return;
        var done = DoneCondition(gm);
        if (done == null) return;

        Player viewer = ui.DebugTargetPlayer;
        if (viewer == null) viewer = local;
        if (!done(viewer)) { switchAt = -1f; return; }

        var next = NextPendingManualSeat(room, viewer, done);
        if (next == null) { switchAt = -1f; return; }
        // 確定した様子が一瞬見えるように、少し置いてから切り替える
        if (switchAt < 0f) { switchAt = now + 0.35f; return; }
        if (now < switchAt) return;
        switchAt = -1f;
        ui.DebugSetTargetPlayer(next);
    }

    public Player NextPendingManualSeat(Room room, Player from, Func<Player, bool> done)
    {
        var seats = room.playerComponents;
        int n = seats.Count;
        int start = Mathf.Max(0, seats.IndexOf(from));
        for (int k = 1; k <= n; k++)
        {
            var p = seats[(start + k) % n];
            if (p != null && p != from && IsManual(p) && !done(p)) return p;
        }
        return null;
    }

    // 窓の表示用: 今の段階で済んでいるか(判定できない段階ならnull)
    public static bool? IsDone(Player p)
    {
        var gm = p != null ? p.gameManager : null;
        if (gm == null) return null;
        var done = DoneCondition(gm);
        return done == null ? (bool?)null : done(p);
    }

    void UpdateAway()
    {
        foreach (var kv in away.ToList())
        {
            var bot = kv.Key;
            var a = kv.Value;
            var gm = a.room?.gameManager;
            if (gm == null || !gm.inProgress)
            {
                // 戻る前にゲームが終わった: 空席は次のゲーム開始時に片付くので、Botも消す
                away.Remove(bot);
                states.Remove(bot);
                if (bot != null) NetworkServer.Destroy(bot.gameObject);
                continue;
            }
            if (a.returnAfterRound >= 0 && gm.DebugRoundsPlayed >= a.returnAfterRound && gm.DebugAcceptingCards)
                Rejoin(bot);
        }
    }

    void Tick(GameManager gm, Player p, float now)
    {
        var st = State(p);
        var s = st.overrideSettings ?? defaults;

        string phase;
        if (!gm.inProgress) phase = "lobby";
        else if (gm.DebugAcceptingCards) phase = "round" + gm.DebugRoundsPlayed;
        else if (gm.DebugInTransition) phase = "next" + gm.DebugRoundsPlayed;
        else phase = "wait";

        if (phase != st.phase)
        {
            st.phase = phase;
            st.due = now + Delay(s);
            st.rolledCancel = false;
            st.pendingCancel = false;
        }
        if (now < st.due) return;

        if (phase == "lobby")
        {
            if (s.autoReady && !p.isReadyToStart && p.room != null)
            {
                p.DebugSetReady(true);
                st.lastAction = "Ready";
            }
        }
        else if (phase.StartsWith("round"))
        {
            if (s.mode == NineBotMode.Idle) return;
            if (!p.isReadytoTurn)
            {
                int idx = ChooseCard(gm, p, s.mode);
                if (idx < 0) return;
                p.DebugSetPendingSelection(idx);
                if (!p.DebugUseCard(idx)) return;
                st.lastAction = "確定 " + p.cards[idx];
                if (!st.rolledCancel)
                {
                    st.rolledCancel = true;
                    if (UnityEngine.Random.value < s.cancelChance)
                    {
                        st.pendingCancel = true;
                        st.due = now + Delay(s);
                    }
                }
            }
            else if (st.pendingCancel)
            {
                st.pendingCancel = false;
                p.DebugCancelCard();
                st.lastAction = "取り消し";
                st.due = now + Delay(s);
            }
        }
        else if (phase.StartsWith("next"))
        {
            if (s.autoNext && !p.isReadyForNextRound)
            {
                p.DebugReadyForNextRound();
                st.lastAction = "Next";
            }
        }
    }

    static float Delay(NineBotSettings s) => UnityEngine.Random.Range(Mathf.Max(0f, s.minDelay), Mathf.Max(s.minDelay, s.maxDelay));

    // 出すカードのindexを選ぶ(-1 = 出せるカードなし)
    public static int ChooseCard(GameManager gm, Player p, NineBotMode mode)
    {
        var unused = new List<int>();
        for (int i = 0; i < p.used.Count && i < p.cards.Count; i++) if (!p.used[i]) unused.Add(i);
        if (unused.Count == 0) return -1;
        unused.Sort((x, y) => p.cards[x].CompareTo(p.cards[y]));

        switch (mode)
        {
            case NineBotMode.Lowest: return unused[0];
            case NineBotMode.Highest: return unused[unused.Count - 1];
            case NineBotMode.Smart:
                if (gm.ScoringMode == GameManager.SCORING_POINT_CARDS && gm.currentPointCard != 0)
                {
                    int value = gm.currentPointCard;
                    foreach (var c in gm.carriedPointCards) value += c;
                    var deck = gm.DebugPointDeck;
                    int maxAbs = 1;
                    foreach (var d in deck) maxAbs = Mathf.Max(maxAbs, Mathf.Abs(d));
                    float t = Mathf.Clamp01((Mathf.Abs(value) - 1f) / Mathf.Max(1, maxAbs - 1));
                    t = Mathf.Clamp01(t + UnityEngine.Random.Range(-0.15f, 0.15f));
                    return unused[Mathf.RoundToInt(t * (unused.Count - 1))];
                }
                return unused[UnityEngine.Random.Range(0, unused.Count)];
            default:
                return unused[UnityEngine.Random.Range(0, unused.Count)];
        }
    }

    // ---- クイックスタート ----
    // ホスト開始 → 部屋を作る → 入る → 設定 → Bot追加 → 全員Ready → 開始 までを一度に行う。
    public Coroutine QuickStart(int bots, int cardCount, int scoringMode, int roundTimeLimit, bool startGame)
        => StartCoroutine(QuickStartRoutine(bots, cardCount, scoringMode, roundTimeLimit, startGame));

    public bool QuickStartRunning { get; private set; }

    IEnumerator QuickStartRoutine(int bots, int cardCount, int scoringMode, int roundTimeLimit, bool startGame)
    {
        if (QuickStartRunning) yield break;
        QuickStartRunning = true;

        // NetworkManagerの初期化(Start)が済むのを待つ
        float limit = Time.unscaledTime + 10f;
        while (NetworkManager.singleton == null && Time.unscaledTime < limit) yield return null;
        yield return null;
        yield return null;
        if (NetworkManager.singleton == null) { Fail("NetworkManagerが見つかりません"); yield break; }

        if (!NetworkServer.active && !NetworkClient.active)
            NetworkManager.singleton.StartHost();
        else if (!NetworkServer.active)
        {
            Fail("クライアントとして接続中です。クイックスタートはホスト(エディタがサーバー)でのみ使えます");
            yield break;
        }

        limit = Time.unscaledTime + 10f;
        while ((!NetworkClient.ready || RoomManager.Instance == null || LocalPlayer == null) && Time.unscaledTime < limit)
            yield return null;
        var local = LocalPlayer;
        var rm = RoomManager.Instance;
        if (local == null || rm == null) { Fail("ローカルプレイヤーが用意できませんでした"); yield break; }

        var room = local.room;
        if (room == null)
        {
            string roomId;
            do { roomId = "dbg" + UnityEngine.Random.Range(100, 1000); } while (rm.roomDict.ContainsKey(roomId));
            rm.CmdCreateRoom(roomId, "", UIEventsManager.GetClientToken());
            limit = Time.unscaledTime + 5f;
            while (!rm.roomDict.ContainsKey(roomId) && Time.unscaledTime < limit) yield return null;
            rm.CmdJoinRoom(roomId, "", UIEventsManager.GetClientToken());
            limit = Time.unscaledTime + 5f;
            while (local.room == null && Time.unscaledTime < limit) yield return null;
            room = local.room;
            if (room == null) { Fail("部屋に入れませんでした"); yield break; }
        }

        UI?.DebugSetRoomInput(room.roomId);
        var gm = room.gameManager;
        if (gm.inProgress) { Done(room, "ゲーム中のため設定・Bot追加は省略"); yield break; }

        int want = Mathf.Clamp(bots, 1, GameManager.MAX_PLAYERS_LIMIT - 1);
        int presentBots = room.playerComponents.Count(p => Room.IsBot(p));
        int total = room.PresentCount + Mathf.Max(0, want - presentBots);
        gm.UpdateSettings(cardCount, scoringMode, Mathf.Max(total, gm.MaxPlayers), roundTimeLimit);

        var ui = UI;
        GameObject prefab = ui != null ? ui.DebugBotPrefab : null;
        if (prefab == null) prefab = NetworkManager.singleton.playerPrefab;
        for (int i = presentBots; i < want && room.playerComponents.Count < GameManager.MAX_PLAYERS_LIMIT; i++)
            room.AddBotPlayer(prefab);
        yield return null;

        if (startGame)
        {
            foreach (var p in room.playerComponents) if (p != null) p.DebugSetReady(true);
            yield return null;
            gm.StartGame();
        }
        Done(room, "Bot" + want + "体 / " + gm.CARDCOUNT + "枚 / 方式" + gm.ScoringMode + (startGame ? " / 開始" : ""));
    }

    void Fail(string msg)
    {
        QuickStartRunning = false;
        NineDebugLog.Add(null, "クイックスタート失敗: " + msg);
        Debug.LogWarning("[NineDebug] クイックスタート失敗: " + msg);
    }

    void Done(Room room, string msg)
    {
        QuickStartRunning = false;
        NineDebugLog.Add(room?.roomId, "クイックスタート: " + msg);
    }
}
#endif
