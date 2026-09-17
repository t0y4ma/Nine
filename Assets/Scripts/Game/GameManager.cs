using System.Collections.Generic;
using UnityEngine;
using Mirror;
using System.Linq;

public class GameManager : NetworkBehaviour
{
    public Room room;
    public readonly SyncList<bool> used_Players = new();
    private List<int> turncards = new();
    public readonly SyncList<int> roundWins = new();
    public readonly SyncList<int> lastRevealedPicks = new();
    // ラウンド終了ごとに1行ずつ追加されるログ。「誰が何を出し、結果として誰が何ptで勝ったか」を記録する。
    // クライアント側の履歴パネルは、この変更(Callback)を購読して表示を追記していく。
    public readonly SyncList<string> roundHistoryLog = new();
    private int roundsPlayed = 0;

    private const float ROUND_TRANSITION_DELAY = 5.0f; // 最大の緩衝時間(秒)。全員がNextを押せばこれより早く進む
    private const float TRANSITION_PROGRESS_SEND_INTERVAL = 0.1f; // 進捗バー送信間隔(秒)
    private const float ROUND_TIME_LIMIT = 45f; // ラウンドの制限時間(30~60秒の間)
    private const float ROUND_TIME_CHMIN = 3f; // 全員が確定した後、残り時間をこの秒数まで短縮する
    private const float ROUND_TIME_SEND_INTERVAL = 0.1f;

    // カード枚数の上限。「ハゲタカの餌食」(15枚)を包摂できる範囲とする。
    // これ以上増やすと、人数が多い場合に使用済み一覧が判読不能な大きさまで縮む。
    public const int CARD_COUNT_LIMIT = 15;
    [SyncVar] public int CARDCOUNT = 9;
    [SyncVar] public int ScoringMode = 0; // 0=ラウンド勝利で1pt、1=相手が出したカード数字の合計をpt
    // 部屋の参加人数上限。
    // 「ハゲタカの餌食」(15枚・最大6人)を包摂できる範囲とする。
    // これ以上増やすと使用済みカード一覧が画面に収まらず判読できなくなる
    // (8人x20枚では必要高さ4836pxに対し画面高2160pxで完全に破綻していた)。
    public const int MAX_PLAYERS_LIMIT = 6;
    [SyncVar(hook = nameof(OnLobbyStatusChanged))] public int MaxPlayers = MAX_PLAYERS_LIMIT;
    [SyncVar(hook = nameof(OnInProgressChanged))] public bool inProgress;
    [SyncVar(hook = nameof(OnLobbyStatusChanged))] public int readyCount;
    [SyncVar(hook = nameof(OnLobbyStatusChanged))] public int totalPlayerCount;

    [Server]
    public void DeleteMatch()
    {
        NetworkServer.Destroy(gameObject);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // roundHistoryLogへの追加をUIの履歴パネルに反映するための購読。
        // SyncListのCallbackはクライアント側で明示的に登録する必要がある。
        roundHistoryLog.Callback += OnRoundHistoryChanged;

        // 全プレイヤーの使用済み状況も、変更を購読して盤面に反映する。
        // 購読が無いと、他プレイヤーがカードを出しても一覧が更新されない。
        used_Players.Callback += OnUsedPlayersChanged;
        // 得点と公開カードも購読しておく。
        // 通常はRpcで直接渡しているが、Rpcを取りこぼした場合や
        // 途中参加時に、SyncListの同期で表示が追いつくようにするため。
        roundWins.Callback += OnScoreOrPicksChanged;
        lastRevealedPicks.Callback += OnScoreOrPicksChanged;

        // Callbackは購読後の変更しか拾わない。
        // 後から入室した場合や、スポーン時点で既に履歴がある場合に
        // 表示が空のままになるため、既存分をここで反映する。
        for (int i = 0; i < roundHistoryLog.Count; i++)
            OnRoundHistoryChanged(SyncList<string>.Operation.OP_ADD, i, null, roundHistoryLog[i]);
    }

    public override void OnStopClient()
    {
        used_Players.Callback -= OnUsedPlayersChanged;
        roundWins.Callback -= OnScoreOrPicksChanged;
        lastRevealedPicks.Callback -= OnScoreOrPicksChanged;
        roundHistoryLog.Callback -= OnRoundHistoryChanged;
        base.OnStopClient();
    }

    private void OnScoreOrPicksChanged(SyncList<int>.Operation op, int index, int oldItem, int newItem)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.RefreshBoardViews();
    }

    private void OnUsedPlayersChanged(SyncList<bool>.Operation op, int index, bool oldItem, bool newItem)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.RefreshBoardViews();
    }

    private void OnRoundHistoryChanged(SyncList<string>.Operation op, int index, string oldItem, string newItem)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

        if (op == SyncList<string>.Operation.OP_ADD)
        {
            // "roundNumber|card0,card1,...|winnerId|tie|resultLabel" をパースして渡す
            var parts = newItem.Split('|');
            if (parts.Length < 5) return;
            int roundNumber = 0; int.TryParse(parts[0], out roundNumber);
            var cards = new List<int>();
            if (!string.IsNullOrEmpty(parts[1]))
            {
                foreach (var s in parts[1].Split(','))
                {
                    int v = 0; int.TryParse(s, out v);
                    cards.Add(v);
                }
            }
            int winnerId = -1; int.TryParse(parts[2], out winnerId);
            bool tie = parts[3] == "1";
            // パネルが開いたまま行が増えると、追加分だけ
            // 幅の計算基準が違ってサイズが揃わない。
            // 開いている場合は全体を作り直す。
            if (uiManager.IsHistoryPanelOpen()) uiManager.RebuildHistoryPanel();
            else uiManager.AppendHistoryLine(roundNumber, cards, winnerId, tie, parts[4]);
        }
        else if (op == SyncList<string>.Operation.OP_CLEAR)
        {
            uiManager.ClearHistoryPanel();
        }
    }

// 設定変更(カード枚数・得点方式)。ゲーム開始前、ホストのみが呼び出せる想定。
    [Server]
    public void UpdateSettings(int newCardCount, int newScoringMode, int newMaxPlayers)
    {
        if (inProgress) return;
        newCardCount = Mathf.Clamp(newCardCount, 3, CARD_COUNT_LIMIT);
        newScoringMode = Mathf.Clamp(newScoringMode, 0, 1);
        // 上限は、既に参加している人数を下回らないようにする(既存プレイヤーが弾き出されないため)
        int currentPlayerCount = room != null ? room.playerComponents.Count : 0;
        newMaxPlayers = Mathf.Clamp(newMaxPlayers, Mathf.Max(2, currentPlayerCount), MAX_PLAYERS_LIMIT);

        CARDCOUNT = newCardCount;
        ScoringMode = newScoringMode;
        MaxPlayers = newMaxPlayers;

        // 既存プレイヤーの手札状態を新しいカード枚数に合わせて作り直す
        used_Players.Clear();
        int playerCount = room.playerComponents.Count;
        for (int i = 0; i < playerCount; i++)
        {
            for (int c = 0; c < CARDCOUNT; c++) used_Players.Add(false);
        }

        foreach (var p in room.playerComponents)
        {
            p.used.Clear();
            for (int c = 0; c < CARDCOUNT; c++) p.used.Add(false);

            // usedだけでなくcardsリストも新しいCARDCOUNTに合わせて作り直す必要がある。
            // ここが漏れていたため、カード枚数を増やした後に新しく増えた分のカード
            // (例: 10枚目以降)を選んでConfirmしても、CmdUseCard内のcards.Countによる
            // 範囲チェックで弾かれ、提出できないというバグになっていた。
            p.cards.Clear();
            for (int c = 1; c <= CARDCOUNT; c++) p.cards.Add(c);
        }
    }

    [Server]
    public void AddPlayer()
    {
        for (int i = 0; i < CARDCOUNT; i++) used_Players.Add(false);
        turncards.Add(0);
        roundWins.Add(0);
        lastRevealedPicks.Add(0);
    }

    [Server]
    public void StartGame()
    {
        inProgress = true;
        for (int i = 0; i < used_Players.Count; i++) used_Players[i] = false;
        for (int i = 0; i < turncards.Count; i++) turncards[i] = 0;
        for (int i = 0; i < roundWins.Count; i++) roundWins[i] = 0;
        for (int i = 0; i < lastRevealedPicks.Count; i++) lastRevealedPicks[i] = 0;
        roundsPlayed = 0;
        roundHistoryLog.Clear();

        foreach (var playerCom in room.playerComponents)
        {
            playerCom.isReadytoTurn = false;
            playerCom.isReadyToStart = false;
            playerCom.pendingSelection = -1;
            for (int i = 0; i < playerCom.used.Count; i++) playerCom.used[i] = false;
        }

        RpcRefreshBoard();
        StartRound();
    }

    // 確定を取り消す。ラウンド解決前のみ有効。
    [Server]
    public void CancelCard(int id)
    {
        if (!inProgress) return;
        if (id < 0 || id >= turncards.Count) return;
        if (turncards[id] == 0) return;   // まだ出していない

        int cardindex = turncards[id] - 1;
        turncards[id] = 0;

        var pl = (room != null && id < room.playerComponents.Count) ? room.playerComponents[id] : null;
        if (pl != null)
        {
            pl.isReadytoTurn = false;
            if (cardindex >= 0 && cardindex < pl.used.Count) pl.used[cardindex] = false;
        }

        RpcRefreshMyHand();
        RpcRefreshBoard();
    }

    [Server]
    public bool UseCard(int id, int cardindex)
    {
        if (used_Players.Count / CARDCOUNT <= id) return false;
        if (used_Players[id * CARDCOUNT + cardindex]) return false;
        if (turncards[id] != 0) return false;

        // used_PlayersはSyncListで全員に見えるため、ここでは更新しない。
        // 更新するとラウンド終了前に「誰が何を出したか」が
        // 一覧から読み取れてしまう。公開はResolveRoundで行う。
        turncards[id] = cardindex + 1;

        var actingPlayer = room.playerComponents[id];
        actingPlayer.isReadytoTurn = true;
        actingPlayer.pendingSelection = -1;   // 確定したので選択状態は不要 // "確定済み"のシグナルのみ公開(値は非公開)。ラウンド終了はタイマーが判断する

        // 手札(自分のみ)と、盤面全体(誰が提出済みかの「?」表示)を更新する。
        // 以前はRpcRefreshMyHandだけだったため、他プレイヤーの提出が
        // 「今出したカード」に反映されなかった。
        RpcRefreshMyHand();
        RpcRefreshBoard();

        return true;
    }

    // ラウンド開始: カットインを表示し、制限時間タイマーを開始する
    [Server]
    private void StartRound()
    {
        RpcRoundStartCutIn(roundsPlayed + 1, CARDCOUNT);
        StartCoroutine(RoundTimerRoutine());
    }

    [ClientRpc]
    private void RpcRoundStartCutIn(int roundNumber, int totalRounds)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.ShowRoundCutIn(roundNumber, totalRounds);
        // 新ラウンドが始まったら前ラウンドの結果表示は消す
        uiManager?.HideRoundResultPopup();
        // 前ラウンドの結果表示が残り続けないよう、新しいラウンドの開始時にクリアする
        uiManager?.ShowResult("");
    }

    // ラウンドの制限時間を管理する。全員が確定した時点で残り時間をROUND_TIME_CHMIN秒まで短縮し、
    // 0になったら(誰かが未確定でも)ラウンドを強制終了して公開する。
    [Server]
    private System.Collections.IEnumerator RoundTimerRoutine()
    {
        float remaining = ROUND_TIME_LIMIT;
        float lastSent = -1f;
        RpcRoundTimer(remaining, ROUND_TIME_LIMIT);

        while (remaining > 0f)
        {
            if (room.playerComponents.Count > 0 && room.playerComponents.All(p => p.isReadytoTurn))
            {
                remaining = Mathf.Min(remaining, ROUND_TIME_CHMIN);
            }

            if (lastSent < 0 || remaining - lastSent <= -ROUND_TIME_SEND_INTERVAL || lastSent - remaining >= ROUND_TIME_SEND_INTERVAL)
            {
                lastSent = remaining;
                RpcRoundTimer(Mathf.Max(0, remaining), ROUND_TIME_LIMIT);
            }

            yield return null;
            remaining -= Time.deltaTime;
        }

        RpcRoundTimer(0, ROUND_TIME_LIMIT);

        StartCoroutine(EndTurnRoutine());
    }

    [ClientRpc]
    private void RpcRoundTimer(float remaining, float total)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.UpdateRoundTimerBar(total > 0 ? remaining / total : 0);
    }

    [ClientRpc]
    private void RpcRefreshMyHand()
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

        var localPlayer = uiManager.GetDebugOrLocalPlayer();
        if (localPlayer != null)
            uiManager.RefreshMyCardView(localPlayer.used.ToList(), localPlayer.used.Count);
    }

    // ラウンド結果を公開し、緩衝時間を置いてから次のラウンドへ移る
    [Server]
    private System.Collections.IEnumerator EndTurnRoutine()
    {
        ResolveRound(turncards);

        RpcRoundTransitionProgress(1f); // バーを満タン状態で表示開始

        // 次ラウンドへの遷移: 全員がNextを押すか、最大待機時間が経過するまで待つ
        foreach (var p in room.playerComponents) p.isReadyForNextRound = false;

        float elapsed = 0f;
        float lastSent = -1f;
        while (elapsed < ROUND_TRANSITION_DELAY)
        {
            if (room.playerComponents.Count > 0 && room.playerComponents.All(p => p.isReadyForNextRound))
                break;

            yield return null;
            elapsed += Time.deltaTime;

            if (lastSent < 0 || elapsed - lastSent >= TRANSITION_PROGRESS_SEND_INTERVAL)
            {
                lastSent = elapsed;
                RpcRoundTransitionProgress(Mathf.Clamp01(1f - elapsed / ROUND_TRANSITION_DELAY));
            }
        }

        RpcRoundTransitionProgress(0f);

        // ゲーム終了の判定・表示は、盤面をクリアする「前」に行う。
        // 以前は盤面をクリアしてロビー状態に戻した後にGameOverを表示していたため、
        // 最終ラウンドの結果(誰が何を出したか)が画面から消えた状態で結果発表される、
        // という分かりにくい挙動になっていた。
        bool gameEnded = (roundsPlayed >= CARDCOUNT);
        if (gameEnded)
        {
            CheckGameOver();
            yield break; // 盤面は最終ラウンドの状態のまま残す(次ゲーム開始時にStartGameがリセットする)
        }

        for (int i = 0; i < turncards.Count; i++) turncards[i] = 0;
        for (int i = 0; i < lastRevealedPicks.Count; i++) lastRevealedPicks[i] = 0;

        foreach (var playerCom in room.playerComponents)
        {
            playerCom.isReadytoTurn = false;
            playerCom.isReadyForNextRound = false;
        }

        RpcRevealBoard(new int[lastRevealedPicks.Count], roundWins.ToArray()); // カードは0=未提出に戻し、得点は維持

        StartRound();
    }

    [ClientRpc]
    private void RpcRoundTransitionProgress(float remainingFraction)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.UpdateTransitionBar(remainingFraction);
    }

    // ラウンド勝敗判定
    // 現在のルール: そのラウンドで一番大きい数字を出した人がラウンド勝ち（同点は無効）。
    // 全ラウンド終了時に一番ラウンド勝ち数が多い人が総合優勝。
    [Server]
    protected virtual void ResolveRound(List<int> playedCards)
    {
        // 選択しなかったプレイヤーには、未使用のカードからランダムに1枚を自動選択させる
        // (誰も選ばずtieで終わる、ということが起きないようにするため)
        for (int i = 0; i < playedCards.Count; i++)
        {
            if (playedCards[i] != 0) continue;

            // 抜けたプレイヤーは自動選択の対象外。常に0(未提出)のままにする。
            if (room != null && i < room.playerComponents.Count
                && room.playerComponents[i] != null && room.playerComponents[i].hasLeft)
                continue;

            // 確定はしていないが選択中のカードがあれば、それを出す。
            // (ランダムより本人の意図に近い)
            var selPl = (room != null && i < room.playerComponents.Count) ? room.playerComponents[i] : null;
            if (selPl != null && selPl.pendingSelection >= 0
                && selPl.pendingSelection < selPl.used.Count
                && !selPl.used[selPl.pendingSelection])
            {
                playedCards[i] = selPl.pendingSelection + 1;
                selPl.used[selPl.pendingSelection] = true;
                continue;
            }

            var candidates = new List<int>();
            for (int c = 0; c < CARDCOUNT; c++)
            {
                // used_Playersは公開時にしか更新しないので、
                // 自分の手札(Player.used)を基準に未使用カードを選ぶ
                bool alreadyUsed = (room != null && i < room.playerComponents.Count
                    && c < room.playerComponents[i].used.Count)
                    ? room.playerComponents[i].used[c]
                    : used_Players[i * CARDCOUNT + c];
                if (!alreadyUsed) candidates.Add(c);
            }
            if (candidates.Count == 0) continue; // 全カード使用済み(基本起こらないはず)

            int chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            used_Players[i * CARDCOUNT + chosen] = true;
            playedCards[i] = chosen + 1;

            if (i < room.playerComponents.Count)
            {
                var p = room.playerComponents[i];
                if (chosen < p.used.Count) p.used[chosen] = true;
            }
        }

        // 最大値を出したプレイヤーを全員求める。
        // 同点でも「勝ちなし」にはせず、最大値の全員が同じ得点を得る。
        int best = 0;
        for (int i = 0; i < playedCards.Count; i++)
            if (playedCards[i] > best) best = playedCards[i];

        var winners = new List<int>();
        for (int i = 0; i < playedCards.Count; i++)
        {
            if (playedCards[i] > 0 && playedCards[i] == best) winners.Add(i);
            // このラウンドで選んだ値を公開する(全員が選び終わったこのタイミングで初めて公開)
            if (playedCards[i] > 0)
            {
                lastRevealedPicks[i] = playedCards[i];
                // 使用済み一覧もここで初めて更新する
                int ci = playedCards[i] - 1;
                if (ci >= 0 && ci < CARDCOUNT) used_Players[i * CARDCOUNT + ci] = true;
            }
        }

        // 提出した人数を数える(抜けた人や未提出は除く)
        int submittedCount = 0;
        for (int i = 0; i < playedCards.Count; i++) if (playedCards[i] > 0) submittedCount++;

        // 全員が同じカードを出した場合は引き分け。誰も得点しない。
        // (全員勝者にすると、単に全員に点が入るだけで勝負にならない)
        bool allSame = submittedCount > 1 && winners.Count == submittedCount;

        bool tie = winners.Count > 1;
        int winnerId = winners.Count > 0 ? winners[0] : -1;

        int pointsGainedThisRound = 0;
        if (winners.Count > 0 && !allSame)
        {
            int pointsToAdd = 1;
            if (ScoringMode == 1)
            {
                // 最大値でなかったプレイヤーが出したカードの合計を得点にする。
                // 同点の場合、勝者それぞれが同じだけ得る。
                int sum = 0;
                for (int i = 0; i < playedCards.Count; i++)
                    if (!winners.Contains(i)) sum += playedCards[i];
                pointsToAdd = Mathf.Max(1, sum);
            }
            foreach (var w in winners) roundWins[w] = roundWins[w] + pointsToAdd;
            pointsGainedThisRound = pointsToAdd;
        }

        roundsPlayed++;

        // 履歴ログの1行を、クライアント側でリッチUIに変換できるよう構造化した形式で記録する。
        // 形式: "roundNumber|card0,card1,...|winnerId|tie|resultLabel"
        // 誰が勝ったかはカード側の強調表示で分かるので、
        // ここでは変動ptだけを出す。
        // 連名にすると人数が増えたとき文字が入りきらなくなる。
        string resultLabel = (winners.Count == 0 || allSame)
            ? (allSame ? "Tie" : "")
            : ("+" + pointsGainedThisRound + "pt");

        var cardsCsv = string.Join(",", playedCards);
        roundHistoryLog.Add(roundsPlayed + "|" + cardsCsv + "|" + winnerId + "|" + (tie ? "1" : "0") + "|" + resultLabel);

        RpcRoundResult(roundsPlayed, CARDCOUNT, winnerId, tie, lastRevealedPicks.ToArray(), allSame);
        // 公開値を明示的に渡すことで、遷移が始まった直後から表示されるようにする
        RpcRevealBoard(lastRevealedPicks.ToArray(), roundWins.ToArray());
    }

    [ClientRpc]
    private void RpcRoundResult(int roundNumber, int totalRounds, int winnerId, bool tie, int[] picks, bool allSameRound)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

        // 同点でも得点は入るので「引き分け」ではなく
        // 「複数人が勝った」という表現にする
        string message;
        if (allSameRound) message = "Round " + roundNumber + "/" + totalRounds + ": tie";
        else if (tie) message = "Round " + roundNumber + "/" + totalRounds + ": multiple winners";
        else message = "Round " + roundNumber + "/" + totalRounds + ": Player " + winnerId + " wins the round";
        uiManager.ShowResult(message);
        // 結果を画面中央に大きく表示する(遷移時間のあいだだけ)
        // picksはRpcの引数で受け取る。
        // SyncListを参照すると、同期が間に合わず空のまま表示されることがある。
        // 全員同じなら勝者なしとして渡す(強調表示もされない)
        uiManager.ShowRoundResultPopup(picks, allSameRound ? -1 : winnerId, tie, message,
            4f);   // 表示時間は4秒
    }

    // 公開されたカードをRpcの引数で直接渡す。
    // SyncList(lastRevealedPicks)の同期はRpcと到着順が保証されないため、
    // SyncListに依存すると「次のラウンドになってから前ラウンドのカードが出る」
    // という表示遅延が起きていた。
    [ClientRpc]
    private void RpcRevealBoard(int[] revealedPicks, int[] scores) // Mirrorの制約でRpcに省略可能引数は使えない
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

        // 公開カードと得点をRpcの引数で直接渡す。
        // SyncListの同期はRpcと到着順が保証されないため、表示にはこちらを優先する。
        if (revealedPicks != null) uiManager.SetRevealedPicksOverride(revealedPicks);
        if (scores != null) uiManager.SetScoresOverride(scores);



        // 時間切れでランダムに選ばれたカードも含め、自分の手札ビューを更新する
        var localPlayer = uiManager.GetDebugOrLocalPlayer();
        if (localPlayer != null)
            uiManager.RefreshMyCardView(localPlayer.used.ToList(), localPlayer.used.Count);

        // RefreshAllCardViewの中で「今出したカード」も更新される。
        // (RefreshRoundResultPanelは廃止済みのパネル用なので呼ばない)
        uiManager.RefreshAllCardView(used_Players.ToList(), CARDCOUNT);
    }

    [Server]
    private void CheckGameOver()
    {
        if (roundsPlayed < CARDCOUNT) return;

        inProgress = false;

        int best = -1;
        int winnerId = -1;
        bool tie = false;
        // roundWinsはこの後リセットされるため、表示用に最終スコアを控えておく
        var finalScores = new List<int>(roundWins);
        for (int i = 0; i < roundWins.Count; i++)
        {
            if (roundWins[i] > best) { best = roundWins[i]; winnerId = i; tie = false; }
            else if (roundWins[i] == best) tie = true;
        }

        // 次のゲームに備えて、プレイヤーの操作状態のみリセットする。
        // 盤面(used_Players/turncards/lastRevealedPicks/roundWins)は、最終ラウンドの結果を
        // 画面に残したままGameOverを表示するため、ここではクリアしない。
        // これらは次のゲーム開始時にStartGame()が確実にリセットする。
        foreach (var p in room.playerComponents)
        {
            p.isReadyToStart = false;
            p.isReadytoTurn = false;
            p.isReadyForNextRound = false;
        }
        RefreshLobbyStatus();

        // 最終スコア一覧を組み立てて渡す(誰が何ptで勝ったかが分かるようにする)
        var scoreSb = new System.Text.StringBuilder();
        for (int i = 0; i < finalScores.Count; i++)
        {
            string pname = (i < room.playerComponents.Count && !string.IsNullOrEmpty(room.playerComponents[i].playerName))
                ? room.playerComponents[i].playerName
                : ("Player " + i);
            scoreSb.AppendLine(pname + ": " + finalScores[i] + " pt");
        }

        RpcGameOver(winnerId, tie, best, scoreSb.ToString());
    }

    [ClientRpc]
    private void RpcGameOver(int winnerId, bool tie, int winningScore, string scoreBoard)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

        string headline = tie
            ? ("It's a tie! (" + winningScore + " pt)")
            : ("Player " + winnerId + " wins with " + winningScore + " pt!");
        // 結果はGameOverPanelに大きく表示するため、StatusText側には出さない。
        // (StatusTextに出すと、ロビーに戻った後も前ゲームの結果が残って見え続けてしまう)
        uiManager.ShowGameOverPanel("GAME OVER\n\n" + headline + "\n\n" + scoreBoard);
    }

    [Server]
    public void RefreshLobbyStatus()
    {
        if (room == null) return;

        int ready = 0;
        foreach (var p in room.playerComponents) if (p.isReadyToStart) ready++;
        readyCount = ready;
        totalPlayerCount = room.playerComponents.Count;
    }

    private void OnLobbyStatusChanged(int oldVal, int newVal)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.UpdateLobbyStatus(readyCount, totalPlayerCount);
    }

    private void OnInProgressChanged(bool oldVal, bool newVal)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.RefreshLobbyPanels();
    }

    [ClientRpc]
    private void RpcRefreshBoard()
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

        var localPlayer = uiManager.GetDebugOrLocalPlayer();
        if (localPlayer != null)
            uiManager.RefreshMyCardView(localPlayer.used.ToList(), localPlayer.used.Count);

        // RefreshAllCardViewの中で「今出したカード」も更新される。
        // (RefreshRoundResultPanelは廃止済みのパネル用なので呼ばない)
        uiManager.RefreshAllCardView(used_Players.ToList(), CARDCOUNT);
    }
}
