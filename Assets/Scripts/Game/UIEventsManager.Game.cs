using System.Collections.Generic;
using System.Linq;
using Mirror;
using Nine.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 対局画面: 上部バー(何戦目・方式・状況・残り時間)、席の一覧(得点と使ったカード)、場(得点カードと出したカード)、手札。
public partial class UIEventsManager
{
    class SeatTile
    {
        public RectTransform rt, minis;
        public Image bg, stripe, ring, badge;
        public TextMeshProUGUI badgeText, name, score, state;
        public readonly List<NineCard> cards = new List<NineCard>();
    }

    class Slot
    {
        public RectTransform rt;
        public NineCard card;
        public TextMeshProUGUI label;
    }

    class GameView
    {
        public RectTransform root, top, seats, table, hand, handCards, actions, spectator, pointGroup;
        public Image topLine, timerTrack, timerFill, sep, nextFill;
        public TextMeshProUGUI round, mode, status, tableCaption, pointCaption, pointLeft, spectatorText, hint;
        public NineButton history, leave, confirm, next;
        public readonly List<SeatTile> tiles = new List<SeatTile>();
        public readonly List<Slot> slots = new List<Slot>();
        public readonly List<NineCard> handList = new List<NineCard>();
        public readonly List<NineCard> carried = new List<NineCard>();
        public NineCard point;
        public float[] handBaseY = new float[0];
        public float handLift;
        public int builtSeats = -1, builtCards = -1;
        public bool pointShown;
        public bool slotTwoLine;   // 場の名前を「P1」と名前の2行に分けるか(席の幅が狭いとき)
    }
    GameView game = new GameView();

    void BuildGame()
    {
        var g = game;
        g.root = NineUi.Stretch(NineUi.Rect("Game", screensRoot));

        // 上部バー
        g.top = NineUi.Flat("TopBar", g.root, NineTheme.A(NineTheme.Surface, 0.94f)).rectTransform;
        g.topLine = NineUi.Flat("Line", g.top, NineTheme.Line);
        g.round = NineUi.Text("Round", g.top, "", 40, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        g.mode = NineUi.Chip("Mode", g.top, "", NineTheme.Accent);
        g.status = NineUi.Text("Status", g.top, "", NineTheme.SizeBody, NineTheme.Muted, TextAlignmentOptions.Center);
        g.history = NineUi.Button("History", g.top, "記録", ButtonToggleHistory, BtnStyle.Ghost);
        g.leave = NineUi.Button("Leave", g.top, "退出", ButtonLeaveRoom, BtnStyle.Danger);
        g.timerTrack = NineUi.Flat("TimerTrack", g.top, NineTheme.A(NineTheme.Line, 0.6f));
        g.timerFill = NineUi.Flat("Fill", g.timerTrack.transform, NineTheme.Accent);
        NineUi.Stretch(g.timerFill.rectTransform);
        g.timerFill.type = Image.Type.Filled;
        g.timerFill.fillMethod = Image.FillMethod.Horizontal;
        // 角丸の画像を横に引き伸ばすと端が細く尖るので、残り時間バーは素の四角で描く
        g.timerFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100);   // 塗りつぶし(Filled)には画像が要る

        // 席の一覧
        g.seats = NineUi.Rect("Seats", g.root);

        // 場
        var table = NineUi.Panel("Table", g.root, NineTheme.A(NineTheme.Raised, 0.55f), 28);
        NineUi.Outline(table.transform, NineTheme.A(NineTheme.LineStrong, 0.7f), 28);
        g.table = table.rectTransform;
        g.tableCaption = NineUi.Text("Caption", g.table, "場", NineTheme.SizeSmall, NineTheme.Faint, TextAlignmentOptions.TopLeft, true);
        g.pointGroup = NineUi.Rect("Point", g.table);
        g.point = NineCard.Create("PointCard", g.pointGroup);
        g.pointCaption = NineUi.Text("Caption", g.pointGroup, "得点カード", NineTheme.SizeSmall, NineTheme.Gold, TextAlignmentOptions.Center, true);
        g.pointLeft = NineUi.Text("Left", g.pointGroup, "", NineTheme.SizeSmall, NineTheme.Muted, TextAlignmentOptions.Center);
        g.sep = NineUi.Flat("Sep", g.table, NineTheme.Line);

        // 手札と操作
        g.hand = NineUi.Rect("Hand", g.root);
        g.handCards = NineUi.Rect("Cards", g.hand);
        g.actions = NineUi.Rect("Actions", g.hand);
        g.hint = NineUi.Text("Hint", g.actions, "", NineTheme.SizeBody, NineTheme.Muted, TextAlignmentOptions.Center);
        g.hint.textWrappingMode = TextWrappingModes.Normal;
        g.confirm = NineUi.Button("Confirm", g.actions, "このカードを出す", ButtonConfirmCard, BtnStyle.Primary, 32);
        g.next = NineUi.Button("Next", g.actions, "次へ", ButtonNextRound, BtnStyle.Go, 32);

        var spec = NineUi.Panel("Spectator", g.hand, NineTheme.A(NineTheme.Spectate, 0.08f), 24);
        NineUi.Outline(spec.transform, NineTheme.A(NineTheme.Spectate, 0.45f), 24);
        g.spectator = spec.rectTransform;
        g.spectatorText = NineUi.Text("Text", g.spectator, "<b><color=" + NineTheme.ToHex(NineTheme.Spectate) + ">観戦中</color></b>　出されたカードは、確定した時点ですぐに見えます。", NineTheme.SizeBody, NineTheme.Text, TextAlignmentOptions.Center);
        g.spectatorText.textWrappingMode = TextWrappingModes.Normal;
        NineUi.Stretch(g.spectatorText.rectTransform, 32, 8, 32, 8);
    }

    void RebuildSeats(int n, int cc)
    {
        var g = game;
        NineUi.Clear(g.seats);
        g.tiles.Clear();
        for (int seat = 0; seat < n; seat++)
        {
            var tile = new SeatTile();
            var bg = NineUi.Panel("Seat" + seat, g.seats, NineTheme.Surface, 18);
            tile.bg = bg;
            tile.rt = bg.rectTransform;
            tile.ring = NineUi.Outline(bg.transform, NineTheme.Line, 18);
            tile.stripe = NineUi.Panel("Stripe", bg.transform, NineTheme.SeatColor(seat), 3);
            tile.badge = NineUi.Panel("Badge", bg.transform, NineTheme.SeatColor(seat), 10);
            tile.badgeText = NineUi.Text("Text", tile.badge.transform, SeatTag(seat), 22, NineTheme.CardInk, TextAlignmentOptions.Center, true);
            NineUi.Stretch(tile.badgeText.rectTransform);
            tile.name = NineUi.Text("Name", bg.transform, "", 28, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
            tile.name.overflowMode = TextOverflowModes.Ellipsis;
            tile.state = NineUi.Chip("State", bg.transform, "", NineTheme.Faint);
            tile.score = NineUi.Text("Score", bg.transform, "", 40, NineTheme.Gold, TextAlignmentOptions.MidlineRight, true);
            tile.score.overflowMode = TextOverflowModes.Overflow;
            tile.minis = NineUi.Rect("Minis", bg.transform);
            for (int j = 0; j < cc; j++) tile.cards.Add(NineCard.Create("C" + (j + 1), tile.minis));
            g.tiles.Add(tile);
        }

        foreach (var sl in g.slots) if (sl.rt != null) Destroy(sl.rt.gameObject);
        g.slots.Clear();
        for (int seat = 0; seat < n; seat++)
        {
            var sl = new Slot();
            sl.rt = NineUi.Rect("Slot" + seat, g.table);
            sl.card = NineCard.Create("Card", sl.rt, withTag: true, withStripe: true);
            sl.label = NineUi.Text("Label", sl.rt, "", NineTheme.SizeSmall, NineTheme.Muted, TextAlignmentOptions.Center, true);
            // 名前が長いと隣の席の名前に重なるので、枠の幅で切って「…」にする
            sl.label.overflowMode = TextOverflowModes.Ellipsis;
            sl.label.margin = new Vector4(6, 0, 6, 0);   // 隣の名前と間を空ける
            g.slots.Add(sl);
        }
        g.builtSeats = n;
        g.builtCards = cc;
    }

    void RebuildHand(int count)
    {
        var g = game;
        foreach (var c in g.handList) if (c != null) Destroy(c.gameObject);
        g.handList.Clear();
        for (int i = 0; i < count; i++)
        {
            int idx = i;
            var c = NineCard.Create("Hand" + (i + 1), g.handCards, clickable: true);
            c.SetClick(() => SelectCard(idx));
            g.handList.Add(c);
        }
        g.handBaseY = new float[count];
    }

    void RebuildCarried(int count)
    {
        var g = game;
        foreach (var c in g.carried) if (c != null) Destroy(c.gameObject);
        g.carried.Clear();
        for (int i = 0; i < count; i++)
        {
            var c = NineCard.Create("Carry" + i, g.pointGroup);
            c.transform.SetSiblingIndex(0);   // 今の得点カードの下に重ねる
            g.carried.Add(c);
        }
    }

    void RenderGame(Player local, Player viewer, GameManager gm, bool inGame)
    {
        var g = game;
        if (gm == null) return;
        int n = seatPlayers.Length, cc = gm.CARDCOUNT;
        bool spectating = viewer == null || viewer.isSpectator;
        int handCount = spectating ? 0 : viewer.used.Count;
        bool pointMode = gm.ScoringMode == GameManager.SCORING_POINT_CARDS && pointKnown && inGame;
        int carriedCount = pointMode ? pointCarried.Length : 0;

        if (n != g.builtSeats || cc != g.builtCards) { RebuildSeats(n, cc); layoutDirty = true; }
        if (g.handList.Count != handCount) { RebuildHand(handCount); layoutDirty = true; }
        if (g.carried.Count != carriedCount) { RebuildCarried(carriedCount); layoutDirty = true; }
        if (g.pointShown != pointMode) { g.pointShown = pointMode; layoutDirty = true; }
        if (layoutDirty) LayoutGame(n, cc, handCount, spectating, pointMode);

        // ---- 上部バー ----
        int done = gm.roundHistoryLog.Count;
        int round = Mathf.Clamp(inGame && !roundResolved ? done + 1 : done, 1, Mathf.Max(1, cc));
        if (!inGame) g.round.text = "ゲーム終了";
        else g.round.text = "第 " + round + " 戦<size=62%><color=" + NineTheme.ToHex(NineTheme.Muted) + ">  / " + cc + "</color></size>";
        NineUi.SetChip(g.mode, ModeName(gm.ScoringMode), NineTheme.Accent);

        int submitted = 0, present = 0;
        for (int i = 0; i < n; i++) { var p = seatPlayers[i]; if (p == null) continue; present++; if (p.isReadytoTurn) submitted++; }
        string status;
        if (!inGame) status = "おつかれさまでした";
        else if (roundResolved) status = transition > 0f ? "結果発表" : "次の戦いへ…";
        else if (spectating) status = "観戦中  ・  提出 " + submitted + " / " + present;
        else if (viewer.isReadytoTurn) status = "提出しました  ・  ほかの人を待っています " + submitted + " / " + present;
        else status = "出すカードを1枚選んでください  ・  提出 " + submitted + " / " + present;
        g.status.text = status;
        NineUi.Show(g.timerTrack, inGame && timerVisible);

        // ---- 席 ----
        for (int seat = 0; seat < n && seat < g.tiles.Count; seat++)
        {
            var tile = g.tiles[seat];
            var p = SeatPlayer(seat);
            bool me = p != null && p == viewer;
            string nm = NameOf(p, seat);
            if (me && tile.rt.rect.width > 560) nm += "  <size=72%><color=" + NineTheme.ToHex(NineTheme.Accent) + ">あなた</color></size>";
            tile.name.text = nm;
            tile.name.color = p == null ? NineTheme.Faint : NineTheme.Text;
            tile.score.text = ScoreOf(gm, seat) + "<size=52%><color=" + NineTheme.ToHex(NineTheme.Muted) + "> 点</color></size>";

            bool won = roundResolved && Contains(lastWinners, seat);
            bool champ = !inGame && gameOverOpen && Contains(goWinners, seat);
            string st; Color sc;
            if (champ) { st = "優勝"; sc = NineTheme.Gold; }
            else if (!inGame) { st = ""; sc = NineTheme.Faint; }
            else if (p == null) { st = "離席"; sc = NineTheme.Danger; }
            else if (won) { st = lastWinnerLabel; sc = NineTheme.Gold; }
            else if (roundResolved) { st = ""; sc = NineTheme.Faint; }
            else if (p.isReadytoTurn) { st = "提出済み"; sc = NineTheme.Go; }
            else { st = "考え中"; sc = NineTheme.Faint; }
            NineUi.Show(ChipRt(tile.state), st.Length > 0);
            if (st.Length > 0) NineUi.SetChip(tile.state, st, sc, won || champ);

            tile.bg.color = me ? NineTheme.RaisedHover : NineTheme.Surface;
            tile.ring.color = (won || champ) ? NineTheme.Gold : me ? NineTheme.A(NineTheme.Accent, 0.8f) : NineTheme.Line;
            tile.ring.sprite = (won || champ || me) ? NineUi.RingThick : NineUi.RingSprite;

            int revealed = RevealedOf(gm, seat);
            for (int j = 0; j < tile.cards.Count; j++)
            {
                int flat = seat * cc + j;
                bool used = flat < gm.used_Players.Count && gm.used_Players[flat];
                var c = tile.cards[j];
                c.SetMini(j + 1, used);
                // このラウンドで出したカードは席の色の枠で示す
                if (used && roundResolved && revealed == j + 1) { c.Ring.color = NineTheme.SeatColor(seat); c.Ring.sprite = NineUi.RingThick; c.Num.color = NineTheme.Muted; }
            }
        }

        // ---- 場 ----
        if (pointMode)
        {
            g.point.SetPoint(pointCard);
            int pot = pointCard;
            for (int i = 0; i < g.carried.Count; i++) { g.carried[i].SetPoint(pointCarried[i]); pot += pointCarried[i]; }
            g.pointCaption.text = carriedCount > 0 ? "場の得点 " + GameManager.FormatPoints(pot) : "得点カード";
            g.pointCaption.color = pot >= 0 ? NineTheme.Gold : NineTheme.Danger;
            g.pointLeft.text = "山札 のこり " + pointLeft + "枚";
        }
        for (int seat = 0; seat < n && seat < g.slots.Count; seat++)
        {
            var sl = g.slots[seat];
            var p = SeatPlayer(seat);
            var c = sl.card;
            int rev = RevealedOf(gm, seat);
            bool winner = rev > 0 && roundResolved && Contains(lastWinners, seat);
            if (rev > 0) c.SetFace(rev);
            else if (spectating && spectatorPicks != null && seat < spectatorPicks.Length && spectatorPicks[seat] > 0) c.SetPending(spectatorPicks[seat].ToString());
            else if (p != null && p.isReadytoTurn) c.SetPending(p == viewer && selectedIdx >= 0 ? (selectedIdx + 1).ToString() : "?");
            else c.SetEmpty();
            c.SetWinner(winner);
            bool clash = rev > 0 && roundResolved && gm.ScoringMode == GameManager.SCORING_POINT_CARDS && IsClash(revealedOverride, gm, seat, rev);
            if (winner) c.SetTag(lastWinnerLabel, NineTheme.Gold);
            else if (clash) c.SetTag("かぶり", NineTheme.Danger);
            else c.SetTag("", NineTheme.Gold);
            c.SetStripe(NineTheme.SeatColor(seat));
            string col = NineTheme.ToHex(NineTheme.SeatColor(seat));
            sl.label.text = "<color=" + col + ">" + SeatTag(seat) + "</color>" + (g.slotTwoLine ? "\n" : " ") + NameOf(p, seat);
            sl.label.color = p == viewer ? NineTheme.Text : NineTheme.Muted;
        }

        // ---- 手札 ----
        NineUi.Show(g.spectator, spectating);
        NineUi.Show(g.handCards, !spectating);
        NineUi.Show(g.actions, !spectating);
        if (!spectating)
        {
            bool canPick = inGame && !roundResolved && !viewer.isReadytoTurn;
            if (selectedIdx >= handCount) selectedIdx = -1;
            for (int i = 0; i < g.handList.Count; i++)
            {
                var c = g.handList[i];
                int value = i < viewer.cards.Count ? viewer.cards[i] : i + 1;
                bool used = i < viewer.used.Count && viewer.used[i];
                bool submittedThis = viewer.isReadytoTurn && i == selectedIdx && !roundResolved;
                bool lifted = false;
                if (submittedThis) { c.SetPending(value.ToString()); lifted = true; }
                else if (used) c.SetUsed(value);
                else if (i == selectedIdx && inGame) { c.SetSelected(value); lifted = true; }
                else c.SetFace(value);
                c.Button.interactable = canPick && !used;
                var rt = c.Rt;
                if (i < g.handBaseY.Length) rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, g.handBaseY[i] + (lifted ? g.handLift : 0));
            }

            bool resolvedOrOver = !inGame || roundResolved;
            bool showConfirm = !resolvedOrOver && (selectedIdx >= 0 || viewer.isReadytoTurn);
            NineUi.Show(g.confirm, showConfirm);
            if (showConfirm)
            {
                g.confirm.Text = viewer.isReadytoTurn ? "取り消す" : "このカードを出す";
                g.confirm.SetStyle(viewer.isReadytoTurn ? BtnStyle.Secondary : BtnStyle.Primary);
            }
            bool pressed = nextPressedBy.Contains(viewer) || viewer.isReadyForNextRound;
            bool showNext = inGame && transition > 0f && !pressed;
            NineUi.Show(g.next, showNext);

            string hint;
            if (!inGame) hint = "";
            else if (transition > 0f && pressed) hint = "ほかの人を待っています…";
            else if (roundResolved) hint = "";
            else if (viewer.isReadytoTurn) hint = "提出しました。結果が出るまでは取り消せます。";
            else if (selectedIdx >= 0) hint = "<b>" + (selectedIdx + 1) + "</b> を出しますか？";
            else hint = "手札から1枚選んでください";
            g.hint.text = hint;
        }
    }

    void LayoutGame(int n, int cc, int handCount, bool spectating, bool pointMode)
    {
        var g = game;
        float w = W, h = H;
        float m = portrait ? 22 : 30, gap = portrait ? 18 : 20;
        float topH = portrait ? 112 : 88;

        // 上部バー
        NineUi.TL(g.top, 0, 0, w, topH);
        NineUi.TL(g.topLine.rectTransform, 0, topH - 2, w, 2);
        NineUi.TL(g.timerTrack.rectTransform, 0, topH - 7, w, 7);
        RightMidIn(g.leave.Rt, m, 130, 60);
        RightMidIn(g.history.Rt, m + 130 + 12, 130, 60);
        if (portrait)
        {
            NineUi.TL(g.round.rectTransform, m, 14, 420, 52);
            NineUi.TL(ChipRt(g.mode), m + 300, 22, 150, 38);
            NineUi.TL(g.status.rectTransform, m, 66, w - 2 * m - 300, 36);
            g.status.alignment = TextAlignmentOptions.MidlineLeft;
        }
        else
        {
            NineUi.TL(g.round.rectTransform, m, 0, 300, topH);
            NineUi.TL(ChipRt(g.mode), m + 300, topH / 2 - 20, 150, 40);
            float sx = m + 470, sw = w - sx - (m + 290) - 20;
            NineUi.TL(g.status.rectTransform, sx, 0, sw, topH);
            g.status.alignment = TextAlignmentOptions.Center;
        }

        float avail = h - topH - 2 * m - 2 * gap;
        float seatsH, tableH, handH;
        if (spectating) { seatsH = avail * 0.50f; tableH = avail * 0.36f; }
        else if (portrait) { seatsH = avail * 0.38f; tableH = avail * 0.24f; }
        else { seatsH = avail * 0.40f; tableH = avail * 0.29f; }
        handH = avail - seatsH - tableH;
        float y = topH + m, cw = w - 2 * m;
        NineUi.TL(g.seats, m, y, cw, seatsH); y += seatsH + gap;
        NineUi.TL(g.table, m, y, cw, tableH); y += tableH + gap;
        NineUi.TL(g.hand, m, y, cw, handH);

        LayoutSeats(n, cc, cw, seatsH);
        LayoutTable(n, cw, tableH, pointMode);
        LayoutHand(handCount, cw, handH);
    }

    // 得点カードのルールで、同じ数字を出した人がほかにいる(無効になった)か
    bool IsClash(int[] picks, GameManager gm, int seat, int value)
    {
        int n = seatPlayers.Length, count = 0;
        for (int i = 0; i < n; i++) if (RevealedOf(gm, i) == value) count++;
        return count > 1;
    }

    static void RightMidIn(RectTransform rt, float right, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-right, 0);
        rt.sizeDelta = new Vector2(w, h);
    }

    void LayoutSeats(int n, int cc, float aw, float ah)
    {
        var g = game;
        if (n <= 0) return;
        int cols;
        if (portrait) cols = n <= 2 ? 1 : 2;
        else cols = n <= 3 ? n : n == 4 ? 2 : 3;
        int rows = (n + cols - 1) / cols;
        float gap = 14;
        float tw = (aw - gap * (cols - 1)) / cols;
        float th = Mathf.Min((ah - gap * (rows - 1)) / rows, 300);
        float oy = (ah - (th * rows + gap * (rows - 1))) / 2;

        float pad = Mathf.Clamp(th * 0.08f, 8, 16);
        float headH = Mathf.Clamp(th * 0.36f, 30, 60);
        float minisH = th - headH - pad * 2 - 4;
        float mgap = Mathf.Clamp(tw * 0.006f, 2, 6);
        float innerW = tw - pad * 2 - 10;
        // 枚数が少ないときに手札より大きくならないよう、上限を設ける
        float mw = Mathf.Min(Mathf.Min((innerW - mgap * (cc - 1)) / cc, minisH / 1.3f), 64f);
        float mh = Mathf.Min(mw * 1.3f, minisH);
        float stripW = mw * cc + mgap * (cc - 1);

        for (int i = 0; i < n && i < g.tiles.Count; i++)
        {
            var tile = g.tiles[i];
            int r = i / cols, c = i % cols;
            int inRow = Mathf.Min(cols, n - r * cols);
            float rowOffset = (aw - (tw * inRow + gap * (inRow - 1))) / 2;
            NineUi.TL(tile.rt, rowOffset + c * (tw + gap), oy + r * (th + gap), tw, th);
            NineUi.TL(tile.stripe.rectTransform, 0, 0, 7, th);
            float bh = headH * 0.78f;
            NineUi.TL(tile.badge.rectTransform, pad + 10, pad + (headH - bh) / 2, bh * 1.2f, bh);
            tile.badgeText.fontSize = bh * 0.46f;
            float scoreW = Mathf.Max(120, headH * 2.4f);
            NineUi.TL(tile.score.rectTransform, tw - pad - scoreW, pad, scoreW, headH);
            tile.score.fontSize = headH * 0.68f;
            float chipW = headH * 2.1f;
            var chip = ChipRt(tile.state);
            NineUi.TL(chip, tw - pad - scoreW - chipW - 8, pad + headH * 0.18f, chipW, headH * 0.64f);
            tile.state.fontSize = headH * 0.36f;
            float nx = pad + 10 + bh * 1.2f + 12;
            NineUi.TL(tile.name.rectTransform, nx, pad, tw - nx - pad - scoreW - chipW - 16, headH);
            tile.name.fontSize = headH * 0.48f;

            NineUi.TL(tile.minis, pad + 10 + (innerW - stripW) / 2, pad + headH + 4 + (minisH - mh) / 2, stripW, mh);
            for (int j = 0; j < tile.cards.Count; j++)
            {
                var card = tile.cards[j];
                NineUi.TL(card.Rt, j * (mw + mgap), 0, mw, mh);
                card.Layout(mw, mh);
            }
        }
    }

    void LayoutTable(int n, float tw, float th, bool pointMode)
    {
        var g = game;
        float pad = Mathf.Clamp(th * 0.08f, 12, 26);
        NineUi.TL(g.tableCaption.rectTransform, 22, 12, 100, 30);
        float labelH = Mathf.Clamp(th * 0.13f, 24, 40);
        // 席が多く1席あたりの幅が狭いときは、名前を2行(P1/名前)にして長く出す
        g.slotTwoLine = n > 0 && (tw - pad * 2) / n < 190f;
        float lineH = labelH;
        if (g.slotTwoLine) labelH *= 1.9f;
        float ch = (th - pad * 2 - labelH - 6) / 1.12f;
        float cw = ch / 1.4f;
        float gapS = cw * 0.32f;
        int carriedN = g.carried.Count;
        float pointW = pointMode ? cw * 1.0f + (carriedN > 0 ? cw * 0.22f * carriedN + 6 : 0) : 0;
        float sepW = pointMode ? 56 : 0;
        float total = pointW + sepW + n * cw + Mathf.Max(0, n - 1) * gapS;
        float avail = tw - pad * 2 - 40;
        if (total > avail)
        {
            float k = avail / total;
            cw *= k; ch *= k; gapS *= k; pointW *= k; total = avail;
        }
        else
        {
            // 横に余裕があるときは席の間を広げ、名前を長く表示できるようにする(カード1枚分まで)。
            // 得点カードとの仕切りも同じだけ広げ、先頭の席の名前が「山札のこり」に重ならないようにする
            int gaps = (n - 1) + (pointMode ? 1 : 0);
            if (gaps > 0)
            {
                float add = Mathf.Min((avail - total) / gaps, cw * 1.0f);
                gapS += add;
                if (pointMode) sepW += add;
                total += add * gaps;
            }
        }
        float x = (tw - total) / 2;
        float cardTop = pad + (th - pad * 2 - (ch * 1.12f + labelH + 6)) / 2 + ch * 0.12f;

        NineUi.Show(g.pointGroup, pointMode);
        NineUi.Show(g.sep, pointMode);
        if (pointMode)
        {
            NineUi.TL(g.pointGroup, x, 0, pointW, th);
            for (int i = 0; i < carriedN; i++)
            {
                var c = g.carried[i];
                NineUi.TL(c.Rt, cw * 0.22f * (carriedN - i), cardTop + 6, cw, ch - 12);
                c.Layout(cw, ch - 12);
            }
            NineUi.TL(g.point.Rt, 0, cardTop, cw, ch);
            g.point.Layout(cw, ch);
            g.point.transform.SetAsLastSibling();
            NineUi.TL(g.pointCaption.rectTransform, -30, cardTop - Mathf.Min(34, cardTop - 2), pointW + 60, 30);
            NineUi.TL(g.pointLeft.rectTransform, -30, cardTop + ch + 6, pointW + 60, labelH);
            g.pointLeft.fontSize = Mathf.Min(NineTheme.SizeSmall, labelH * 0.7f);
            g.pointCaption.fontSize = Mathf.Min(NineTheme.SizeSmall, labelH * 0.7f);
            NineUi.TL(g.sep.rectTransform, x + pointW + sepW / 2 - 1, cardTop, 2, ch);
            x += pointW + sepW;
        }
        for (int i = 0; i < n && i < g.slots.Count; i++)
        {
            var sl = g.slots[i];
            NineUi.TL(sl.rt, x + i * (cw + gapS) - gapS / 2, 0, cw + gapS, th);
            NineUi.TL(sl.card.Rt, gapS / 2, cardTop, cw, ch);
            sl.card.Layout(cw, ch);
            NineUi.TL(sl.label.rectTransform, 0, cardTop + ch + 6, cw + gapS, labelH);
            sl.label.fontSize = Mathf.Min(NineTheme.SizeSmall, lineH * 0.66f);
            sl.label.lineSpacing = -12;
        }
    }

    void LayoutHand(int count, float hw, float hh)
    {
        var g = game;
        NineUi.TL(g.spectator, 0, 0, hw, Mathf.Min(hh, 160));
        float actionW, cardsW, cardsH, cardsX, cardsY;
        if (portrait)
        {
            float rowH = 96;
            NineUi.TL(g.actions, 0, 0, hw, rowH);
            float bw = Mathf.Min(420, hw * 0.46f);
            NineUi.TL(g.hint.rectTransform, 0, 0, hw - bw - 20, rowH);
            g.hint.alignment = TextAlignmentOptions.MidlineLeft;
            NineUi.TL(g.confirm.Rt, hw - bw, 4, bw, rowH - 8);
            NineUi.TL(g.next.Rt, hw - bw, 4, bw, rowH - 8);
            cardsX = 0; cardsY = rowH + 12; cardsW = hw; cardsH = hh - rowH - 12;
        }
        else
        {
            actionW = Mathf.Min(360, hw * 0.22f);
            NineUi.TL(g.actions, hw - actionW, 0, actionW, hh);
            float bh = Mathf.Min(92, hh * 0.36f);
            NineUi.TL(g.hint.rectTransform, 0, hh / 2 - bh / 2 - 84, actionW, 76);
            g.hint.alignment = TextAlignmentOptions.Bottom;
            NineUi.TL(g.confirm.Rt, 0, hh / 2 - bh / 2, actionW, bh);
            NineUi.TL(g.next.Rt, 0, hh / 2 - bh / 2, actionW, bh);
            cardsX = 0; cardsY = 0; cardsW = hw - actionW - 28; cardsH = hh;
        }
        NineUi.TL(g.handCards, cardsX, cardsY, cardsW, cardsH);
        if (count <= 0) return;

        int rows = portrait && count > 8 ? 2 : 1;
        int perRow = (count + rows - 1) / rows;
        float gap = Mathf.Clamp(cardsW * 0.008f, 6, 14);
        float rowGap = 14;
        float liftRoom = 0.12f;
        float cardH = Mathf.Min((cardsH - rowGap * (rows - 1)) / rows / (1 + liftRoom), 260);
        float cardW = Mathf.Min(cardH / 1.4f, (cardsW - gap * (perRow - 1)) / perRow);
        cardH = cardW * 1.4f;
        g.handLift = cardH * liftRoom;
        float blockH = rows * cardH * (1 + liftRoom) + rowGap * (rows - 1);
        float oy = (cardsH - blockH) / 2;
        for (int i = 0; i < count && i < g.handList.Count; i++)
        {
            int r = i / perRow, c = i % perRow;
            int inRow = Mathf.Min(perRow, count - r * perRow);
            float rowW = inRow * cardW + (inRow - 1) * gap;
            float x = (cardsW - rowW) / 2 + c * (cardW + gap);
            float yTop = oy + r * (cardH * (1 + liftRoom) + rowGap) + cardH * liftRoom;
            var card = g.handList[i];
            NineUi.TL(card.Rt, x, yTop, cardW, cardH);
            card.Layout(cardW, cardH);
            g.handBaseY[i] = -yTop;
        }
    }
}
