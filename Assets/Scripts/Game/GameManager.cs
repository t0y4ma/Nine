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

        // Callbackは購読後の変更しか拾わない。
        // 後から入室した場合や、スポーン時点で既に履歴がある場合に
        // 表示が空のままになるため、既存分をここで反映する。
        for (int i = 0; i < roundHistoryLog.Count; i++)
            OnRoundHistoryChanged(SyncList<string>.Operation.OP_ADD, i, null, roundHistoryLog[i]);
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
            uiManager.AppendHistoryLine(roundNumber, cards, winnerId, tie, parts[4]);
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
            for (int i = 0; i < playerCom.used.Count; i++) playerCom.used[i] = false;
        }

        RpcRefreshBoard();
        StartRound();
    }

    [Server]
    public bool UseCard(int id, int cardindex)
    {
        if (used_Players.Count / CARDCOUNT <= id) return false;
        if (used_Players[id * CARDCOUNT + cardindex]) return false;
        if (turncards[id] != 0) return false;

        used_Players[id * CARDCOUNT + cardindex] = true;
        turncards[id] = cardindex + 1;

        var actingPlayer = room.playerComponents[id];
        actingPlayer.isReadytoTurn = true; // "確定済み"のシグナルのみ公開(値は非公開)。ラウンド終了はタイマーが判断する

        RpcRefreshMyHand(); // 自分の手札ビューだけ更新(他人には見せない)

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

            var candidates = new List<int>();
            for (int c = 0; c < CARDCOUNT; c++)
            {
                if (!used_Players[i * CARDCOUNT + c]) candidates.Add(c);
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

        int best = -1;
        int winnerId = -1;
        bool tie = false;

        for (int i = 0; i < playedCards.Count; i++)
        {
            if (playedCards[i] > best) { best = playedCards[i]; winnerId = i; tie = false; }
            else if (playedCards[i] == best) tie = true;

            // このラウンドで選んだ値を公開する(全員が選び終わったこのタイミングで初めて公開)
            if (playedCards[i] > 0) lastRevealedPicks[i] = playedCards[i];
        }

        int pointsGainedThisRound = 0;
        if (!tie && winnerId >= 0)
        {
            int pointsToAdd = 1;
            if (ScoringMode == 1)
            {
                // 相手(勝者以外)が出したカードの数字の合計を得点にする
                int sum = 0;
                for (int i = 0; i < playedCards.Count; i++)
                {
                    if (i != winnerId) sum += playedCards[i];
                }
                pointsToAdd = Mathf.Max(1, sum); // 念のため最低1pt保証
            }
            roundWins[winnerId] = roundWins[winnerId] + pointsToAdd;
            pointsGainedThisRound = pointsToAdd;
        }

        roundsPlayed++;

        // 履歴ログの1行を、クライアント側でリッチUIに変換できるよう構造化した形式で記録する。
        // 形式: "roundNumber|card0,card1,...|winnerId|tie|resultLabel"
        string resultLabel;
        if (tie) resultLabel = "Tie";
        else if (winnerId >= 0)
        {
            string winnerName = (winnerId < room.playerComponents.Count && !string.IsNullOrEmpty(room.playerComponents[winnerId].playerName))
                ? room.playerComponents[winnerId].playerName
                : ("P" + winnerId);
            resultLabel = winnerName + " +" + pointsGainedThisRound + "pt";
        }
        else resultLabel = "";

        var cardsCsv = string.Join(",", playedCards);
        roundHistoryLog.Add(roundsPlayed + "|" + cardsCsv + "|" + winnerId + "|" + (tie ? "1" : "0") + "|" + resultLabel);

        RpcRoundResult(roundsPlayed, CARDCOUNT, winnerId, tie);
        // 公開値を明示的に渡すことで、遷移が始まった直後から表示されるようにする
        RpcRevealBoard(lastRevealedPicks.ToArray(), roundWins.ToArray());
    }

    [ClientRpc]
    private void RpcRoundResult(int roundNumber, int totalRounds, int winnerId, bool tie)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

        string message = tie
            ? ("Round " + roundNumber + "/" + totalRounds + ": tie")
            : ("Round " + roundNumber + "/" + totalRounds + ": Player " + winnerId + " wins the round");
        uiManager.ShowResult(message);
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

        uiManager.RefreshAllCardView(used_Players.ToList(), CARDCOUNT);
        uiManager.RefreshRoundResultPanel();
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

        uiManager.RefreshAllCardView(used_Players.ToList(), CARDCOUNT);
        uiManager.RefreshRoundResultPanel();
    }
}
