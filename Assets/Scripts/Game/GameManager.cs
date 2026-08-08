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
    private int roundsPlayed = 0;

    private const float ROUND_TRANSITION_DELAY = 2.5f; // 最大の緩衝時間(秒)。全員がNextを押せばこれより早く進む
    private const float TRANSITION_PROGRESS_SEND_INTERVAL = 0.1f; // 進捗バー送信間隔(秒)
    private const float ROUND_TIME_LIMIT = 45f; // ラウンドの制限時間(30~60秒の間)
    private const float ROUND_TIME_CHMIN = 3f; // 全員が確定した後、残り時間をこの秒数まで短縮する
    private const float ROUND_TIME_SEND_INTERVAL = 0.1f;

    [SyncVar] public int CARDCOUNT = 9;
    [SyncVar] public int ScoringMode = 0; // 0=ラウンド勝利で1pt、1=相手が出したカード数字の合計をpt
    [SyncVar(hook = nameof(OnInProgressChanged))] public bool inProgress;
    [SyncVar(hook = nameof(OnLobbyStatusChanged))] public int readyCount;
    [SyncVar(hook = nameof(OnLobbyStatusChanged))] public int totalPlayerCount;

    [Server]
    public void DeleteMatch()
    {
        NetworkServer.Destroy(gameObject);
    }

// 設定変更(カード枚数・得点方式)。ゲーム開始前、ホストのみが呼び出せる想定。
    [Server]
    public void UpdateSettings(int newCardCount, int newScoringMode)
    {
        if (inProgress) return;
        newCardCount = Mathf.Clamp(newCardCount, 3, 20);
        newScoringMode = Mathf.Clamp(newScoringMode, 0, 1);

        CARDCOUNT = newCardCount;
        ScoringMode = newScoringMode;

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

        for (int i = 0; i < turncards.Count; i++) turncards[i] = 0;
        for (int i = 0; i < lastRevealedPicks.Count; i++) lastRevealedPicks[i] = 0;

        foreach (var playerCom in room.playerComponents)
        {
            playerCom.isReadytoTurn = false;
            playerCom.isReadyForNextRound = false;
        }

        RpcRevealBoard();

        CheckGameOver();

        if (inProgress) StartRound();
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
        }

        roundsPlayed++;
        RpcRoundResult(roundsPlayed, CARDCOUNT, winnerId, tie);
        RpcRevealBoard();
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

[ClientRpc]
    private void RpcRevealBoard()
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

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
        for (int i = 0; i < roundWins.Count; i++)
        {
            if (roundWins[i] > best) { best = roundWins[i]; winnerId = i; tie = false; }
            else if (roundWins[i] == best) tie = true;
        }

        // 次のゲームに備えて、全員のReady状態をリセットする(再戦には全員の再Readyが必要)
        foreach (var p in room.playerComponents) p.isReadyToStart = false;
        RefreshLobbyStatus();

        RpcGameOver(winnerId, tie);
    }

    [ClientRpc]
    private void RpcGameOver(int winnerId, bool tie)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

        string message = tie ? "It's a tie!" : ("Player " + winnerId + " wins!");
        uiManager.ShowResult(message);
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
