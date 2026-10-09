using System.Collections.Generic;
using System.Linq;
using Mirror;
using Nine.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 重ねて出すもの: ラウンド開始の帯、通知、ラウンド結果、ゲーム結果、記録、退出の確認。
public partial class UIEventsManager
{
    class OverlayView
    {
        public RectTransform cutIn, toast;
        public CanvasGroup cutInGroup, toastGroup;
        public Image cutInBand;
        public TextMeshProUGUI cutInMain, cutInSub, toastText;

        public RectTransform resultDim, resultCard, resultSlots;
        public TextMeshProUGUI resultCaption, resultHeadline;
        public NineButton resultNext, resultClose;

        public RectTransform goDim, goCard, goList;
        public TextMeshProUGUI goTitle, goHeadline;
        public NineButton goHistory, goBack;

        public RectTransform histDim, histCard, histHeader, histContent, histScrollRt;
        public TextMeshProUGUI histTitle, histEmpty;
        public NineButton histClose;
        public ScrollRect histScroll;

        public RectTransform leaveDim, leaveCard;
        public TextMeshProUGUI leaveTitle, leaveBody;
        public NineButton leaveYes, leaveNo;
    }
    OverlayView ov = new OverlayView();

    void BuildOverlays()
    {
        var o = ov;

        // ---- ラウンド開始の帯 ----
        o.cutIn = NineUi.Stretch(NineUi.Rect("CutIn", overlayRoot));
        o.cutInGroup = o.cutIn.gameObject.AddComponent<CanvasGroup>();
        o.cutInGroup.blocksRaycasts = false;
        o.cutInGroup.interactable = false;
        o.cutInBand = NineUi.Flat("Band", o.cutIn, NineTheme.A(NineTheme.Background, 0.9f));
        var lineA = NineUi.Flat("LineA", o.cutInBand.transform, NineTheme.Accent);
        var la = lineA.rectTransform; la.anchorMin = new Vector2(0, 1); la.anchorMax = new Vector2(1, 1); la.pivot = new Vector2(0.5f, 1); la.sizeDelta = new Vector2(0, 3); la.anchoredPosition = Vector2.zero;
        var lineB = NineUi.Flat("LineB", o.cutInBand.transform, NineTheme.Accent);
        var lb = lineB.rectTransform; lb.anchorMin = new Vector2(0, 0); lb.anchorMax = new Vector2(1, 0); lb.pivot = new Vector2(0.5f, 0); lb.sizeDelta = new Vector2(0, 3); lb.anchoredPosition = Vector2.zero;
        o.cutInMain = NineUi.Text("Main", o.cutInBand.transform, "", 104, NineTheme.Text, TextAlignmentOptions.Center, true);
        o.cutInMain.characterSpacing = 8;
        o.cutInMain.overflowMode = TextOverflowModes.Overflow;
        o.cutInSub = NineUi.Text("Sub", o.cutInBand.transform, "", 32, NineTheme.Muted, TextAlignmentOptions.Center, true);
        o.cutIn.gameObject.SetActive(false);

        // ---- ラウンド結果 ----
        o.resultDim = NineUi.Modal("Result", overlayRoot, out o.resultCard, () => { resultOpen = false; MarkDirty(); });
        o.resultCaption = NineUi.Text("Caption", o.resultCard, "", NineTheme.SizeBody, NineTheme.Muted, TextAlignmentOptions.Center, true);
        o.resultHeadline = NineUi.Text("Headline", o.resultCard, "", 46, NineTheme.Gold, TextAlignmentOptions.Center, true);
        o.resultSlots = NineUi.Rect("Slots", o.resultCard);
        o.resultClose = NineUi.Button("Close", o.resultCard, "閉じる", () => { resultOpen = false; MarkDirty(); }, BtnStyle.Ghost);
        o.resultNext = NineUi.Button("Next", o.resultCard, "次へ", ButtonNextRound, BtnStyle.Go, 30);

        // ---- ゲーム結果 ----
        o.goDim = NineUi.Modal("GameOver", overlayRoot, out o.goCard, null);
        o.goTitle = NineUi.Text("Title", o.goCard, "ゲーム終了", NineTheme.SizeBody, NineTheme.Muted, TextAlignmentOptions.Center, true);
        o.goTitle.characterSpacing = 6;
        o.goHeadline = NineUi.Text("Headline", o.goCard, "", 56, NineTheme.Gold, TextAlignmentOptions.Center, true);
        o.goList = NineUi.Rect("Ranking", o.goCard);
        o.goHistory = NineUi.Button("History", o.goCard, "記録を見る", () => { historyOpen = true; historyDirty = true; layoutDirty = true; MarkDirty(); }, BtnStyle.Secondary);
        o.goBack = NineUi.Button("Back", o.goCard, "ルームに戻る", CloseGameOver, BtnStyle.Primary, 30);

        // ---- 記録 ----
        o.histDim = NineUi.Modal("History", overlayRoot, out o.histCard, () => { historyOpen = false; MarkDirty(); });
        o.histTitle = NineUi.Text("Title", o.histCard, "対戦の記録", NineTheme.SizeTitle, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        o.histClose = NineUi.Button("Close", o.histCard, "閉じる", () => { historyOpen = false; MarkDirty(); }, BtnStyle.Secondary);
        o.histHeader = NineUi.Rect("Header", o.histCard);
        o.histContent = NineUi.ScrollArea("Rows", o.histCard, out o.histScroll, 8);
        o.histScrollRt = (RectTransform)o.histScroll.transform;
        o.histEmpty = NineUi.Text("Empty", o.histCard, "まだ記録がありません。", NineTheme.SizeBody, NineTheme.Faint, TextAlignmentOptions.Center);

        // ---- 退出の確認 ----
        o.leaveDim = NineUi.Modal("LeaveConfirm", overlayRoot, out o.leaveCard, () => { leaveConfirmOpen = false; MarkDirty(); });
        o.leaveTitle = NineUi.Text("Title", o.leaveCard, "対局から抜けますか？", NineTheme.SizeTitle, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
        o.leaveBody = NineUi.Text("Body", o.leaveCard, "抜けている間は、残りのカードからランダムに出されます。\n同じ部屋に入り直すと、自分の席に戻れます。", NineTheme.SizeBody, NineTheme.Muted, TextAlignmentOptions.TopLeft);
        o.leaveBody.textWrappingMode = TextWrappingModes.Normal;
        o.leaveBody.lineSpacing = 10;
        o.leaveNo = NineUi.Button("Stay", o.leaveCard, "続ける", () => { leaveConfirmOpen = false; MarkDirty(); }, BtnStyle.Secondary);
        o.leaveYes = NineUi.Button("Leave", o.leaveCard, "抜ける", DoLeave, BtnStyle.Danger);

        // ---- 通知 ----
        var toast = NineUi.Panel("Toast", overlayRoot, NineTheme.Raised, 18);
        NineUi.Outline(toast.transform, NineTheme.A(NineTheme.Warn, 0.7f), 18);
        o.toast = toast.rectTransform;
        o.toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
        o.toastGroup.blocksRaycasts = false;
        var mark = NineUi.Panel("Mark", toast.transform, NineTheme.Warn, 999);
        LeftMid(mark.rectTransform, 22, 0, 12, 12);
        o.toastText = NineUi.Text("Text", toast.transform, "", NineTheme.SizeBody, NineTheme.Text, TextAlignmentOptions.MidlineLeft);
        o.toastText.textWrappingMode = TextWrappingModes.Normal;
        NineUi.Stretch(o.toastText.rectTransform, 50, 8, 24, 8);
        toast.gameObject.SetActive(false);
    }

    void RenderOverlays(Player local, Player viewer, GameManager gm)
    {
        var o = ov;
        bool gameView = view == View.Game;

        bool showResult = resultOpen && gameView && !gameOverOpen;
        NineUi.Show(o.resultDim, showResult);
        if (showResult && layoutDirty) FillResult(gm, viewer);
        if (showResult) NineUi.Show(o.resultNext, game.next.gameObject.activeSelf);

        bool showGo = gameOverOpen && gameView;
        NineUi.Show(o.goDim, showGo);
        if (showGo && layoutDirty) FillGameOver(gm, viewer);

        NineUi.Show(o.histDim, historyOpen);
        if (historyOpen && (historyDirty || layoutDirty)) { historyDirty = false; FillHistory(gm, viewer); }

        NineUi.Show(o.leaveDim, leaveConfirmOpen);
        if (leaveConfirmOpen && layoutDirty) LayoutLeave();

        if (layoutDirty) LayoutCutInAndToast();
    }

    // ================= 毎フレームの動き =================
    void AnimateOverlays()
    {
        float now = Time.unscaledTime;
        var g = game;

        // 残り時間バー: 余裕のあるうちは落ち着いた青、残り半分から黄、終盤は赤
        if (g.timerFill != null && timerVisible)
        {
            timerShown = Mathf.MoveTowards(timerShown, timerTarget, Time.unscaledDeltaTime * 3f);
            g.timerFill.fillAmount = timerShown;
            g.timerFill.color = TimerColor(timerShown);
        }

        // 「次へ」に、自動で進むまでの秒数を添える
        if (transition > 0f)
        {
            string label = "次へ  <size=70%><color=#1A1F2999>" + Mathf.CeilToInt(transition * 5f) + "</color></size>";
            if (g.next != null) g.next.Text = label;
            if (ov.resultNext != null) ov.resultNext.Text = label;
        }

        // ラウンド開始の帯
        float tc = now - cutInStart;
        bool cutOn = tc >= 0f && tc < CUT_IN_SECONDS && view == View.Game;
        NineUi.Show(ov.cutIn, cutOn);
        if (cutOn)
        {
            float a = tc < 0.18f ? tc / 0.18f : tc > CUT_IN_SECONDS - 0.32f ? (CUT_IN_SECONDS - tc) / 0.32f : 1f;
            ov.cutInGroup.alpha = Mathf.Clamp01(a);
            float s = 0.92f + 0.08f * Mathf.Clamp01(tc / 0.25f);
            ov.cutInBand.rectTransform.localScale = new Vector3(1f, s, 1f);
            ov.cutInMain.text = cutInMain;
            ov.cutInSub.text = cutInSub;
            ov.cutInMain.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-40f, 0f, Mathf.Clamp01(tc / 0.3f)), 22);
        }

        // 通知
        float tt = noticeUntil - now;
        bool toastOn = tt > 0f && noticeText.Length > 0;
        NineUi.Show(ov.toast, toastOn);
        if (toastOn)
        {
            ov.toastText.text = noticeText;
            float shown = 4.5f - tt;
            ov.toastGroup.alpha = Mathf.Clamp01(Mathf.Min(shown / 0.15f, tt / 0.4f));
        }

        // ラウンド結果は時間が来たら閉じる
        if (resultOpen && now > resultUntil) { resultOpen = false; MarkDirty(); }
    }

    static Color TimerColor(float f)
    {
        if (f >= 0.5f) return NineTheme.Accent;
        if (f >= 0.25f) return Color.Lerp(NineTheme.Warn, NineTheme.Accent, (f - 0.25f) / 0.25f);
        return Color.Lerp(NineTheme.Danger, NineTheme.Warn, f / 0.25f);
    }

    void LayoutCutInAndToast()
    {
        var o = ov;
        float bandH = portrait ? 260 : 230;
        NineUi.Center(o.cutInBand.rectTransform, 0, 40, W + 40, bandH);
        NineUi.Center(o.cutInMain.rectTransform, 0, 22, W, 130);
        NineUi.Center(o.cutInSub.rectTransform, 0, -66, W, 44);

        float tw = Mathf.Min(W - 48, 1100);
        float top = view == View.Title ? 28 : (portrait ? 128 : 104);
        o.toast.anchorMin = o.toast.anchorMax = new Vector2(0.5f, 1);
        o.toast.pivot = new Vector2(0.5f, 1);
        o.toast.anchoredPosition = new Vector2(0, -top);
        o.toast.sizeDelta = new Vector2(tw, 84);
    }

    // ================= ラウンド結果 =================
    string ResultHeadline(GameManager gm)
    {
        if (resultWinners.Length > 0)
        {
            string names = string.Join("・", resultWinners.Select(sx => NameOf(SeatPlayer(sx), sx)).ToArray());
            if (gm != null && gm.ScoringMode == GameManager.SCORING_POINT_CARDS)
                return names + " が " + PtJa(resultWinnerLabel) + " を獲得";
            return names + " の勝ち　" + ResultLabelJa(resultLabel);
        }
        if (resultLabel == "Tie") return "全員が同じ数字 — 引き分け";
        if (resultLabel.StartsWith("Carry ")) return "全員かぶり — " + PtJa(resultLabel.Substring(6)) + " は次へ持ち越し";
        if (resultLabel.StartsWith("Lost ")) return "全員かぶり — " + PtJa(resultLabel.Substring(5)) + " は流れました";
        return "勝者なし";
    }

    void FillResult(GameManager gm, Player viewer)
    {
        var o = ov;
        float cw = portrait ? W - 48 : Mathf.Min(W - 160, 1320);
        float ch = portrait ? 720 : 560;
        NineUi.Center(o.resultCard, 0, portrait ? 0 : 30, cw, ch);
        NineUi.TL(o.resultCaption.rectTransform, 40, 34, cw - 80, 34);
        o.resultCaption.text = "第 " + resultRound + " 戦の結果";
        NineUi.TL(o.resultHeadline.rectTransform, 40, 74, cw - 80, 66);
        o.resultHeadline.text = ResultHeadline(gm);
        o.resultHeadline.color = resultWinners.Length > 0 ? NineTheme.Gold : NineTheme.Warn;
        NineUi.Fit(o.resultHeadline, portrait ? 40 : 46, 16);

        float bh = 76, by = ch - 32 - bh;
        NineUi.TL(o.resultClose.Rt, 40, by, 180, bh);
        NineUi.TL(o.resultNext.Rt, cw - 40 - 260, by, 260, bh);

        float areaTop = 160, areaH = by - areaTop - 24, areaW = cw - 80;
        NineUi.TL(o.resultSlots, 40, areaTop, areaW, areaH);
        NineUi.Clear(o.resultSlots);
        int n = resultPicks.Length;
        if (n == 0) return;
        float labelH = 40;
        float cardH = Mathf.Min((areaH - labelH - 10) / 1.12f, 230);
        float cardW = cardH / 1.4f;
        float gap = cardW * 0.35f;
        float total = n * cardW + (n - 1) * gap;
        if (total > areaW) { float k = areaW / total; cardW *= k; cardH *= k; gap *= k; total = areaW; }
        else if (n > 1) { float add = Mathf.Min((areaW - total) / (n - 1), cardW * 0.8f); gap += add; total += add * (n - 1); }   // 名前を長く出せるよう席の間を広げる
        float x0 = (areaW - total) / 2;
        float top = (areaH - (cardH * 1.12f + labelH + 10)) / 2 + cardH * 0.12f;
        for (int i = 0; i < n; i++)
        {
            var slot = NineUi.Rect("Slot" + i, o.resultSlots);
            NineUi.TL(slot, x0 + i * (cardW + gap) - gap / 2, 0, cardW + gap, areaH);
            var card = NineCard.Create("Card", slot, withTag: true, withStripe: true);
            NineUi.TL(card.Rt, gap / 2, top, cardW, cardH);
            card.Layout(cardW, cardH);
            if (resultPicks[i] > 0) card.SetFace(resultPicks[i]); else card.SetEmpty();
            bool win = Contains(resultWinners, i);
            card.SetWinner(win);
            int dup = 0;
            for (int k = 0; k < n; k++) if (resultPicks[k] == resultPicks[i]) dup++;
            bool clash = !win && resultPicks[i] > 0 && dup > 1 && gm != null && gm.ScoringMode == GameManager.SCORING_POINT_CARDS;
            if (win) card.SetTag(WinnerTag(resultWinnerLabel), NineTheme.Gold);
            else if (clash) card.SetTag("かぶり", NineTheme.Danger);
            else card.SetTag("", NineTheme.Gold);
            card.SetStripe(NineTheme.SeatColor(i));
            var p = SeatPlayer(i);
            var label = NineUi.Text("Label", slot, "<color=" + NineTheme.ToHex(NineTheme.SeatColor(i)) + ">" + SeatTag(i) + "</color> " + NameOf(p, i), NineTheme.SizeSmall, p != null && p == viewer ? NineTheme.Text : NineTheme.Muted, TextAlignmentOptions.Center, true);
            NineUi.TL(label.rectTransform, 0, top + cardH + 10, cardW + gap, labelH);
            NineUi.Fit(label, NineTheme.SizeSmall, 14);
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    // ================= ゲーム結果 =================
    void FillGameOver(GameManager gm, Player viewer)
    {
        var o = ov;
        int n = goScores.Length;
        float rowH = 80, gap = 10;
        float cw = portrait ? W - 48 : Mathf.Min(W - 160, 880);
        float listH = n * rowH + Mathf.Max(0, n - 1) * gap;
        float ch = 34 + 34 + 16 + 76 + 28 + listH + 36 + 80 + 36;
        ch = Mathf.Min(ch, H - 80);
        NineUi.Center(o.goCard, 0, 0, cw, ch);
        float y = 34;
        NineUi.TL(o.goTitle.rectTransform, 40, y, cw - 80, 34); y += 34 + 16;
        string names = string.Join("・", goWinners.Select(sx => NameOf(SeatPlayer(sx), sx)).ToArray());
        o.goHeadline.text = goWinners.Length == 1 ? "優勝　" + names : goWinners.Length > 1 ? "同点優勝　" + names : "引き分け";
        NineUi.TL(o.goHeadline.rectTransform, 40, y, cw - 80, 76); y += 76 + 28;
        NineUi.Fit(o.goHeadline, 56, 18);

        NineUi.TL(o.goList, 40, y, cw - 80, listH);
        NineUi.Clear(o.goList);
        var order = Enumerable.Range(0, n).OrderByDescending(i => goScores[i]).ThenBy(i => i).ToList();
        Color[] medal = { NineTheme.Gold, NineTheme.Hex("C3CAD6"), NineTheme.Hex("D49A6A") };
        int rank = 0, prev = int.MinValue;
        for (int k = 0; k < order.Count; k++)
        {
            int seat = order[k];
            if (goScores[seat] != prev) { rank = k + 1; prev = goScores[seat]; }
            var p = SeatPlayer(seat);
            bool top = rank == 1;
            var row = NineUi.Panel("Rank" + k, o.goList, top ? NineTheme.A(NineTheme.Gold, 0.12f) : NineTheme.Raised, 16);
            NineUi.TL(row.rectTransform, 0, k * (rowH + gap), cw - 80, rowH);
            if (top) NineUi.Outline(row.transform, NineTheme.A(NineTheme.Gold, 0.7f), 16);
            var rc = rank <= 3 ? medal[rank - 1] : NineTheme.Faint;
            var rt = NineUi.Text("Rank", row.transform, rank + "<size=60%>位</size>", 36, rc, TextAlignmentOptions.Center, true);
            LeftMid(rt.rectTransform, 12, 0, 90, rowH);
            var badge = NineUi.Panel("Badge", row.transform, NineTheme.SeatColor(seat), 10);
            LeftMid(badge.rectTransform, 110, 0, 58, 48);
            var bt = NineUi.Text("T", badge.transform, SeatTag(seat), 22, NineTheme.CardInk, TextAlignmentOptions.Center, true);
            NineUi.Stretch(bt.rectTransform);
            string you = p != null && p == viewer ? "  <size=75%><color=" + NineTheme.ToHex(NineTheme.Accent) + ">あなた</color></size>" : "";
            var nm = NineUi.Text("Name", row.transform, NameOf(p, seat) + you, 30, NineTheme.Text, TextAlignmentOptions.MidlineLeft, true);
            nm.overflowMode = TextOverflowModes.Ellipsis;
            var nrt = nm.rectTransform; nrt.anchorMin = Vector2.zero; nrt.anchorMax = Vector2.one; nrt.offsetMin = new Vector2(188, 0); nrt.offsetMax = new Vector2(-200, 0);
            var sc = NineUi.Text("Score", row.transform, goScores[seat] + "<size=55%> 点</size>", 40, top ? NineTheme.Gold : NineTheme.Text, TextAlignmentOptions.MidlineRight, true);
            RightMid(sc.rectTransform, 28, 180, rowH);
        }
        y += listH + 36;
        float bw = (cw - 80 - 16) / 2;
        NineUi.TL(o.goHistory.Rt, 40, y, bw, 80);
        NineUi.TL(o.goBack.Rt, 40 + bw + 16, y, bw, 80);
    }

    // ================= 記録 =================
    void FillHistory(GameManager gm, Player viewer)
    {
        var o = ov;
        float cw = portrait ? W - 32 : Mathf.Min(W - 120, 1500);
        float ch = portrait ? H - 220 : H - 140;
        NineUi.Center(o.histCard, 0, 0, cw, ch);
        NineUi.TL(o.histTitle.rectTransform, 36, 28, cw - 260, 56);
        NineUi.TL(o.histClose.Rt, cw - 36 - 170, 26, 170, 60);

        var entries = new List<string>();
        if (gm != null) for (int i = 0; i < gm.roundHistoryLog.Count; i++) entries.Add(gm.roundHistoryLog[i]);
        int n = seatPlayers.Length;
        foreach (var e in entries)
        {
            int r; List<int> c, w; string l;
            if (TryParseHistoryEntry(e, out r, out c, out w, out l)) n = Mathf.Max(n, c.Count);
        }

        float innerW = cw - 72;
        float roundW = portrait ? 70 : 96, resultW = portrait ? 150 : 200;
        float colW = n > 0 ? (innerW - roundW - resultW) / n : 0;
        float cardH = Mathf.Clamp(colW * 0.9f, 44, 84);
        float cardW = Mathf.Min(cardH / 1.35f, colW - 8);
        cardH = cardW * 1.35f;
        float rowH = cardH + 18;

        // 見出し(席)
        NineUi.TL(o.histHeader, 36, 100, innerW, 44);
        NineUi.Clear(o.histHeader);
        var hr = NineUi.Text("Round", o.histHeader, "回", NineTheme.SizeSmall, NineTheme.Faint, TextAlignmentOptions.Center, true);
        NineUi.TL(hr.rectTransform, 0, 0, roundW, 44);
        for (int i = 0; i < n; i++)
        {
            var p = SeatPlayer(i);
            var t = NineUi.Text("Seat" + i, o.histHeader, "<color=" + NineTheme.ToHex(NineTheme.SeatColor(i)) + ">" + SeatTag(i) + "</color> " + NameOf(p, i), NineTheme.SizeSmall, p != null && p == viewer ? NineTheme.Text : NineTheme.Muted, TextAlignmentOptions.Center, true);
            NineUi.TL(t.rectTransform, roundW + i * colW, 0, colW, 44);
            NineUi.Fit(t, NineTheme.SizeSmall, 14);
            t.overflowMode = TextOverflowModes.Ellipsis;
        }
        var hres = NineUi.Text("Result", o.histHeader, "結果", NineTheme.SizeSmall, NineTheme.Faint, TextAlignmentOptions.Center, true);
        NineUi.TL(hres.rectTransform, roundW + n * colW, 0, resultW, 44);

        NineUi.TL(o.histScrollRt, 36, 152, innerW, ch - 152 - 30);
        NineUi.Clear(o.histContent);
        NineUi.Show(o.histEmpty, entries.Count == 0);
        NineUi.TL(o.histEmpty.rectTransform, 36, 200, innerW, 60);

        foreach (var e in entries)
        {
            int round; List<int> cards, winners; string label;
            if (!TryParseHistoryEntry(e, out round, out cards, out winners, out label)) continue;
            var row = NineUi.Panel("R" + round, o.histContent, NineTheme.Raised, 14);
            NineUi.Height(row, rowH);
            var rt = NineUi.Text("Round", row.transform, round.ToString(), 30, NineTheme.Muted, TextAlignmentOptions.Center, true);
            LeftMid(rt.rectTransform, 0, 0, roundW, rowH);
            for (int i = 0; i < cards.Count; i++)
            {
                var c = NineCard.Create("C" + i, row.transform);
                var crt = c.Rt;
                crt.anchorMin = crt.anchorMax = new Vector2(0, 0.5f);
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.anchoredPosition = new Vector2(roundW + i * colW + colW / 2, 0);
                c.Layout(cardW, cardH);
                if (cards[i] > 0) c.SetFace(cards[i]); else c.SetEmpty();
                c.SetWinner(winners.Contains(i));
            }
            string ja = ResultLabelJa(label);
            var lt = NineUi.Text("Label", row.transform, ja, 28, ja.StartsWith("+") ? NineTheme.Gold : ja.StartsWith("-") ? NineTheme.Danger : NineTheme.Muted, TextAlignmentOptions.Center, true);
            LeftMid(lt.rectTransform, roundW + n * colW, 0, resultW, rowH);
            NineUi.Fit(lt, 28, 12);
        }
        // 新しい行が見えるように一番下へ
        Canvas.ForceUpdateCanvases();
        o.histScroll.verticalNormalizedPosition = 0f;
    }

    // ================= 退出の確認 =================
    void LayoutLeave()
    {
        var o = ov;
        float cw = Mathf.Min(W - 48, 820), ch = 380;
        NineUi.Center(o.leaveCard, 0, 0, cw, ch);
        NineUi.TL(o.leaveTitle.rectTransform, 44, 40, cw - 88, 56);
        NineUi.TL(o.leaveBody.rectTransform, 44, 112, cw - 88, 120);
        float bw = (cw - 88 - 16) / 2;
        NineUi.TL(o.leaveNo.Rt, 44, ch - 40 - 78, bw, 78);
        NineUi.TL(o.leaveYes.Rt, 44 + bw + 16, ch - 40 - 78, bw, 78);
    }
}
