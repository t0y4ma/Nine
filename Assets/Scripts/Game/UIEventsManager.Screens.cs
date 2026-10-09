using System.Collections.Generic;
using System.Linq;
using Mirror;
using Nine.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 土台(キャンバス・画面の切り替え)と、タイトル/部屋一覧・ルームの画面。
public partial class UIEventsManager
{
    Canvas canvas;
    RectTransform canvasRt, screensRoot, overlayRoot;
    CanvasScaler scaler;
    bool portrait;

    // ================= 土台 =================
    void BuildUi()
    {
        var go = new GameObject("NineCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.layer = 5;
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // リニア色空間でも、指定した色(sRGB)のまま表示する(既定のfalseだと全体が白っぽく浮く)
        canvas.vertexColorAlwaysGammaSpace = true;
        scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100;
        canvasRt = (RectTransform)go.transform;

        var bg = NineUi.Flat("Background", canvasRt, Color.white);
        bg.sprite = NineUi.Gradient;
        NineUi.Stretch(bg.rectTransform);

        screensRoot = NineUi.Stretch(NineUi.Rect("Screens", canvasRt));
        overlayRoot = NineUi.Stretch(NineUi.Rect("Overlays", canvasRt));

        BuildTitle();
        BuildRoom();
        BuildGame();
        BuildOverlays();
        EnsureEventSystem();
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
    }

    void UpdateScalerOrientation()
    {
        bool p = Screen.height > Screen.width;
        var want = p ? new Vector2(1080, 1920) : new Vector2(1920, 1080);
        if (scaler.referenceResolution != want)
        {
            scaler.referenceResolution = want;
            layoutDirty = true;
            dirty = true;
        }
        if (portrait != p) { portrait = p; layoutDirty = true; dirty = true; }
    }

    float W { get { return canvasRt.rect.width; } }
    float H { get { return canvasRt.rect.height; } }

    void Render()
    {
        var local = LocalPlayer;
        var viewer = GetDebugOrLocalPlayer();
        bool inRoom = local != null && local.inRoom;
        var gm = inRoom ? local.gameManager : null;
        bool inGame = inRoom && gm != null && gm.inProgress;

        if (!inRoom)
        {
            if (gameOverOpen || historyOpen || leaveConfirmOpen) layoutDirty = true;
            gameOverOpen = false; historyOpen = false; leaveConfirmOpen = false;
        }
        if (inGame && !wasInGame) OnEnterGame(gm);
        if (!inGame && wasInGame) OnLeaveGame();
        wasInGame = inGame;

        var next = !inRoom ? View.Title : (inGame || gameOverOpen) ? View.Game : View.Room;
        if (next != view) { view = next; layoutDirty = true; roomListDirty = true; }

        CollectMembers(gm);

        NineUi.Show(title.root, view == View.Title);
        NineUi.Show(room.root, view == View.Room);
        NineUi.Show(game.root, view == View.Game);

        switch (view)
        {
            case View.Title: RenderTitle(); break;
            case View.Room: RenderRoom(local, viewer, gm); break;
            case View.Game: RenderGame(local, viewer, gm, inGame); break;
        }
        RenderOverlays(local, viewer, gm);
    }

    void OnEnterGame(GameManager gm)
    {
        // 新しいゲームが始まった: 前のゲームの表示用の値を捨てる
        revealedOverride = null;
        scoresOverride = null;
        spectatorPicks = null;
        selectedIdx = -1;
        roundResolved = false;
        transition = -1f;
        nextPressedBy.Clear();
        lastWinners = null;
        gameOverOpen = false;
        resultOpen = false;
        historyDirty = true;
        layoutDirty = true;
        if (gm.ScoringMode != GameManager.SCORING_POINT_CARDS) pointKnown = false;
        RefreshPointCardFromGame(gm);
    }

    void OnLeaveGame()
    {
        timerVisible = false;
        transition = -1f;
        layoutDirty = true;
    }

    // ================= 共通の小物 =================
    static TextMeshProUGUI FieldLabel(Transform parent, string text)
    {
        var t = NineUi.Text("Label", parent, text, NineTheme.SizeSmall, NineTheme.Muted, TextAlignmentOptions.BottomLeft, true);
        t.characterSpacing = 2;
        return t;
    }

    static RectTransform Card(string name, Transform parent)
    {
        var c = NineUi.Panel(name, parent, NineTheme.Surface, 24);
        NineUi.Outline(c.transform, NineTheme.Line, 24);
        return c.rectTransform;
    }

    static void RightMid(RectTransform rt, float right, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-right, 0);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void LeftMid(RectTransform rt, float left, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(left, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static RectTransform ChipRt(TextMeshProUGUI chipLabel) { return (RectTransform)chipLabel.transform.parent; }

    static Button RowButton(Image row, System.Action onClick)
    {
        var b = row.gameObject.AddComponent<Button>();
        b.targetGraphic = row;
        var c = b.colors;
        c.normalColor = new Color(0.86f, 0.86f, 0.86f);
        c.selectedColor = c.normalColor;
        c.highlightedColor = new Color(1f, 1f, 1f);
        c.pressedColor = new Color(0.75f, 0.75f, 0.75f);
        c.colorMultiplier = 1.163f;
        c.fadeDuration = 0.06f;
        b.colors = c;
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        if (onClick != null) b.onClick.AddListener(() => onClick());
        return b;
    }

    // ================= タイトル / 部屋一覧 =================
    class TitleView
    {
        public RectTransform root, hero, left, right, listContent, scrollRt;
        public NineCard[] fan = new NineCard[3];
        public TextMeshProUGUI logo, tagline, leftTitle, nameLabel, addrLabel, status, idLabel, pwLabel, hint, listTitle, listCount, listEmpty;
        public TMP_InputField name, address, roomId, password;
        public NineButton host, connect, server, create, join;
        public ScrollRect scroll;
    }
    TitleView title = new TitleView();

    void BuildTitle()
    {
        var t = title;
        t.root = NineUi.Stretch(NineUi.Rect("Title", screensRoot));

        t.hero = NineUi.Rect("Hero", t.root);
        int[] fanValues = { 3, 9, 7 };
        for (int i = 0; i < 3; i++)
        {
            var c = NineCard.Create("Fan" + i, t.hero);
            if (i == 1) c.SetSelected(fanValues[i]); else c.SetFace(fanValues[i]);
            t.fan[i] = c;
        }
        t.logo = NineUi.Text("Logo", t.hero, "Nine", 150, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        t.logo.characterSpacing = 6;
        t.logo.overflowMode = TextOverflowModes.Overflow;
        t.tagline = NineUi.Text("Tagline", t.hero, "同時に1枚。数字で読み合うカードゲーム", 30, NineTheme.Muted, TextAlignmentOptions.MidlineLeft);

        // 左: はじめる
        t.left = Card("Start", t.root);
        t.leftTitle = NineUi.Text("Title", t.left, "はじめる", NineTheme.SizeTitle, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        t.nameLabel = FieldLabel(t.left, "名前");
        t.name = NineUi.Input("Name", t.left, "名前を入力(" + Player.NAME_MAX + "文字まで)");
        t.name.characterLimit = Player.NAME_MAX;
        t.name.text = PlayerName;
        t.name.onEndEdit.AddListener(v => SaveName(v));

        t.addrLabel = FieldLabel(t.left, "接続先");
        t.address = NineUi.Input("Address", t.left, "localhost");
        t.host = NineUi.Button("Host", t.left, "ホストで始める", ButtonHost, BtnStyle.Primary);
        t.connect = NineUi.Button("Connect", t.left, "接続する", ButtonConnect, BtnStyle.Secondary);
        t.server = NineUi.Button("Server", t.left, "サーバーとして起動", ButtonServer, BtnStyle.Ghost, NineTheme.SizeSmall);
        t.status = NineUi.Text("Status", t.left, "", NineTheme.SizeBody, NineTheme.Muted, TextAlignmentOptions.TopLeft);
        t.status.textWrappingMode = TextWrappingModes.Normal;

        t.idLabel = FieldLabel(t.left, "部屋ID");
        t.roomId = NineUi.Input("RoomId", t.left, "空欄なら4桁の番号で作ります");
        t.roomId.characterLimit = RoomManager.ROOM_ID_MAX;
        t.pwLabel = FieldLabel(t.left, "パスワード(任意)");
        t.password = NineUi.Input("Password", t.left, "なし");
        t.password.contentType = TMP_InputField.ContentType.Password;
        t.hint = NineUi.Text("Hint", t.left, "作った部屋には自動で入ります。", NineTheme.SizeSmall, NineTheme.Faint, TextAlignmentOptions.MidlineLeft);
        t.create = NineUi.Button("Create", t.left, "部屋を作る", ButtonCreateRoom, BtnStyle.Primary, 28);
        t.join = NineUi.Button("Join", t.left, "入室する", ButtonJoinRoom, BtnStyle.Secondary, 28);

        // 右: 部屋一覧
        t.right = Card("Rooms", t.root);
        t.listTitle = NineUi.Text("Title", t.right, "部屋一覧", NineTheme.SizeTitle, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        t.listCount = NineUi.Chip("Count", t.right, "0部屋", NineTheme.Muted);
        t.listContent = NineUi.ScrollArea("List", t.right, out t.scroll, 12);
        t.scrollRt = (RectTransform)t.scroll.transform;
        t.listEmpty = NineUi.Text("Empty", t.right, "", NineTheme.SizeBody, NineTheme.Faint, TextAlignmentOptions.Center);
        t.listEmpty.textWrappingMode = TextWrappingModes.Normal;
    }

    void RenderTitle()
    {
        var t = title;
        bool connected = NetworkClient.isConnected;
        bool connecting = NetworkClient.active && !connected;
        bool hosting = IsHostingSupported();
        bool serverOnly = NetworkServer.active && !NetworkClient.active;

        // 接続前の部品
        bool showConnect = !connected;
        NineUi.Show(t.addrLabel, showConnect && hosting);
        NineUi.Show(t.address, showConnect && hosting);
        NineUi.Show(t.host, showConnect && hosting);
        NineUi.Show(t.connect, showConnect && hosting);
        NineUi.Show(t.server, showConnect && IsServerModeAllowed());
        NineUi.Show(t.status, showConnect);
        t.host.Interactable = !NetworkClient.active && !NetworkServer.active;
        t.connect.Interactable = !NetworkClient.active && !NetworkServer.active;
        t.server.Interactable = !NetworkServer.active;
        if (!string.IsNullOrEmpty(connectError)) { t.status.text = connectError; t.status.color = NineTheme.Danger; }
        else if (serverOnly) { t.status.text = "サーバーとして動いています。"; t.status.color = NineTheme.Go; }
        else if (connecting || !hosting) { t.status.text = "サーバーに接続しています…"; t.status.color = NineTheme.Muted; }
        else { t.status.text = "自分がホストになって遊ぶか、接続先のサーバーに接続してください。"; t.status.color = NineTheme.Muted; }

        // 接続後の部品(部屋を作る・入る)
        bool showRoom = connected;
        NineUi.Show(t.idLabel, showRoom);
        NineUi.Show(t.roomId, showRoom);
        NineUi.Show(t.pwLabel, showRoom);
        NineUi.Show(t.password, showRoom);
        NineUi.Show(t.hint, showRoom);
        NineUi.Show(t.create, showRoom);
        NineUi.Show(t.join, connected);
        t.create.Interactable = connected;

        if (roomListDirty) { roomListDirty = false; RebuildRoomList(); }
        if (layoutDirty) LayoutTitle();
    }

    void LayoutTitle()
    {
        var t = title;
        float w = W, h = H;
        float m = portrait ? 40 : 56;
        float cw = Mathf.Min(w - 2 * m, portrait ? 1000 : 1600);
        float cx = (w - cw) / 2;

        float heroTop = portrait ? 64 : 34;
        float heroH = portrait ? 440 : 220;
        NineUi.TL(t.hero, cx, heroTop, cw, heroH);
        float cardW = portrait ? 120 : 104, cardH = cardW * 1.4f;
        if (portrait)
        {
            float fx = cw / 2, fy = 120;
            for (int i = 0; i < 3; i++) PlaceFan(t.fan[i], fx + (i - 1) * cardW * 0.78f, fy + (i == 1 ? -12 : 10), cardW, cardH, (1 - i) * 13f);
            NineUi.TL(t.logo.rectTransform, 0, 232, cw, 150);
            t.logo.alignment = TextAlignmentOptions.Center;
            NineUi.TL(t.tagline.rectTransform, 0, 392, cw, 44);
            t.tagline.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            float groupW = 300 + 40 + 700;
            float gx = (cw - groupW) / 2;
            float fx = gx + 150, fy = heroH / 2 + 4;
            for (int i = 0; i < 3; i++) PlaceFan(t.fan[i], fx + (i - 1) * cardW * 0.78f, fy + (i == 1 ? -12 : 10), cardW, cardH, (1 - i) * 13f);
            NineUi.TL(t.logo.rectTransform, gx + 340, heroH / 2 - 100, 700, 140);
            t.logo.alignment = TextAlignmentOptions.MidlineLeft;
            NineUi.TL(t.tagline.rectTransform, gx + 346, heroH / 2 + 44, 700, 44);
            t.tagline.alignment = TextAlignmentOptions.MidlineLeft;
        }

        float top = heroTop + heroH + (portrait ? 30 : 22);
        if (!portrait)
        {
            float lw = Mathf.Round(cw * 0.40f);
            float ch = h - top - m;
            NineUi.TL(t.left, cx, top, lw, ch);
            NineUi.TL(t.right, cx + lw + 28, top, cw - lw - 28, ch);
            LayoutStartCard(lw);
            LayoutRoomListCard(cw - lw - 28, ch);
        }
        else
        {
            float lh = LayoutStartCard(cw);
            NineUi.TL(t.left, cx, top, cw, lh);
            float rt = top + lh + 28;
            float rh = Mathf.Max(300, h - rt - m);
            NineUi.TL(t.right, cx, rt, cw, rh);
            LayoutRoomListCard(cw, rh);
        }
    }

    static void PlaceFan(NineCard c, float x, float y, float w, float h, float angle)
    {
        var rt = c.Rt;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, -y);
        c.Layout(w, h);
        rt.localRotation = Quaternion.Euler(0, 0, angle);
    }

    float LayoutStartCard(float w)
    {
        var t = title;
        float pad = 36, iw = w - pad * 2, y = pad;
        NineUi.TL(t.leftTitle.rectTransform, pad, y, iw, 52); y += 52 + 18;
        NineUi.TL(t.nameLabel.rectTransform, pad, y, iw, 30); y += 36;
        NineUi.TL((RectTransform)t.name.transform, pad, y, iw, 68); y += 68 + 26;

        bool connected = NetworkClient.isConnected;
        bool hosting = IsHostingSupported();
        float bw = (iw - 16) / 2;
        if (!connected)
        {
            if (hosting)
            {
                NineUi.TL(t.addrLabel.rectTransform, pad, y, iw, 30); y += 36;
                NineUi.TL((RectTransform)t.address.transform, pad, y, iw, 68); y += 68 + 20;
                NineUi.TL(t.host.Rt, pad, y, bw, 76);
                NineUi.TL(t.connect.Rt, pad + bw + 16, y, bw, 76); y += 76 + 14;
            }
            if (IsServerModeAllowed()) { NineUi.TL(t.server.Rt, pad, y, iw, 56); y += 56 + 14; }
            NineUi.TL(t.status.rectTransform, pad, y, iw, 76); y += 76 + 16;
        }
        if (connected)
        {
            {
                NineUi.TL(t.idLabel.rectTransform, pad, y, iw, 30); y += 36;
                NineUi.TL((RectTransform)t.roomId.transform, pad, y, iw, 68); y += 68 + 18;
                NineUi.TL(t.pwLabel.rectTransform, pad, y, iw, 30); y += 36;
                NineUi.TL((RectTransform)t.password.transform, pad, y, iw, 68); y += 68 + 10;
                NineUi.TL(t.hint.rectTransform, pad, y, iw, 32); y += 32 + 18;
                NineUi.TL(t.create.Rt, pad, y, bw, 80);
                NineUi.TL(t.join.Rt, pad + bw + 16, y, bw, 80); y += 80;
            }
        }
        return y + pad;
    }

    void LayoutRoomListCard(float w, float h)
    {
        var t = title;
        NineUi.TL(t.listTitle.rectTransform, 36, 30, w - 260, 52);
        var chip = ChipRt(t.listCount);
        chip.anchorMin = chip.anchorMax = new Vector2(1, 1);
        chip.pivot = new Vector2(1, 0.5f);
        chip.anchoredPosition = new Vector2(-36, -56);
        chip.sizeDelta = new Vector2(130, 40);
        NineUi.TL(t.scrollRt, 24, 104, w - 48, h - 128);
        NineUi.TL(t.listEmpty.rectTransform, 60, 104, w - 120, Mathf.Min(260, h - 128));
    }

    void RebuildRoomList()
    {
        var t = title;
        NineUi.Clear(t.listContent);
        bool connected = NetworkClient.isConnected;
        int count = 0;
        var rm = RM;
        if (connected && rm != null)
        {
            foreach (var kv in rm.roomNames.OrderBy(k => k.Key))
            {
                BuildRoomRow(kv.Key, kv.Value);
                count++;
            }
        }
        NineUi.SetChip(t.listCount, count + "部屋", NineTheme.Muted);
        t.listEmpty.text = connected ? "まだ部屋がありません。\n" + (portrait ? "上" : "左") + "のフォームから作れます。" : "サーバーに接続すると、\nここに部屋の一覧が出ます。";
        NineUi.Show(t.listEmpty, count == 0);
    }

    void BuildRoomRow(string id, string summary)
    {
        int players = 0, max = 0, spec = 0;
        bool playing = false, locked = false;
        var parts = (summary ?? "").Split('|');
        if (parts.Length >= 5)
        {
            int.TryParse(parts[0], out players);
            int.TryParse(parts[1], out max);
            int.TryParse(parts[2], out spec);
            playing = parts[3] == "1";
            locked = parts[4] == "1";
        }

        var row = NineUi.Panel("Room_" + id, title.listContent, NineTheme.Raised, 16, raycast: true);
        NineUi.Height(row, 100);
        RowButton(row, () =>
        {
            title.roomId.text = id;
            if (locked) title.password.Select();
        });

        var idText = NineUi.Text("Id", row.transform, id, 34, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        LeftMid(idText.rectTransform, 28, 16, 420, 46);
        string sub = max > 0 ? "参加 " + players + "/" + max + (spec > 0 ? "   観戦 " + spec : "") : "";
        var subText = NineUi.Text("Sub", row.transform, sub, NineTheme.SizeSmall, NineTheme.Muted, TextAlignmentOptions.MidlineLeft);
        LeftMid(subText.rectTransform, 28, -22, 420, 32);

        var join = NineUi.Button("Join", row.transform, "入る", () => JoinRoom(id), BtnStyle.Secondary, NineTheme.SizeBody);
        RightMid(join.Rt, 20, 132, 60);
        var state = NineUi.Chip("State", row.transform, playing ? "対局中" : "待機中", playing ? NineTheme.Warn : NineTheme.Go);
        RightMid(ChipRt(state), 168, 112, 40);
        if (locked)
        {
            var lk = NineUi.Chip("Lock", row.transform, "鍵付き", NineTheme.Muted);
            RightMid(ChipRt(lk), 292, 112, 40);
        }
    }

    // ================= ルーム(対局前の待合) =================
    class RoomView
    {
        public RectTransform root, top, members, rules, bar, memberList, memberScrollRt;
        public Image topLine, barLine;
        public TextMeshProUGUI roomCaption, roomId, lockChip, membersTitle, membersCount, rulesTitle, rulesNote,
            modeLabel, modeDesc, cardsLabel, cardsValue, maxLabel, maxValue, timeLabel, timeValue, status;
        public NineButton leave, spectate, ready, start, cardsMinus, cardsPlus, maxMinus, maxPlus;
        public NineButton[] modes = new NineButton[3];
        public Slider time;
        public ScrollRect memberScroll;
        public string memberSig = "";
    }
    RoomView room = new RoomView();

    void BuildRoom()
    {
        var r = room;
        r.root = NineUi.Stretch(NineUi.Rect("Room", screensRoot));

        r.top = NineUi.Flat("TopBar", r.root, NineTheme.A(NineTheme.Surface, 0.92f)).rectTransform;
        r.topLine = NineUi.Flat("Line", r.top, NineTheme.Line);
        r.roomCaption = NineUi.Text("Caption", r.top, "ルーム", NineTheme.SizeSmall, NineTheme.Muted, TextAlignmentOptions.MidlineLeft, true);
        r.roomId = NineUi.Text("RoomId", r.top, "", 44, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        r.lockChip = NineUi.Chip("Lock", r.top, "鍵付き", NineTheme.Muted);
        r.leave = NineUi.Button("Leave", r.top, "退出", ButtonLeaveRoom, BtnStyle.Danger);

        r.members = Card("Members", r.root);
        r.membersTitle = NineUi.Text("Title", r.members, "メンバー", NineTheme.SizeTitle, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        r.membersCount = NineUi.Chip("Count", r.members, "", NineTheme.Accent);
        r.memberList = NineUi.ScrollArea("List", r.members, out r.memberScroll, 10);
        r.memberScrollRt = (RectTransform)r.memberScroll.transform;

        r.rules = Card("Rules", r.root);
        r.rulesTitle = NineUi.Text("Title", r.rules, "ルール", NineTheme.SizeTitle, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        r.rulesNote = NineUi.Text("Note", r.rules, "", NineTheme.SizeSmall, NineTheme.Faint, TextAlignmentOptions.MidlineLeft);
        r.modeLabel = FieldLabel(r.rules, "得点方式");
        for (int i = 0; i < 3; i++)
        {
            int m = i;
            r.modes[i] = NineUi.Button("Mode" + i, r.rules, ModeName(i), () => SelectMode(m), BtnStyle.Quiet, NineTheme.SizeBody, 12);
        }
        r.modeDesc = NineUi.Text("ModeDesc", r.rules, "", NineTheme.SizeSmall, NineTheme.Muted, TextAlignmentOptions.TopLeft);
        r.modeDesc.textWrappingMode = TextWrappingModes.Normal;
        r.modeDesc.lineSpacing = 8;

        r.cardsLabel = NineUi.Text("CardsLabel", r.rules, "カードの枚数", NineTheme.SizeBody, NineTheme.Text, TextAlignmentOptions.MidlineLeft);
        r.cardsMinus = NineUi.Button("CardsMinus", r.rules, "−", () => StepCards(-1), BtnStyle.Secondary, 32, 12);
        r.cardsValue = NineUi.Text("CardsValue", r.rules, "", 34, NineTheme.Text, TextAlignmentOptions.Center, true);
        r.cardsPlus = NineUi.Button("CardsPlus", r.rules, "+", () => StepCards(1), BtnStyle.Secondary, 32, 12);

        r.maxLabel = NineUi.Text("MaxLabel", r.rules, "参加人数の上限", NineTheme.SizeBody, NineTheme.Text, TextAlignmentOptions.MidlineLeft);
        r.maxMinus = NineUi.Button("MaxMinus", r.rules, "−", () => StepMax(-1), BtnStyle.Secondary, 32, 12);
        r.maxValue = NineUi.Text("MaxValue", r.rules, "", 34, NineTheme.Text, TextAlignmentOptions.Center, true);
        r.maxPlus = NineUi.Button("MaxPlus", r.rules, "+", () => StepMax(1), BtnStyle.Secondary, 32, 12);

        r.timeLabel = NineUi.Text("TimeLabel", r.rules, "1回の持ち時間", NineTheme.SizeBody, NineTheme.Text, TextAlignmentOptions.MidlineLeft);
        r.timeValue = NineUi.Text("TimeValue", r.rules, "", 30, NineTheme.Text, TextAlignmentOptions.MidlineRight, true);
        r.time = NineUi.Slider("Time", r.rules, NineTheme.Accent);
        r.time.minValue = 0;
        r.time.maxValue = TIME_STEPS;
        r.time.wholeNumbers = true;
        r.time.onValueChanged.AddListener(OnTimeSlider);

        r.bar = NineUi.Flat("ActionBar", r.root, NineTheme.A(NineTheme.Surface, 0.94f)).rectTransform;
        r.barLine = NineUi.Flat("Line", r.bar, NineTheme.Line);
        r.status = NineUi.Text("Status", r.bar, "", NineTheme.SizeBody, NineTheme.Muted, TextAlignmentOptions.MidlineLeft);
        r.spectate = NineUi.Button("Spectate", r.bar, "観戦する", ButtonToggleSpectate, BtnStyle.Secondary, 28);
        r.ready = NineUi.Button("Ready", r.bar, "準備OK", ButtonReady, BtnStyle.Go, 30);
        r.start = NineUi.Button("Start", r.bar, "ゲームを始める", ButtonStartGame, BtnStyle.Primary, 30);
    }

    void RenderRoom(Player local, Player viewer, GameManager gm)
    {
        var r = room;
        if (gm == null) return;
        bool host = local.isRoomHost;
        bool spectating = local.isSpectator;

        // 上部
        r.roomId.text = local.myRoomId;
        bool locked = false;
        string summary;
        if (RM != null && RM.roomNames.TryGetValue(local.myRoomId ?? "", out summary))
        {
            var parts = summary.Split('|');
            locked = parts.Length >= 5 && parts[4] == "1";
        }
        NineUi.Show(ChipRt(r.lockChip), locked);

        // メンバー
        int present = 0, readyN = 0, spec = 0;
        for (int i = 0; i < seatPlayers.Length; i++) if (seatPlayers[i] != null) { present++; if (seatPlayers[i].isReadyToStart) readyN++; }
        foreach (var p in members) if (p.isSpectator) spec++;
        int max = gm.MaxPlayers;
        NineUi.SetChip(r.membersCount, "参加 " + present + " / " + max, present > max ? NineTheme.Danger : NineTheme.Accent);

        var sig = new System.Text.StringBuilder();
        sig.Append(host).Append(';').Append(max).Append(';').Append(seatPlayers.Length).Append(';').Append(viewer != null ? viewer.netId : 0).Append(';');
        foreach (var p in members)
            sig.Append(p.netId).Append(',').Append(p.playerId).Append(',').Append(p.playerName).Append(',').Append(p.isSpectator)
               .Append(',').Append(p.isRoomHost).Append(',').Append(p.isReadyToStart).Append('|');
        string s = sig.ToString();
        if (s != r.memberSig || layoutDirty) { r.memberSig = s; RebuildMembers(local, viewer, gm); }

        // ルール
        int mode = EffMode(gm), cards = EffCards(gm), maxP = EffMax(gm), time = EffTime(gm);
        for (int i = 0; i < 3; i++)
        {
            r.modes[i].SetStyle(i == mode ? BtnStyle.Primary : BtnStyle.Quiet);
            r.modes[i].Interactable = host || i == mode;
        }
        r.modeDesc.text = ModeDescription(mode);
        r.cardsValue.text = cards + "<size=60%> 枚</size>";
        r.maxValue.text = maxP + "<size=60%> 人</size>";
        r.cardsMinus.Interactable = host && cards > 3;
        r.cardsPlus.Interactable = host && cards < GameManager.CARD_COUNT_LIMIT;
        r.maxMinus.Interactable = host && maxP > 2;
        r.maxPlus.Interactable = host && maxP < GameManager.MAX_PLAYERS_LIMIT;
        r.timeValue.text = TimeLabel(time);
        suppressSlider = true;
        if (timeSliderSendAt < 0f) r.time.value = SecondsToSlider(time);
        suppressSlider = false;
        r.time.interactable = host;
        r.rulesNote.text = host ? "変えるとすぐ全員に反映されます" : "ルールはホストだけが変えられます";

        // 下部の操作
        r.spectate.Text = spectating ? "参加する" : "観戦する";
        var readyTarget = viewer != null ? viewer : local;
        bool showReady = readyTarget != null && !readyTarget.isSpectator;
        if (r.ready.gameObject.activeSelf != showReady) layoutDirty = true;
        NineUi.Show(r.ready, showReady);
        if (showReady)
        {
            bool isReady = readyTarget.isReadyToStart;
            r.ready.Text = isReady ? "準備を取り消す" : "準備OK";
            r.ready.SetStyle(isReady ? BtnStyle.Secondary : BtnStyle.Go);
        }
        if (r.start.gameObject.activeSelf != host) layoutDirty = true;
        NineUi.Show(r.start, host);
        bool canStart = present >= 2 && readyN == present && present <= max;
        r.start.SetStyle(canStart ? BtnStyle.Primary : BtnStyle.Secondary);

        string line = "準備 <b><color=" + NineTheme.ToHex(readyN == present && present > 0 ? NineTheme.Go : NineTheme.Text) + ">" + readyN + " / " + present + "</color></b>";
        line += "　　参加 <b><color=" + NineTheme.ToHex(present > max ? NineTheme.Danger : NineTheme.Text) + ">" + present + " / " + max + "</color></b>";
        if (spec > 0) line += "　　観戦 <b><color=" + NineTheme.ToHex(NineTheme.Text) + ">" + spec + "</color></b>";
        if (!host) line += "\n<size=85%>ホストが始めるのを待っています</size>";
        else if (!canStart) line += "\n<size=85%>" + (present < 2 ? "参加者が2人以上必要です" : present > max ? "上限を超えています" : "全員の準備を待っています") + "</size>";
        r.status.text = line;

        if (layoutDirty) LayoutRoom(host);
    }

    void RebuildMembers(Player local, Player viewer, GameManager gm)
    {
        var r = room;
        NineUi.Clear(r.memberList);
        bool host = local.isRoomHost;
        int rows = Mathf.Max(seatPlayers.Length, gm.MaxPlayers);
        for (int seat = 0; seat < rows; seat++)
        {
            var p = SeatPlayer(seat);
            if (p != null) BuildMemberRow(p, seat, local, viewer, host);
            else if (seat < seatPlayers.Length) BuildEmptyRow(seat, "空席(戻ってくるのを待っています)");
            else BuildEmptyRow(seat, "空き");
        }
        var specs = members.Where(p => p.isSpectator).ToList();
        if (specs.Count > 0)
        {
            var head = NineUi.Text("SpecHead", r.memberList, "観戦  " + specs.Count + "人", NineTheme.SizeSmall, NineTheme.Muted, TextAlignmentOptions.BottomLeft, true);
            NineUi.Height(head, 52);
            foreach (var p in specs) BuildMemberRow(p, -1, local, viewer, host);
        }
    }

    void BuildMemberRow(Player p, int seat, Player local, Player viewer, bool host)
    {
        var row = NineUi.Panel("Member", room.memberList, p == viewer ? NineTheme.RaisedHover : NineTheme.Raised, 16);
        NineUi.Height(row, 84);
        if (p == viewer) NineUi.Outline(row.transform, NineTheme.A(NineTheme.Accent, 0.6f), 16);

        var col = NineTheme.SeatColor(seat);
        var badge = NineUi.Panel("Badge", row.transform, col, 12);
        LeftMid(badge.rectTransform, 16, 0, 60, 56);
        var bt = NineUi.Text("Text", badge.transform, seat >= 0 ? SeatTag(seat) : "観", 24, NineTheme.CardInk, TextAlignmentOptions.Center, true);
        NineUi.Stretch(bt.rectTransform);

        float right = 16;
        if (host && p != local)
        {
            var give = NineUi.Button("GiveHost", row.transform, "ホストを渡す", () => TransferHostTo(p), BtnStyle.Ghost, NineTheme.SizeSmall, 10);
            RightMid(give.Rt, right, 168, 52);
            right += 168 + 12;
        }
        if (seat >= 0)
        {
            var rc = NineUi.Chip("Ready", row.transform, p.isReadyToStart ? "準備OK" : "準備中", p.isReadyToStart ? NineTheme.Go : NineTheme.Faint);
            RightMid(ChipRt(rc), right, 116, 40);
            right += 116 + 10;
        }
        if (p.isRoomHost)
        {
            var hc = NineUi.Chip("Host", row.transform, "ホスト", NineTheme.Gold);
            RightMid(ChipRt(hc), right, 100, 40);
            right += 100 + 10;
        }

        string you = p == local ? "  <size=80%><color=" + NineTheme.ToHex(NineTheme.Accent) + ">あなた</color></size>" : "";
        if (p == viewer && p != local) you = "  <size=80%><color=" + NineTheme.ToHex(NineTheme.Warn) + ">操作中</color></size>";
        var name = NineUi.Text("Name", row.transform, NameOf(p, seat) + you, 30, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        name.overflowMode = TextOverflowModes.Ellipsis;
        var nrt = name.rectTransform;
        nrt.anchorMin = new Vector2(0, 0); nrt.anchorMax = new Vector2(1, 1);
        nrt.offsetMin = new Vector2(96, 0); nrt.offsetMax = new Vector2(-right, 0);
    }

    void BuildEmptyRow(int seat, string text)
    {
        var row = NineUi.Panel("Empty", room.memberList, NineTheme.A(NineTheme.Raised, 0.25f), 16);
        NineUi.Height(row, 62);
        NineUi.Outline(row.transform, NineTheme.Line, 16);
        var badge = NineUi.Panel("Badge", row.transform, NineTheme.A(NineTheme.SeatColor(seat), 0.12f), 12);
        LeftMid(badge.rectTransform, 16, 0, 60, 48);
        var bt = NineUi.Text("Text", badge.transform, SeatTag(seat), 22, NineTheme.A(NineTheme.SeatColor(seat), 0.7f), TextAlignmentOptions.Center, true);
        NineUi.Stretch(bt.rectTransform);
        var t = NineUi.Text("Text", row.transform, text, NineTheme.SizeBody, NineTheme.Faint, TextAlignmentOptions.MidlineLeft);
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(96, 0); rt.offsetMax = new Vector2(-16, 0);
    }

    void LayoutRoom(bool host)
    {
        var r = room;
        float w = W, h = H;
        float m = portrait ? 28 : 40;
        float topH = portrait ? 108 : 96;
        float barH = portrait ? 236 : 124;

        NineUi.TL(r.top, 0, 0, w, topH);
        NineUi.TL(r.topLine.rectTransform, 0, topH - 2, w, 2);
        NineUi.TL(r.roomCaption.rectTransform, m, topH / 2 - 38, 200, 28);
        NineUi.TL(r.roomId.rectTransform, m, topH / 2 - 12, 520, 52);
        var lk = ChipRt(r.lockChip);
        NineUi.TL(lk, m + Mathf.Min(520, r.roomId.preferredWidth) + 20, topH / 2 - 2, 108, 40);
        r.leave.Rt.anchorMin = r.leave.Rt.anchorMax = new Vector2(1, 0.5f);
        r.leave.Rt.pivot = new Vector2(1, 0.5f);
        r.leave.Rt.anchoredPosition = new Vector2(-m, 0);
        r.leave.Rt.sizeDelta = new Vector2(150, 62);

        NineUi.TL(r.bar, 0, h - barH, w, barH);
        NineUi.TL(r.barLine.rectTransform, 0, 0, w, 2);

        float y0 = topH + m, y1 = h - barH - m, bh = y1 - y0;
        float rw, rh;
        if (!portrait)
        {
            float mw = Mathf.Round((w - 2 * m - 28) * 0.5f);
            NineUi.TL(r.members, m, y0, mw, bh);
            NineUi.TL(r.rules, m + mw + 28, y0, w - 2 * m - 28 - mw, bh);
            LayoutMembers(mw, bh);
            rw = w - 2 * m - 28 - mw; rh = bh;
        }
        else
        {
            float mh = Mathf.Round(bh * 0.52f);
            NineUi.TL(r.members, m, y0, w - 2 * m, mh);
            NineUi.TL(r.rules, m, y0 + mh + 24, w - 2 * m, bh - mh - 24);
            LayoutMembers(w - 2 * m, mh);
            rw = w - 2 * m; rh = bh - mh - 24;
        }
        LayoutRules(rw, rh);

        // 下部の操作
        if (!portrait)
        {
            float bx = w - m;
            float bhgt = 80;
            if (host) { bx -= 300; NineUi.TL(r.start.Rt, bx, (barH - bhgt) / 2, 300, bhgt); bx -= 16; }
            if (r.ready.gameObject.activeSelf) { bx -= 250; NineUi.TL(r.ready.Rt, bx, (barH - bhgt) / 2, 250, bhgt); bx -= 16; }
            bx -= 200; NineUi.TL(r.spectate.Rt, bx, (barH - bhgt) / 2, 200, bhgt);
            NineUi.TL(r.status.rectTransform, m, 10, bx - m - 24, barH - 20);
        }
        else
        {
            NineUi.TL(r.status.rectTransform, m, 16, w - 2 * m, 84);
            float by = 112, bhgt = 92, gap = 16;
            bool showReady = r.ready.gameObject.activeSelf;
            int n = 1 + (host ? 1 : 0) + (showReady ? 1 : 0);
            float bw = (w - 2 * m - gap * (n - 1)) / n;
            float x = m;
            NineUi.TL(r.spectate.Rt, x, by, bw, bhgt); x += bw + gap;
            if (showReady) { NineUi.TL(r.ready.Rt, x, by, bw, bhgt); x += bw + gap; }
            if (host) NineUi.TL(r.start.Rt, x, by, bw, bhgt);
        }
    }

    void LayoutMembers(float w, float h)
    {
        var r = room;
        NineUi.TL(r.membersTitle.rectTransform, 32, 26, w - 260, 52);
        var chip = ChipRt(r.membersCount);
        chip.anchorMin = chip.anchorMax = new Vector2(1, 1);
        chip.pivot = new Vector2(1, 0.5f);
        chip.anchoredPosition = new Vector2(-32, -52);
        chip.sizeDelta = new Vector2(170, 42);
        NineUi.TL(r.memberScrollRt, 24, 96, w - 48, h - 120);
    }

    void LayoutRules(float w, float h)
    {
        var r = room;
        float pad = 32, iw = w - pad * 2, y = 26;
        NineUi.TL(r.rulesTitle.rectTransform, pad, y, 200, 52);
        NineUi.TL(r.rulesNote.rectTransform, pad + 140, y + 10, iw - 140, 40);
        r.rulesNote.alignment = TextAlignmentOptions.MidlineRight;
        y += 52 + 22;

        NineUi.TL(r.modeLabel.rectTransform, pad, y, iw, 30); y += 38;
        float mw = (iw - 24) / 3;
        for (int i = 0; i < 3; i++) NineUi.TL(r.modes[i].Rt, pad + i * (mw + 12), y, mw, 68);
        y += 68 + 14;
        float descH = Mathf.Clamp(h * 0.14f, 64, 110);
        NineUi.TL(r.modeDesc.rectTransform, pad + 4, y, iw - 8, descH); y += descH + 12;

        float rowH = 72, stepW = 64, valW = 120;
        foreach (var row in new[] { 0, 1 })
        {
            var label = row == 0 ? r.cardsLabel : r.maxLabel;
            var minus = row == 0 ? r.cardsMinus : r.maxMinus;
            var val = row == 0 ? r.cardsValue : r.maxValue;
            var plus = row == 0 ? r.cardsPlus : r.maxPlus;
            NineUi.TL(label.rectTransform, pad, y, iw - (stepW * 2 + valW), rowH);
            float x = pad + iw - (stepW * 2 + valW);
            NineUi.TL(minus.Rt, x, y + (rowH - stepW) / 2, stepW, stepW);
            NineUi.TL(val.rectTransform, x + stepW, y, valW, rowH);
            NineUi.TL(plus.Rt, x + stepW + valW, y + (rowH - stepW) / 2, stepW, stepW);
            y += rowH + 10;
        }
        NineUi.TL(r.timeLabel.rectTransform, pad, y, iw * 0.6f, 52);
        NineUi.TL(r.timeValue.rectTransform, pad + iw * 0.5f, y, iw * 0.5f, 52);
        y += 56;
        NineUi.TL((RectTransform)r.time.transform, pad, y, iw, 44);
    }
}
