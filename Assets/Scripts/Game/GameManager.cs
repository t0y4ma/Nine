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
    // ラウンドの選択時間(秒)。ホストが設定画面のスライダーで変更する。
    // 30秒~5分を15秒刻みで選べ、0は無制限を表す。
    public const int ROUND_TIME_DEFAULT = 45;
    public const int ROUND_TIME_MIN = 30;
    public const int ROUND_TIME_MAX = 300;
    public const int ROUND_TIME_STEP = 15;
    [SyncVar] public int RoundTimeLimit = ROUND_TIME_DEFAULT;
    private const float ROUND_TIME_CHMIN = 3f; // 全員が確定した後、残り時間をこの秒数まで短縮する
    private const float ROUND_TIME_SEND_INTERVAL = 0.1f;

    // カード枚数の上限。「ハゲタカの餌食」(15枚)を包摂できる範囲とする。
    // これ以上増やすと、人数が多い場合に使用済み一覧が判読不能な大きさまで縮む。
    public const int CARD_COUNT_LIMIT = 15;
    [SyncVar] public int CARDCOUNT = 9;
    [SyncVar] public int ScoringMode = 0; // 0=ラウンド勝利で1pt、1=相手が出したカード数字の合計をpt、2=得点カード(ハゲタカのえじき)
    public const int SCORING_FIXED = 0;
    public const int SCORING_SUM = 1;
    public const int SCORING_POINT_CARDS = 2;

    // ===== 得点カード(ハゲタカのえじき)モード =====
    // 毎ラウンド得点カードを1枚めくり、単独で最大(マイナスなら単独で最小)の数字を出した人が取る。
    // 同じ数字を出した人同士は無効(バッティング)。全員無効なら次のラウンドへ持ち越す。
    // 今のラウンドの得点カード
    [SyncVar] public int currentPointCard;
    // 前のラウンドから持ち越している得点カード(古い順)。
    // 合計ではなくカードそのものを持つ(表示側で1枚ずつ並べるため)。
    public readonly SyncList<int> carriedPointCards = new();

    private int CarriedPointsTotal()
    {
        int sum = 0;
        foreach (var v in carriedPointCards) sum += v;
        return sum;
    }
    // 山札の残り枚数(今めくった分を除く)
    [SyncVar] public int pointCardsLeft;
    // 山札(サーバーのみ)。ゲーム開始時にシャッフルし、ラウンドごとに先頭から使う
    private readonly List<int> pointDeck = new();

    // 得点カードの山を作る。カード枚数の1/3をマイナス、残りをプラスにする。
    // 15枚なら -5~-1 と +1~+10 で、ハゲタカのえじきと同じ構成になる。
    [Server]
    private void BuildPointDeck()
    {
        pointDeck.Clear();
        int negatives = CARDCOUNT / 3;
        int positives = CARDCOUNT - negatives;
        for (int v = 1; v <= negatives; v++) pointDeck.Add(-v);
        for (int v = 1; v <= positives; v++) pointDeck.Add(v);
        for (int i = pointDeck.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (pointDeck[i], pointDeck[j]) = (pointDeck[j], pointDeck[i]);
        }
    }

    public static string FormatPoints(int v) => v > 0 ? "+" + v : v.ToString();
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

        // ゲーム中の部屋に戻ってきた場合、このオブジェクトが届くのは入室より後になる。
        // その時点ではまだ「ゲーム中」と判定できずロビー表示になっているので、ここで画面を合わせ直す。
        var ui = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (ui != null)
        {
            ui.RefreshLobbyPanels();
            if (inProgress)
            {
                ui.RefreshBoardViews();
                ui.RequestFullLayoutRebuild();
                if (ScoringMode == SCORING_POINT_CARDS) ui.ShowPointCard(currentPointCard, carriedPointCards.ToArray(), pointCardsLeft);
            }
        }
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
            // 1行分をパースして渡す(形式はResolveRoundを参照)
            if (!UIEventsManager.TryParseHistoryEntry(newItem, out int roundNumber,
                    out List<int> cards, out List<int> winners, out string resultLabel))
                return;
            // パネルが開いたまま行が増えると、追加分だけ
            // 幅の計算基準が違ってサイズが揃わない。
            // 開いている場合は全体を作り直す。
            if (uiManager.IsHistoryPanelOpen()) uiManager.RebuildHistoryPanel();
            else uiManager.AppendHistoryLine(roundNumber, cards, winners, resultLabel);
        }
        else if (op == SyncList<string>.Operation.OP_CLEAR)
        {
            uiManager.ClearHistoryPanel();
        }
    }

// 設定変更(カード枚数・得点方式)。ゲーム開始前、ホストのみが呼び出せる想定。
    [Server]
    public void UpdateSettings(int newCardCount, int newScoringMode, int newMaxPlayers, int newRoundTimeLimit)
    {
        if (inProgress) return;
        if (room != null) room.RemoveEmptySeats();
        newCardCount = Mathf.Clamp(newCardCount, 3, CARD_COUNT_LIMIT);
        newScoringMode = Mathf.Clamp(newScoringMode, SCORING_FIXED, SCORING_POINT_CARDS);
        // 上限は、既に参加している人数を下回らないようにする(既存プレイヤーが弾き出されないため)
        int currentPlayerCount = room != null ? room.playerComponents.Count : 0;
        newMaxPlayers = Mathf.Clamp(newMaxPlayers, Mathf.Max(2, currentPlayerCount), MAX_PLAYERS_LIMIT);

        CARDCOUNT = newCardCount;
        ScoringMode = newScoringMode;
        MaxPlayers = newMaxPlayers;
        // 0以下は無制限。それ以外は範囲内に収める
        RoundTimeLimit = newRoundTimeLimit <= 0
            ? 0
            : Mathf.Clamp(newRoundTimeLimit, ROUND_TIME_MIN, ROUND_TIME_MAX);

        // 既存プレイヤーの手札状態を新しいカード枚数に合わせて作り直す
        used_Players.Clear();
        int playerCount = room.playerComponents.Count;
        for (int i = 0; i < playerCount; i++)
        {
            for (int c = 0; c < CARDCOUNT; c++) used_Players.Add(false);
        }

        foreach (var p in room.playerComponents)
        {
            if (p == null) continue;
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

    // 席を取り除くときに、その席のデータも取り除く。
    // これをしないと、抜けた人の枠が残ったまま新しい枠が足され、
    // 一覧に存在しないプレイヤーが表示されてしまう。
    [Server]
    public void RemovePlayerData(int idx)
    {
        if (idx < 0) return;

        for (int c = CARDCOUNT - 1; c >= 0; c--)
        {
            int flat = idx * CARDCOUNT + c;
            if (flat < used_Players.Count) used_Players.RemoveAt(flat);
        }
        if (idx < turncards.Count) turncards.RemoveAt(idx);
        if (idx < roundWins.Count) roundWins.RemoveAt(idx);
        if (idx < lastRevealedPicks.Count) lastRevealedPicks.RemoveAt(idx);
    }

    // 空席に本人が戻ってきたとき、手札の使用状況を記録から復元する。
    // 空席だった間のカードも(ランダム提出で)公開済みの記録に残っているので、それを使う。
    // 抜ける前にこのラウンドのカードを確定していた場合は、その確定も引き継ぐ。
    [Server]
    public void RestoreSeat(int idx)
    {
        if (room == null || idx < 0 || idx >= room.playerComponents.Count) return;
        var pl = room.playerComponents[idx];
        if (pl == null) return;

        for (int c = 0; c < CARDCOUNT && c < pl.used.Count; c++)
        {
            int flat = idx * CARDCOUNT + c;
            pl.used[c] = flat < used_Players.Count && used_Players[flat];
        }

        int confirmed = idx < turncards.Count ? turncards[idx] - 1 : -1;
        if (confirmed >= 0 && confirmed < pl.used.Count)
        {
            pl.used[confirmed] = true;
            pl.isReadytoTurn = true;
        }
        RpcRefreshBoard();
    }

    // 空席を除いた全員が条件を満たしているか。
    // 空席を含めると、抜けた人は二度と確定もNextもしないので、判定が永遠に成立しなくなる。
    private bool AllPresent(System.Func<Player, bool> condition)
    {
        bool any = false;
        foreach (var p in room.playerComponents)
        {
            if (p == null) continue;
            any = true;
            if (!condition(p)) return false;
        }
        return any;
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
        // 前のゲームで抜けたまま戻らなかった人の空席を片付けてから始める
        room.RemoveEmptySeats();
        inProgress = true;
        for (int i = 0; i < used_Players.Count; i++) used_Players[i] = false;
        for (int i = 0; i < turncards.Count; i++) turncards[i] = 0;
        for (int i = 0; i < roundWins.Count; i++) roundWins[i] = 0;
        for (int i = 0; i < lastRevealedPicks.Count; i++) lastRevealedPicks[i] = 0;
        roundsPlayed = 0;
        roundHistoryLog.Clear();
        currentPointCard = 0;
        carriedPointCards.Clear();
        if (ScoringMode == SCORING_POINT_CARDS) BuildPointDeck();

        foreach (var playerCom in room.playerComponents)
        {
            if (playerCom == null) continue;
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
        bool pointMode = ScoringMode == SCORING_POINT_CARDS && roundsPlayed < pointDeck.Count;
        if (pointMode)
        {
            currentPointCard = pointDeck[roundsPlayed];
            pointCardsLeft = pointDeck.Count - (roundsPlayed + 1);
        }
        // 得点カードはRpcの引数でも渡す(SyncVarより先にRpcが届いても表示できるように)
        RpcRoundStartCutIn(roundsPlayed + 1, CARDCOUNT, pointMode, currentPointCard, carriedPointCards.ToArray(), pointCardsLeft);
        StartCoroutine(RoundTimerRoutine());
    }

    [ClientRpc]
    private void RpcRoundStartCutIn(int roundNumber, int totalRounds, bool pointMode, int pointCard, int[] carriedCards, int cardsLeft)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.ShowRoundCutIn(roundNumber, totalRounds,
            pointMode ? "Card " + FormatPoints(pointCard) : null);
        // ゲーム開始時は盤面を作った直後なので、画面サイズに合わせてレイアウトを組み直す。
        // (StartGameの盤面更新Rpcの後に届くので、この時点でカードは揃っている)
        if (roundNumber == 1) uiManager?.RequestFullLayoutRebuild();
        // 新ラウンドが始まったら前ラウンドの結果表示は消す
        uiManager?.HideRoundResultPopup();
        // 前ラウンドの結果表示が残り続けないよう、新しいラウンドの開始時にクリアする
        uiManager?.ShowResult("");
        // 得点カードモードでは、「今出したカード」の横に今の得点カードを出しておく
        if (pointMode) uiManager?.ShowPointCard(pointCard, carriedCards, cardsLeft);
    }

    // ラウンドの制限時間を管理する。全員が確定した時点で残り時間をROUND_TIME_CHMIN秒まで短縮し、
    // 0になったら(誰かが未確定でも)ラウンドを強制終了して公開する。
    // 無制限の場合は時間切れが起きず、全員の確定後の短縮だけで終わる。
    [Server]
    private System.Collections.IEnumerator RoundTimerRoutine()
    {
        bool unlimited = RoundTimeLimit <= 0;
        float remaining = unlimited ? float.PositiveInfinity : RoundTimeLimit;
        // 残り時間バーの基準。無制限のときは、短縮後の数秒をバーで見せるために短縮時間を基準にする
        // (確定待ちの間は満タンのまま表示される)
        float barTotal = unlimited ? ROUND_TIME_CHMIN : RoundTimeLimit;
        float lastSent = -1f;
        RpcRoundTimer(barTotal, barTotal);

        while (remaining > 0f)
        {
            if (AllPresent(p => p.isReadytoTurn))
            {
                remaining = Mathf.Min(remaining, ROUND_TIME_CHMIN);
            }

            // 無制限で待っている間は、送る値が変わらないので送信しない
            if (!float.IsInfinity(remaining)
                && (lastSent < 0 || Mathf.Abs(lastSent - remaining) >= ROUND_TIME_SEND_INTERVAL))
            {
                lastSent = remaining;
                RpcRoundTimer(Mathf.Max(0, remaining), barTotal);
            }

            yield return null;
            remaining -= Time.deltaTime;
        }

        RpcRoundTimer(0, barTotal);

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
        foreach (var p in room.playerComponents) if (p != null) p.isReadyForNextRound = false;

        float elapsed = 0f;
        float lastSent = -1f;
        while (elapsed < ROUND_TRANSITION_DELAY)
        {
            if (AllPresent(p => p.isReadyForNextRound))
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
            // 以前は抜けた人もここでfalseに戻していたため、次のラウンド以降
            // 「全員確定」「全員Next」が成立せず、毎回制限時間いっぱい待たされていた。
            if (playerCom == null) continue;
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

            // 空席(抜けたプレイヤー)も、時間切れの人と同じく未使用カードからランダムに出す。
            // 空席にはPlayerが無いので、手札の状態は公開済みの記録(used_Players)で判断する。
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
                    && room.playerComponents[i] != null
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
                if (p != null && chosen < p.used.Count) p.used[chosen] = true;   // 空席なら記録側だけ
            }
        }

        // このラウンドで選んだ値を公開する(全員が選び終わったこのタイミングで初めて公開)
        for (int i = 0; i < playedCards.Count; i++)
        {
            if (playedCards[i] <= 0) continue;
            lastRevealedPicks[i] = playedCards[i];
            // 使用済み一覧もここで初めて更新する
            int ci = playedCards[i] - 1;
            if (ci >= 0 && ci < CARDCOUNT) used_Players[i * CARDCOUNT + ci] = true;
        }

        // 勝者(得点を得た人)はサーバーがここで決め、表示側はそれをそのまま使う。
        // (表示側で「最大値の人」を探すと、得点カードモードのように
        //  最小値や2番目の人が勝つ場合に正しく強調できない)
        var winners = new List<int>();
        string resultLabel;    // 履歴の右端に出す変動
        string message;        // 結果ポップアップ/ステータスの文言
        string winnerLabel;    // 結果ポップアップで勝者のカードの下に出す文字

        if (ScoringMode == SCORING_POINT_CARDS)
            ResolvePointCardRound(playedCards, winners, out resultLabel, out message, out winnerLabel);
        else
            ResolveHighestCardRound(playedCards, winners, out resultLabel, out message, out winnerLabel);

        roundsPlayed++;

        // 履歴ログの1行を、クライアント側でリッチUIに変換できるよう構造化した形式で記録する。
        // 形式: "roundNumber|card0,card1,...|winnerId|tie|resultLabel|winner0,winner1,..."
        // (6番目の勝者一覧は後から追加。古い形式の行は表示側が最大値で判断する)
        int winnerId = winners.Count > 0 ? winners[0] : -1;
        bool tie = winners.Count > 1;
        var cardsCsv = string.Join(",", playedCards);
        roundHistoryLog.Add(roundsPlayed + "|" + cardsCsv + "|" + winnerId + "|" + (tie ? "1" : "0") + "|"
            + resultLabel + "|" + string.Join(",", winners));

        RpcRoundResult("Round " + roundsPlayed + "/" + CARDCOUNT + ": " + message,
            lastRevealedPicks.ToArray(), winners.ToArray(), winnerLabel);
        // 公開値を明示的に渡すことで、遷移が始まった直後から表示されるようにする
        RpcRevealBoard(lastRevealedPicks.ToArray(), roundWins.ToArray());
    }

    // 通常ルール: 一番大きい数字を出した人が勝つ。
    // 同点でも「勝ちなし」にはせず、最大値の全員が同じ得点を得る。
    // ただし全員が同じカードを出した場合は引き分けで、誰も得点しない。
    [Server]
    private void ResolveHighestCardRound(List<int> playedCards, List<int> winners,
        out string resultLabel, out string message, out string winnerLabel)
    {
        int best = 0;
        for (int i = 0; i < playedCards.Count; i++)
            if (playedCards[i] > best) best = playedCards[i];
        for (int i = 0; i < playedCards.Count; i++)
            if (playedCards[i] > 0 && playedCards[i] == best) winners.Add(i);

        // 提出した人数を数える(未提出は除く)
        int submittedCount = 0;
        for (int i = 0; i < playedCards.Count; i++) if (playedCards[i] > 0) submittedCount++;

        // 全員が同じカードを出した場合は引き分け。誰も得点しない。
        // (全員勝者にすると、単に全員に点が入るだけで勝負にならない)
        bool allSame = submittedCount > 1 && winners.Count == submittedCount;
        winnerLabel = "WIN";

        if (winners.Count == 0 || allSame)
        {
            winners.Clear();   // 強調表示もしない
            resultLabel = allSame ? "Tie" : "";
            message = "tie";
            return;
        }

        int pointsToAdd = 1;
        if (ScoringMode == SCORING_SUM)
        {
            // 最大値でなかったプレイヤーが出したカードの合計を得点にする。
            // 同点の場合、勝者それぞれが同じだけ得る。
            int sum = 0;
            for (int i = 0; i < playedCards.Count; i++)
                if (!winners.Contains(i)) sum += playedCards[i];
            pointsToAdd = Mathf.Max(1, sum);
        }
        foreach (var w in winners) roundWins[w] = roundWins[w] + pointsToAdd;

        // 誰が勝ったかはカード側の強調表示で分かるので、履歴には変動ptだけを出す。
        // (連名にすると人数が増えたとき文字が入りきらなくなる)
        resultLabel = "+" + pointsToAdd + "pt";
        message = winners.Count > 1 ? "multiple winners" : "Player " + winners[0] + " wins the round";
    }

    // 得点カード(ハゲタカのえじき)ルール。
    // ・同じ数字を出した人同士は無効(バッティング)。
    // ・残った数字のうち、場の得点(今回の得点カード+持ち越し)がプラスなら一番大きい数字、
    //   マイナスなら一番小さい数字を出した人が、場の得点をすべて取る。
    // ・全員が無効なら、場の得点を次のラウンドへ持ち越す(最終ラウンドなら誰も取らない)。
    [Server]
    private void ResolvePointCardRound(List<int> playedCards, List<int> winners,
        out string resultLabel, out string message, out string winnerLabel)
    {
        int pot = currentPointCard + CarriedPointsTotal();

        var counts = new Dictionary<int, int>();
        foreach (var v in playedCards)
        {
            if (v <= 0) continue;
            counts[v] = counts.TryGetValue(v, out int c) ? c + 1 : 1;
        }

        int taker = -1;
        for (int i = 0; i < playedCards.Count; i++)
        {
            int v = playedCards[i];
            if (v <= 0 || counts[v] != 1) continue;   // バッティングした数字は無効
            if (taker < 0) { taker = i; continue; }
            bool better = pot >= 0 ? v > playedCards[taker] : v < playedCards[taker];
            if (better) taker = i;
        }

        winnerLabel = FormatPoints(pot) + "pt";

        if (taker >= 0)
        {
            roundWins[taker] = roundWins[taker] + pot;
            winners.Add(taker);
            carriedPointCards.Clear();
            resultLabel = FormatPoints(pot) + "pt";
            message = "Player " + taker + " takes " + FormatPoints(pot) + "pt";
            return;
        }

        // 誰も取れなかった
        bool lastRound = roundsPlayed + 1 >= CARDCOUNT;
        if (lastRound)
        {
            carriedPointCards.Clear();
            resultLabel = "Lost " + FormatPoints(pot);
            message = "all cards clashed, " + FormatPoints(pot) + " is discarded";
        }
        else
        {
            // 今回の得点カードを持ち越しの列に加える(次のラウンドの得点カードと一緒に取り合う)
            carriedPointCards.Add(currentPointCard);
            resultLabel = "Carry " + FormatPoints(pot);
            message = "all cards clashed, " + FormatPoints(pot) + " carries over";
        }
    }

    [ClientRpc]
    private void RpcRoundResult(string message, int[] picks, int[] winners, string winnerLabel)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        if (uiManager == null) return;

        uiManager.ShowResult(message);
        // 結果を画面中央に大きく表示する(遷移時間のあいだだけ)
        // picksはRpcの引数で受け取る。
        // SyncListを参照すると、同期が間に合わず空のまま表示されることがある。
        uiManager.ShowRoundResultPopup(picks, winners, message, winnerLabel,
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

        // 得点カードモードでは合計がマイナスになり得るので、最小値から比べる
        int best = int.MinValue;
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
            if (p == null) continue;
            p.isReadyToStart = false;
            p.isReadytoTurn = false;
            p.isReadyForNextRound = false;
        }
        RefreshLobbyStatus();

        // 最終スコア一覧を組み立てて渡す(誰が何ptで勝ったかが分かるようにする)
        var scoreSb = new System.Text.StringBuilder();
        for (int i = 0; i < finalScores.Count; i++)
        {
            string pname = (i < room.playerComponents.Count && room.playerComponents[i] != null
                            && !string.IsNullOrEmpty(room.playerComponents[i].playerName))
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
        foreach (var p in room.playerComponents) if (p != null && p.isReadyToStart) ready++;
        readyCount = ready;
        totalPlayerCount = room.PresentCount;
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
