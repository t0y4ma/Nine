using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEngine;

// Nine のローカルデバッグ窓(軍人将棋の「オンライン デバッグ」窓とホットシートを参考にしたもの)。
// ・画面の切り替え: 1つのGameビューで、各席から見た画面(手札・選択・Confirm/Cancel・Next・Ready・(You))に切り替えて操作する。
//   席ごとに手動/自動を決め、「確定したら次の手動の席へ」でホットシートのように全員ぶんを順に操作できる
// ・クイックスタート: ホスト開始→部屋作成→参加→設定→Bot追加→全員Ready→開始 を1クリックで(Play開始時に自動でも可)
// ・テスト用ボット: 出し方(ランダム/最小/最大/ハゲタカ向け/放置)・考える時間・自動Ready/Next・取り消し確率
// ・部屋と席の様子: 確定カード(公開前)、得点、ハゲタカの山札、残り時間
// ・操作: 時間切れにする/Next待ちを飛ばす/タイマー停止、Botの退出→(Nラウンド後に)復帰、自分のLeave→再Join
// ・ゲームのログ
public class NineDebugWindow : EditorWindow
{
    const string P = "nine.debug.";

    [MenuItem("Nine/オンライン デバッグ", priority = 0)]
    public static void Open() => GetWindow<NineDebugWindow>("Nine デバッグ");

    // ---- 設定(EditorPrefsに保存) ----
    internal static int QsBots { get => EditorPrefs.GetInt(P + "qs.bots", 2); set => EditorPrefs.SetInt(P + "qs.bots", value); }
    internal static int QsCards { get => EditorPrefs.GetInt(P + "qs.cards", 9); set => EditorPrefs.SetInt(P + "qs.cards", value); }
    internal static int QsScoring { get => EditorPrefs.GetInt(P + "qs.scoring", GameManager.SCORING_DEFAULT); set => EditorPrefs.SetInt(P + "qs.scoring", value); }
    internal static int QsTime { get => EditorPrefs.GetInt(P + "qs.time", GameManager.ROUND_TIME_DEFAULT); set => EditorPrefs.SetInt(P + "qs.time", value); }
    internal static bool QsStart { get => EditorPrefs.GetBool(P + "qs.start", true); set => EditorPrefs.SetBool(P + "qs.start", value); }
    internal static bool QsOnPlay { get => EditorPrefs.GetBool(P + "qs.onPlay", false); set => EditorPrefs.SetBool(P + "qs.onPlay", value); }

    static bool AutoPlay { get => EditorPrefs.GetBool(P + "autoPlay", true); set => EditorPrefs.SetBool(P + "autoPlay", value); }
    static bool AutoLocal { get => EditorPrefs.GetBool(P + "autoLocal", false); set => EditorPrefs.SetBool(P + "autoLocal", value); }
    static bool SkipControlled { get => EditorPrefs.GetBool(P + "skipControlled", true); set => EditorPrefs.SetBool(P + "skipControlled", value); }
    static bool AutoRestart { get => EditorPrefs.GetBool(P + "autoRestart", false); set => EditorPrefs.SetBool(P + "autoRestart", value); }
    static bool AutoSwitchView { get => EditorPrefs.GetBool(P + "autoSwitchView", true); set => EditorPrefs.SetBool(P + "autoSwitchView", value); }
    static bool NewBotsManual { get => EditorPrefs.GetBool(P + "newBotsManual", false); set => EditorPrefs.SetBool(P + "newBotsManual", value); }
    static int ReturnAfter { get => EditorPrefs.GetInt(P + "returnAfter", 2); set => EditorPrefs.SetInt(P + "returnAfter", value); }

    static NineBotSettings LoadBotDefaults() => new NineBotSettings
    {
        mode = (NineBotMode)EditorPrefs.GetInt(P + "bot.mode", (int)NineBotMode.Random),
        minDelay = EditorPrefs.GetFloat(P + "bot.min", 0.4f),
        maxDelay = EditorPrefs.GetFloat(P + "bot.max", 1.5f),
        autoReady = EditorPrefs.GetBool(P + "bot.ready", true),
        autoNext = EditorPrefs.GetBool(P + "bot.next", true),
        cancelChance = EditorPrefs.GetFloat(P + "bot.cancel", 0f),
    };

    static void SaveBotDefaults(NineBotSettings s)
    {
        EditorPrefs.SetInt(P + "bot.mode", (int)s.mode);
        EditorPrefs.SetFloat(P + "bot.min", s.minDelay);
        EditorPrefs.SetFloat(P + "bot.max", s.maxDelay);
        EditorPrefs.SetBool(P + "bot.ready", s.autoReady);
        EditorPrefs.SetBool(P + "bot.next", s.autoNext);
        EditorPrefs.SetFloat(P + "bot.cancel", s.cancelChance);
    }

    // 保存した設定を実行中のボットに反映する
    internal static void ApplyPrefs(NineDebugBots b)
    {
        if (b == null) return;
        b.autoPlay = AutoPlay;
        b.autoPlayLocal = AutoLocal;
        b.skipControlledBot = SkipControlled;
        b.autoRestart = AutoRestart;
        b.autoSwitchView = AutoSwitchView;
        b.newBotsManual = NewBotsManual;
        b.defaults = LoadBotDefaults();
    }

    internal static void RunQuickStart()
    {
        var b = NineDebugBots.Instance;
        if (b == null) return;
        ApplyPrefs(b);
        int cards = QsScoring == GameManager.SCORING_POINT_CARDS ? GameManager.CARD_COUNT_LIMIT : QsCards;
        b.QuickStart(QsBots, cards, QsScoring, QsTime, QsStart);
    }

    static readonly string[] ScoringNames = { "勝ち1pt", "出した数の合計", "ハゲタカ" };
    static readonly int[] TimeValues = { 0, 30, 45, 60, 90, 120, 180, 300 };
    static readonly string[] TimeNames = { "無制限", "30秒", "45秒", "60秒", "90秒", "2分", "3分", "5分" };

    Vector2 scroll, logScroll;
    bool foldView = true, foldQs = true, foldBot = true, foldRooms = true, foldLog = true;
    string logFilter = "";
    static string lastLocalRoomId = "";
    double lastRepaint;

    void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeChanged;
    void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    void OnPlayModeChanged(PlayModeStateChange s) => Repaint();

    void Update()
    {
        if (!Application.isPlaying) return;
        if (EditorApplication.timeSinceStartup - lastRepaint < 0.25) return;
        lastRepaint = EditorApplication.timeSinceStartup;
        Repaint();
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawStatus();
        DrawViews();
        DrawQuickStart();
        DrawBotSettings();
        DrawRooms();
        DrawLog();
        EditorGUILayout.EndScrollView();
    }

    // ───────── 状態 ─────────
    void DrawStatus()
    {
        string s;
        if (!Application.isPlaying) s = "停止中(Playすると操作できます)";
        else if (NetworkServer.active && NetworkClient.active) s = "ホスト(このエディタがサーバー兼プレイヤー)";
        else if (NetworkServer.active) s = "サーバーのみ";
        else if (NetworkClient.active) s = "クライアント(別サーバーに接続中。部屋の中身はサーバー側でしか見えません)";
        else s = "未接続";
        EditorGUILayout.HelpBox(s, MessageType.None);
    }

    // ───────── 画面の切り替え ─────────
    void DrawViews()
    {
        foldView = EditorGUILayout.BeginFoldoutHeaderGroup(foldView, "画面の切り替え");
        if (foldView)
        {
            EditorGUI.BeginChangeCheck();
            AutoSwitchView = EditorGUILayout.ToggleLeft(new GUIContent("確定したら、次のまだの手動の席の画面へ切り替える(ホットシート)",
                "表示中の席が Confirm / Next / Ready を済ませると、席順で次の手動の席へ移ります"), AutoSwitchView);
            NewBotsManual = EditorGUILayout.ToggleLeft(new GUIContent("新しく入ったBotを手動にする", "オンにしてからクイックスタートすると、全員を自分で操作するホットシートになります"), NewBotsManual);
            if (EditorGUI.EndChangeCheck() && Application.isPlaying && NineDebugBots.Exists) ApplyPrefs(NineDebugBots.Instance);

            var ui = Application.isPlaying ? UIEventsManager.Current : null;
            var local = Application.isPlaying ? NetworkClient.connection?.identity?.GetComponent<Player>() : null;
            if (ui == null || local == null || local.room == null || !NetworkServer.active)
            {
                EditorGUILayout.LabelField("(ホストで部屋に入ると、席ごとの画面に切り替えられます。Gameビューでは ←/→ でも切り替え)", EditorStyles.miniLabel);
            }
            else
            {
                var bots = NineDebugBots.Instance;
                Player viewer = ui.DebugTargetPlayer;
                if (viewer == null) viewer = local;
                var seats = local.room.playerComponents;
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = 0; i < seats.Count; i++)
                    {
                        var p = seats[i];
                        string who = p == null ? "空席" : p == local ? "自分" : Room.IsBot(p) ? "Bot" : "人";
                        bool? done = NineDebugBots.IsDone(p);
                        string mark = done == true ? " ✓" : "";
                        string mode = p == null ? "" : bots.IsManual(p) ? "" : " (自動)";
                        var style = new GUIStyle(EditorStyles.miniButton) { fixedHeight = 30, fontStyle = p == viewer ? FontStyle.Bold : FontStyle.Normal };
                        using (new EditorGUI.DisabledScope(p == null))
                        {
                            bool on = GUILayout.Toggle(p == viewer, "P" + i + " " + who + mode + mark, style);
                            if (on && p != viewer && p != null) ui.DebugSetTargetPlayer(p);
                        }
                    }
                }
                EditorGUILayout.LabelField("表示中: P" + viewer.playerId + (viewer == local ? "(自分)" : Room.IsBot(viewer) ? "(Bot)" : "")
                    + "   ✓=今の段階(Ready/Confirm/Next)を済ませた席   (自動)=Botが自分で動く席。表示中の自動Botは止まって操作を待ちます", EditorStyles.miniLabel);
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ───────── クイックスタート ─────────
    void DrawQuickStart()
    {
        foldQs = EditorGUILayout.BeginFoldoutHeaderGroup(foldQs, "クイックスタート");
        if (foldQs)
        {
            QsBots = EditorGUILayout.IntSlider("Botの数", QsBots, 1, GameManager.MAX_PLAYERS_LIMIT - 1);
            QsScoring = EditorGUILayout.Popup("得点方式", Mathf.Clamp(QsScoring, 0, 2), ScoringNames);
            using (new EditorGUI.DisabledScope(QsScoring == GameManager.SCORING_POINT_CARDS))
                QsCards = EditorGUILayout.IntSlider(QsScoring == GameManager.SCORING_POINT_CARDS ? "カード枚数(ハゲタカは15)" : "カード枚数", QsCards, 3, GameManager.CARD_COUNT_LIMIT);
            int ti = System.Array.IndexOf(TimeValues, QsTime);
            ti = EditorGUILayout.Popup("選択時間", ti < 0 ? 2 : ti, TimeNames);
            QsTime = TimeValues[ti];
            QsStart = EditorGUILayout.Toggle("ゲームも開始する", QsStart);
            QsOnPlay = EditorGUILayout.Toggle(new GUIContent("Play開始時に自動で実行", "Playボタンを押すだけで、設定どおりの部屋でゲームが始まります"), QsOnPlay);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!Application.isPlaying)
                {
                    if (GUILayout.Button("Playしてクイックスタート", GUILayout.Height(24)))
                    {
                        NineDebugPlayHook.RequestQuickStartOnce();
                        EditorApplication.isPlaying = true;
                    }
                }
                else
                {
                    bool running = NineDebugBots.Exists && NineDebugBots.Instance.QuickStartRunning;
                    using (new EditorGUI.DisabledScope(running || (NetworkClient.active && !NetworkServer.active)))
                        if (GUILayout.Button(running ? "実行中…" : "クイックスタート", GUILayout.Height(24))) RunQuickStart();
                }
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ───────── ボットの既定 ─────────
    void DrawBotSettings()
    {
        foldBot = EditorGUILayout.BeginFoldoutHeaderGroup(foldBot, "テスト用ボット");
        if (foldBot)
        {
            EditorGUI.BeginChangeCheck();
            AutoPlay = EditorGUILayout.Toggle("Botを自動で動かす", AutoPlay);
            SkipControlled = EditorGUILayout.Toggle(new GUIContent("操作中のBotは動かさない", "←/→ や「操作」で画面の操作対象にしたBotは、自動で動かしません"), SkipControlled);
            AutoLocal = EditorGUILayout.Toggle(new GUIContent("自分も自動で動かす", "通しの動作確認用。自分の手番もBotと同じ設定で自動で出します"), AutoLocal);
            AutoRestart = EditorGUILayout.Toggle(new GUIContent("終わったら次のゲームを自動で開始", "全員Readyになったら1.5秒後に開始。長時間の連続テスト用"), AutoRestart);

            var s = LoadBotDefaults();
            s.mode = (NineBotMode)EditorGUILayout.EnumPopup(new GUIContent("出し方(既定)", "Random/Lowest/Highest/Smart(ハゲタカで得点に見合う数)/Idle(何もしない→時間切れの自動提出)"), s.mode);
            float min = s.minDelay, max = s.maxDelay;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("考える時間(秒)");
                min = EditorGUILayout.FloatField(min, GUILayout.Width(40));
                EditorGUILayout.MinMaxSlider(ref min, ref max, 0f, 10f);
                max = EditorGUILayout.FloatField(max, GUILayout.Width(40));
            }
            s.minDelay = Mathf.Clamp(min, 0f, 30f);
            s.maxDelay = Mathf.Clamp(Mathf.Max(max, s.minDelay), 0f, 30f);
            s.autoReady = EditorGUILayout.Toggle("自動でReady", s.autoReady);
            s.autoNext = EditorGUILayout.Toggle("自動でNext", s.autoNext);
            s.cancelChance = EditorGUILayout.Slider(new GUIContent("取り消して選び直す確率", "確定後に取り消し→選び直しをする確率。取り消し処理の確認用"), s.cancelChance, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                SaveBotDefaults(s);
                if (Application.isPlaying && NineDebugBots.Exists) ApplyPrefs(NineDebugBots.Instance);
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ───────── 部屋 ─────────
    void DrawRooms()
    {
        foldRooms = EditorGUILayout.BeginFoldoutHeaderGroup(foldRooms, "部屋と席");
        if (foldRooms)
        {
            if (!Application.isPlaying)
                EditorGUILayout.LabelField("(Play中に表示)", EditorStyles.miniLabel);
            else if (!NetworkServer.active || RoomManager.Instance == null)
                EditorGUILayout.HelpBox("このエディタはサーバーではありません。ホストで始めると部屋の中身が見えます。", MessageType.Info);
            else
            {
                var bots = NineDebugBots.Instance;
                var ui = UIEventsManager.Current;
                var local = NetworkClient.connection?.identity?.GetComponent<Player>();
                DrawLocalControls(local, ui);

                ReturnAfter = EditorGUILayout.IntSlider(new GUIContent("Bot退出後、自動で戻るまで", "ラウンド数。0なら「戻す」を押すまで戻らない"), ReturnAfter, 0, 10);

                var rooms = RoomManager.Instance.roomDict.Values.ToList();
                if (rooms.Count == 0) EditorGUILayout.LabelField("部屋はありません", EditorStyles.miniLabel);
                foreach (var info in rooms) DrawRoom(info.room, bots, ui, local);
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void DrawLocalControls(Player local, UIEventsManager ui)
    {
        if (local == null) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            var target = ui != null ? ui.DebugTargetPlayer : null;
            EditorGUILayout.LabelField("画面の操作対象: " + (target == null ? "自分" : "席" + target.playerId + (Room.IsBot(target) ? "(Bot)" : "")), GUILayout.MinWidth(160));
            using (new EditorGUI.DisabledScope(target == null || ui == null))
                if (GUILayout.Button("自分に戻す", GUILayout.Width(80))) ui.DebugSetTargetPlayer(null);

            if (local.room != null)
            {
                lastLocalRoomId = local.room.roomId;
                if (GUILayout.Button(new GUIContent("自分: Leave", "退出ボタンと同じ処理(確認なし)。ゲーム中なら空席になります"), GUILayout.Width(90)))
                    RoomManager.Instance.CmdLeaveRoom(local.room.roomId);
            }
            else
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(lastLocalRoomId) || !RoomManager.Instance.roomDict.ContainsKey(lastLocalRoomId)))
                    if (GUILayout.Button(new GUIContent("自分: 再Join " + lastLocalRoomId, "同じ端末の識別子でJoin。ゲーム中なら元の席に戻ります"), GUILayout.Width(140)))
                    {
                        ui?.DebugSetRoomInput(lastLocalRoomId);
                        RoomManager.Instance.CmdJoinRoom(lastLocalRoomId, "", UIEventsManager.GetClientToken());
                    }
            }
        }
    }

    void DrawRoom(Room room, NineDebugBots bots, UIEventsManager ui, Player local)
    {
        var gm = room?.gameManager;
        if (gm == null) return;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        string phase = !gm.inProgress ? "ロビー" : gm.DebugAcceptingCards ? "選択中" : gm.DebugInTransition ? "結果表示(Next待ち)" : "処理中";
        int roundNo = gm.DebugRoundsPlayed + (gm.inProgress && !gm.DebugInTransition ? 1 : 0);
        EditorGUILayout.LabelField("部屋 " + room.roomId + "   " + phase + (gm.inProgress ? "   ラウンド " + roundNo + " / " + gm.CARDCOUNT : ""), EditorStyles.boldLabel);

        string time = gm.RoundTimeLimit <= 0 ? "無制限" : gm.RoundTimeLimit + "秒";
        string remain = "";
        if (gm.inProgress && gm.DebugAcceptingCards)
            remain = float.IsInfinity(gm.DebugTimerRemaining) ? "   残り ∞" : "   残り " + gm.DebugTimerRemaining.ToString("0.0") + "秒";
        if (gm.DebugTimerPaused) remain += "(停止中)";
        EditorGUILayout.LabelField("カード" + gm.CARDCOUNT + "枚   " + ScoringNames[Mathf.Clamp(gm.ScoringMode, 0, 2)] + "   選択時間 " + time
            + "   人数 " + room.PresentCount + " / " + gm.MaxPlayers + remain, EditorStyles.miniLabel);

        if (gm.ScoringMode == GameManager.SCORING_POINT_CARDS && gm.inProgress)
        {
            var deck = gm.DebugPointDeck;
            // 選択中は今の得点カード(pointDeck[roundsPlayed])の次から、結果表示中はroundsPlayedが進んでいるのでそこから
            int next = gm.DebugRoundsPlayed + (gm.DebugInTransition ? 0 : 1);
            string rest = deck != null && next < deck.Count ? string.Join(" ", deck.Skip(next).Select(GameManager.FormatPoints)) : "";
            EditorGUILayout.LabelField("得点カード " + GameManager.FormatPoints(gm.currentPointCard)
                + (gm.carriedPointCards.Count > 0 ? "   持ち越し [" + string.Join(" ", gm.carriedPointCards.Select(GameManager.FormatPoints)) + "]" : "")
                + "   この後の山札: " + rest, EditorStyles.miniLabel);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (gm.inProgress)
            {
                using (new EditorGUI.DisabledScope(!gm.DebugAcceptingCards))
                    if (GUILayout.Button(new GUIContent("時間切れにする", "残り時間を0に。未確定の席は選択中のカード→なければランダムで出ます"))) gm.DebugSkipTimer();
                using (new EditorGUI.DisabledScope(!gm.DebugInTransition))
                    if (GUILayout.Button("Next待ちを飛ばす")) gm.DebugSkipTransition();
                bool paused = GUILayout.Toggle(gm.DebugTimerPaused, "タイマー停止", "Button");
                if (paused != gm.DebugTimerPaused) gm.DebugSetTimerPaused(paused);
            }
            else
            {
                using (new EditorGUI.DisabledScope(room.playerComponents.Count >= GameManager.MAX_PLAYERS_LIMIT))
                    if (GUILayout.Button("Botを追加"))
                    {
                        GameObject prefab = ui != null ? ui.DebugBotPrefab : null;
                        if (prefab == null) prefab = NetworkManager.singleton.playerPrefab;
                        if (room.playerComponents.Count >= gm.MaxPlayers)
                            gm.UpdateSettings(gm.CARDCOUNT, gm.ScoringMode, room.playerComponents.Count + 1, gm.RoundTimeLimit);
                        room.AddBotPlayer(prefab);
                    }
                if (GUILayout.Button("全員Readyにして開始"))
                {
                    foreach (var p in room.playerComponents) if (p != null) p.DebugSetReady(true);
                    if (room.AllPlayersReady()) gm.StartGame();
                    else ShowNotification(new GUIContent("2人以上必要です"));
                }
            }
        }

        for (int i = 0; i < room.playerComponents.Count; i++)
            DrawSeat(room, gm, i, bots, ui, local);

        EditorGUILayout.EndVertical();
    }

    void DrawSeat(Room room, GameManager gm, int i, NineDebugBots bots, UIEventsManager ui, Player local)
    {
        var p = room.playerComponents[i];
        int score = i < gm.roundWins.Count ? gm.roundWins[i] : 0;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (p == null)
            {
                bool botSeat = room.DebugSeatIsBot(i);
                EditorGUILayout.LabelField("  席" + i + "  空席(" + (botSeat ? "Bot" : "人") + "が退出中)   " + score + "pt", GUILayout.MinWidth(220));
                var awayBot = room.DebugFindAwayBot(i, bots.AwayBots(room));
                if (awayBot != null)
                {
                    int rr = bots.ReturnAfterRound(awayBot);
                    if (rr >= 0) GUILayout.Label("R" + rr + "終了後に戻る", EditorStyles.miniLabel, GUILayout.Width(100));
                    if (GUILayout.Button("戻す", GUILayout.Width(50))) bots.Rejoin(awayBot);
                }
                return;
            }

            bool isBot = Room.IsBot(p);
            bool isTarget = ui != null && ui.DebugTargetPlayer == p;
            string who = p == local ? "自分" : isBot ? "Bot" : "人";

            string state;
            if (!gm.inProgress) state = p.isReadyToStart ? "Ready" : "-";
            else
            {
                int tc = gm.DebugTurnCard(i);
                state = tc > 0 ? "確定 " + tc : p.pendingSelection >= 0 ? "選択中 " + (p.pendingSelection + 1) : "未選択";
                if (gm.DebugInTransition) state = p.isReadyForNextRound ? "Next済" : "Next待ち";
            }
            int left = 0;
            foreach (var u in p.used) if (!u) left++;

            string head = (isTarget ? "▶" : "  ") + "席" + i + " " + who;
            EditorGUILayout.LabelField(head, GUILayout.Width(70));
            EditorGUILayout.LabelField(state, GUILayout.Width(80));
            EditorGUILayout.LabelField(score + "pt  残" + left, GUILayout.Width(80));

            if (GUILayout.Button("操作", GUILayout.Width(40)) && ui != null) ui.DebugSetTargetPlayer(p);
            using (new EditorGUI.DisabledScope(!gm.inProgress || !gm.DebugAcceptingCards))
            {
                if (GUILayout.Button("出す", GUILayout.Width(40))) bots.PlayNow(p, isBot ? bots.SettingsFor(p).mode : NineBotMode.Random);
                if (GUILayout.Button("取消", GUILayout.Width(40))) p.DebugCancelCard();
            }

            if (isBot)
            {
                bool manual = GUILayout.Toggle(bots.IsManual(p), new GUIContent("手動", "オンなら自動で動かさず、この席の画面に切り替えて自分で操作します"), GUILayout.Width(46));
                if (manual != bots.IsManual(p)) bots.SetManual(p, manual);
                var s = bots.SettingsFor(p);
                var m = (NineBotMode)EditorGUILayout.EnumPopup(s.mode, GUILayout.Width(80));
                if (m != s.mode)
                {
                    var o = s.Clone();
                    o.mode = m;
                    bots.SetOverride(p, o);
                }
                if (bots.HasOverride(p) && GUILayout.Button(new GUIContent("既定", "このBotの個別設定をやめて既定に戻す"), GUILayout.Width(40)))
                    bots.SetOverride(p, null);
                if (GUILayout.Button(new GUIContent("退出", gm.inProgress ? "空席になり、ランダム提出に切り替わります" : "席ごと取り除きます"), GUILayout.Width(40)))
                {
                    bots.Leave(p, ReturnAfter);
                    GUIUtility.ExitGUI();
                }
                GUILayout.Label(bots.LastAction(p), EditorStyles.miniLabel);
            }
        }
    }

    // ───────── ログ ─────────
    void DrawLog()
    {
        foldLog = EditorGUILayout.BeginFoldoutHeaderGroup(foldLog, "ゲームのログ(新しい順)");
        if (foldLog)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                logFilter = EditorGUILayout.TextField(logFilter, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("コピー", GUILayout.Width(60)))
                    EditorGUIUtility.systemCopyBuffer = string.Join("\n", NineDebugLog.Lines);
                if (GUILayout.Button("クリア", GUILayout.Width(60))) NineDebugLog.Clear();
            }
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(220));
            var lines = NineDebugLog.Lines;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(logFilter) && lines[i].IndexOf(logFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                EditorGUILayout.LabelField(lines[i], EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField("公開前の確定カードも記録します(サーバー内だけ。クライアントには送りません)", EditorStyles.centeredGreyMiniLabel);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }
}

// Play開始時: ボットを用意して設定を反映し、必要ならクイックスタートする
[InitializeOnLoad]
static class NineDebugPlayHook
{
    const string OnceKey = "nine.debug.qsOnce";

    static NineDebugPlayHook()
    {
        EditorApplication.playModeStateChanged += OnChanged;
    }

    public static void RequestQuickStartOnce() => SessionState.SetBool(OnceKey, true);

    static void OnChanged(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredPlayMode)
        {
            NineDebugLog.Clear();
            NineDebugWindow.ApplyPrefs(NineDebugBots.Instance);
            bool once = SessionState.GetBool(OnceKey, false);
            SessionState.SetBool(OnceKey, false);
            if (once || NineDebugWindow.QsOnPlay) NineDebugWindow.RunQuickStart();
        }
        else if (s == PlayModeStateChange.ExitingPlayMode)
        {
            SessionState.SetBool(OnceKey, false);
        }
    }
}
