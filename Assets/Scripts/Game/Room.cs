using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class Room
{
    public GameManager gameManager;
    public List<NetworkConnectionToClient> players;

    // 席ごとのプレイヤー。indexがそのままplayerId(=得点や使用済みカードの並び)になる。
    // ゲーム中に抜けた人の席は削除せずnull(空席)にする。
    // 削除すると以降の席番号がずれて、得点や履歴の対応が壊れるため。
    public List<Player> playerComponents = new();

    // 席ごとの持ち主の識別子。抜けた人が戻ってきたとき、どの席に戻すかを決める。
    // 他人の席を奪えてしまわないよう、サーバー内だけで持ち、どのクライアントにも送らない。
    private readonly List<string> seatTokens = new();
    private string hostToken = "";

    private string password;
    public Guid matchId;
    public string roomId;

    // この部屋を作成したクライアントの接続。Start権限はサーバー側ではなくこの接続に紐付く。
    // 譲渡できるので「作成者」とは限らない。
    public NetworkConnectionToClient hostConnection;

    // 観戦者。席を持たず、ゲームには参加しないが、部屋の一員として盤面を見る。
    // 席(playerComponents)に入れないのは、席のindexが得点・使用済みカードの並びと対応しているため。
    // 観戦しているかどうかはPlayer.isSpectatorで表し、人数の数え方や表示はそれに従う。
    public readonly List<NetworkConnectionToClient> spectators = new();

    public int SpectatorCount => spectators.Count;

    // パスワード付きの部屋か(空のパスワードは内部で"****"として持つ)
    public bool HasPassword => password != "****";

    // 部屋一覧に出す要約。「参加人数|上限|観戦人数|対局中(0/1)|鍵(0/1)」
    public string Summary => PresentCount + "|" + gameManager.MaxPlayers + "|" + SpectatorCount + "|"
        + (gameManager.inProgress ? "1" : "0") + "|" + (HasPassword ? "1" : "0");

    // 部屋にいる全員(席のある人＋観戦者)のPlayer
    public IEnumerable<Player> AllMembers
    {
        get
        {
            foreach (var p in playerComponents) if (p != null) yield return p;
            foreach (var c in spectators)
            {
                var p = c.identity != null ? c.identity.GetComponent<Player>() : null;
                if (p != null) yield return p;
            }
        }
    }

    public Room(GameManager gameManager, string password)
    {
        this.gameManager = gameManager;
        this.gameManager.room = this;
        this.password = password;
        players = new List<NetworkConnectionToClient>();
    }

    // 席にいる(空席でない)プレイヤーの数
    public int PresentCount
    {
        get
        {
            int n = 0;
            foreach (var p in playerComponents) if (p != null) n++;
            return n;
        }
    }

    [Server]
    public void AddPlayer(NetworkConnectionToClient player, string token)
    {
        var playerCom = player.identity.GetComponent<Player>();
        spectators.Remove(player);
        playerCom.isSpectator = false;

        // 同じPlayerが既に席を持っていたら先に外す(二重登録で「幻影」が出るのを防ぐ)
        int existing = playerComponents.IndexOf(playerCom);
        if (existing >= 0) RemoveSeatAt(existing);

        token = token ?? "";
        playerCom.clientToken = token;
        if (player == hostConnection && !string.IsNullOrEmpty(token)) hostToken = token;

        int id = playerComponents.Count;
        playerComponents.Add(playerCom);
        seatTokens.Add(token);
        gameManager.AddPlayer();
        SeatPlayer(player, playerCom, id);
        NineDebugLog.Add(roomId, "参加: 席" + id + (player == hostConnection ? "(ホスト)" : ""));
        gameManager.RefreshLobbyStatus();
    }

    // 席にプレイヤーを座らせる。新規参加と、抜けた席への復帰の共通処理。
    [Server]
    private void SeatPlayer(NetworkConnectionToClient conn, Player playerCom, int id)
    {
        playerCom.Setup(this, id);
        playerCom.gameManager = gameManager;
        playerCom.isRoomHost = (conn == hostConnection);
        playerCom.isReadyToStart = false;
        playerCom.isReadytoTurn = false;
        playerCom.isReadyForNextRound = false;
        playerCom.pendingSelection = -1;
        playerCom.isSpectator = false;
        conn.identity.GetComponent<NetworkMatch>().matchId = matchId;
        if (!players.Contains(conn)) players.Add(conn);
    }

    // 観戦者として部屋に入れる。席は持たない。
    [Server]
    public void AddSpectator(NetworkConnectionToClient conn, string token)
    {
        var playerCom = conn.identity != null ? conn.identity.GetComponent<Player>() : null;
        if (playerCom == null) return;

        int existing = playerComponents.IndexOf(playerCom);
        if (existing >= 0) RemoveSeatAt(existing);

        playerCom.clientToken = token ?? "";
        playerCom.room = this;
        playerCom.gameManager = gameManager;
        playerCom.playerId = -1;
        playerCom.isSpectator = true;
        playerCom.isReadyToStart = false;
        playerCom.isReadytoTurn = false;
        playerCom.isReadyForNextRound = false;
        playerCom.pendingSelection = -1;
        playerCom.isRoomHost = (conn == hostConnection);
        playerCom.SetupSpectator(this);
        conn.identity.GetComponent<NetworkMatch>().matchId = matchId;
        if (!players.Contains(conn)) players.Add(conn);
        if (!spectators.Contains(conn)) spectators.Add(conn);

        NineDebugLog.Add(roomId, "観戦で参加");
        gameManager.RefreshLobbyStatus();
        // 観戦者は、各プレイヤーが今のラウンドで何を出したかを即座に見られる
        gameManager.SendSpectatorSnapshot(conn);
    }

    // 参加/観戦を自分で切り替える。ゲーム中は切り替えられない。
    // 戻り値: 切り替えに失敗した理由(成功ならnull)
    [Server]
    public string SetSpectating(NetworkConnectionToClient conn, bool spectate)
    {
        var playerCom = conn.identity != null ? conn.identity.GetComponent<Player>() : null;
        if (playerCom == null || !players.Contains(conn)) return "この部屋に入っていません。";
        if (gameManager != null && gameManager.inProgress) return "対局中は参加・観戦を切り替えられません。";
        if (playerCom.isSpectator == spectate) return null;

        if (spectate)
        {
            AddSpectator(conn, playerCom.clientToken);
            return null;
        }

        RemoveEmptySeats();
        // 参加人数の上限は、開始時にホストへ知らせる。ここでは席の総数(ハードリミット)だけ守る。
        if (playerComponents.Count >= GameManager.MAX_PLAYERS_LIMIT)
            return "席が埋まっています(最大" + GameManager.MAX_PLAYERS_LIMIT + "人)。";

        AddPlayer(conn, playerCom.clientToken);
        return null;
    }

    // ゲーム中に抜けた本人が戻ってきた場合、元の席に座り直させる。
    // 得点・使用済みカード・履歴は席(index)に紐付いているので、そのまま引き継がれる。
    [Server]
    private bool TryReclaimSeat(NetworkConnectionToClient conn, string token)
    {
        if (string.IsNullOrEmpty(token)) return false;

        for (int i = 0; i < playerComponents.Count; i++)
        {
            if (playerComponents[i] != null) continue;
            if (seatTokens[i] != token) continue;

            var playerCom = conn.identity.GetComponent<Player>();
            playerCom.clientToken = token;
            // 部屋を作った本人が、接続が変わって(再読み込み等)戻ってきた場合だけホスト権限を戻す。
            // 抜けている間に他の人へ譲られていたら、その人のままにする。
            if (token == hostToken && (hostConnection == null || !players.Contains(hostConnection)))
                hostConnection = conn;

            playerComponents[i] = playerCom;
            SeatPlayer(conn, playerCom, i);
            gameManager.RestoreSeat(i);
            NineDebugLog.Add(roomId, "復帰: 席" + i);
            gameManager.RefreshLobbyStatus();
            return true;
        }
        return false;
    }

    // 部屋から抜ける。
    // notifyClient: 本人に「部屋選択画面へ戻れ」と伝えるか。
    //   切断による退出では、もう送り先が無いのでfalseにする。
    [Server]
    public void RemovePlayer(NetworkConnectionToClient player, bool notifyClient)
    {
        var playerCom = player.identity != null ? player.identity.GetComponent<Player>() : null;
        int idx = playerCom != null ? playerComponents.IndexOf(playerCom) : -1;
        bool wasHost = (player == hostConnection);

        players.Remove(player);
        spectators.Remove(player);

        if (idx >= 0)
        {
            if (gameManager != null && gameManager.inProgress)
            {
                // ゲーム中は席を残して空席にする。
                // 空席の間は毎ラウンド未使用のカードからランダムに出す(時間切れと同じ扱い)。
                // このラウンドで確定済みのカードがあれば、それはそのまま出す。
                // 同じ部屋にJoinし直せば、この席に戻って続きから遊べる。
                playerComponents[idx] = null;
                NineDebugLog.Add(roomId, "退出: 席" + idx + "(空席として残す)");
            }
            else
            {
                RemoveSeatAt(idx);
            }
        }

        if (playerCom != null)
        {
            // 抜けたPlayerの状態を部屋に入る前に戻す。
            // (空席はnullで表すので、Player側に「抜けた」印を残す必要はない。
            //  残すと、そのPlayerが別の部屋に入ったときに状態が混ざる)
            playerCom.inRoom = false;
            playerCom.isRoomHost = false;
            playerCom.isReadyToStart = false;
            playerCom.isReadytoTurn = false;
            playerCom.isReadyForNextRound = false;
            playerCom.pendingSelection = -1;
            playerCom.isSpectator = false;
            playerCom.room = null;
            playerCom.gameManager = null;
            playerCom.myRoomId = "";
            if (player.identity != null)
                player.identity.GetComponent<NetworkMatch>().matchId = Guid.Empty;
            if (notifyClient) playerCom.TargetLeftRoom(player);
        }

        // 接続している人が誰もいなくなったら部屋ごと消す(観戦者だけでも残す)
        if (players.Count == 0) { DeleteRoom(); return; }

        // ホストが抜けたら、残っている人に引き継ぐ
        if (wasHost) PromoteNewHost();

        gameManager.RefreshLobbyStatus();
    }

    // ホスト権限を別の人に渡す。渡す相手は部屋にいる人なら誰でもよい(観戦者も含む)。
    [Server]
    public bool TransferHost(Player target)
    {
        if (target == null || target.connectionToClient == null) return false;
        var conn = target.connectionToClient;
        if (!players.Contains(conn)) return false;

        var old = hostConnection;
        hostConnection = conn;
        hostToken = target.clientToken ?? "";

        foreach (var p in AllMembers) p.isRoomHost = (p == target);
        NineDebugLog.Add(roomId, "ホストを引き継ぎ: " + (target.isSpectator ? "観戦者" : "席" + target.playerId));
        if (old != null && old.identity != null)
        {
            var oldPlayer = old.identity.GetComponent<Player>();
            if (oldPlayer != null && oldPlayer != target) oldPlayer.isRoomHost = false;
        }
        gameManager.RefreshLobbyStatus();
        return true;
    }

    // ホストが抜けたときの自動引き継ぎ。席順が早い人を優先し、いなければ観戦者に渡す。
    [Server]
    private void PromoteNewHost()
    {
        foreach (var p in playerComponents)
            if (p != null && p.connectionToClient != null) { TransferHost(p); return; }
        foreach (var c in spectators)
        {
            var p = c.identity != null ? c.identity.GetComponent<Player>() : null;
            if (p != null) { TransferHost(p); return; }
        }
        hostConnection = null;
        hostToken = "";
    }

    // 参加人数(席のある人)が設定の上限を超えていないか。開始時に確認する。
    public bool ParticipantsWithinLimit => PresentCount <= gameManager.MaxPlayers;

    // 席そのものを取り除き、後ろの席の番号を詰める(ゲーム外でのみ使う)
    [Server]
    private void RemoveSeatAt(int idx)
    {
        if (idx < 0 || idx >= playerComponents.Count) return;
        // データ削除はインデックスで行うので、リストから外す前に呼ぶ
        if (gameManager != null) gameManager.RemovePlayerData(idx);
        playerComponents.RemoveAt(idx);
        seatTokens.RemoveAt(idx);
        for (int i = 0; i < playerComponents.Count; i++)
            if (playerComponents[i] != null) playerComponents[i].playerId = i;
    }

    // ゲームが終わった後に残っている空席を片付ける。
    // ゲームの外では空席に意味がないため、ロビーでの操作の前に呼ぶ。
    [Server]
    public void RemoveEmptySeats()
    {
        for (int i = playerComponents.Count - 1; i >= 0; i--)
            if (playerComponents[i] == null) RemoveSeatAt(i);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // デバッグ専用: 実クライアント接続なしでBotプレイヤーを追加する(複数人プレイのテスト用)
    [Server]
    public Player AddBotPlayer(GameObject playerPrefab)
    {
        var obj = UnityEngine.Object.Instantiate(playerPrefab);

        var playerCom = obj.GetComponent<Player>();
        int id = playerComponents.Count;
        playerCom.playerName = "Bot" + (id + 1);
        playerCom.Setup(this, id);
        playerCom.gameManager = gameManager;
        // matchIdはSpawnより前に設定する(NetworkMatchはインタレスト管理のため、
        // Spawn時点のmatchIdで配信先が決まる)
        obj.GetComponent<NetworkMatch>().matchId = matchId;

        NetworkServer.Spawn(obj);

        // Botにも席の持ち主の印を持たせる(デバッグで「抜ける→戻る」を試せるように)
        string token = "bot:" + Guid.NewGuid().ToString("N");
        playerCom.clientToken = token;
        playerComponents.Add(playerCom);
        seatTokens.Add(token);
        gameManager.AddPlayer();
        gameManager.RefreshLobbyStatus();
        NineDebugLog.Add(roomId, "Bot追加: 席" + id);

        return playerCom;
    }

    // 接続を持たないPlayer(Bot)か
    public static bool IsBot(Player p) => p != null && p.connectionToClient == null;

    // 席の持ち主がBotか(空席でも判定できる)
    public bool DebugSeatIsBot(int idx) => idx >= 0 && idx < seatTokens.Count && seatTokens[idx].StartsWith("bot:");

    // 空席idxに戻れるBot(抜けたBot)を探す
    public Player DebugFindAwayBot(int idx, IEnumerable<Player> candidates)
    {
        if (idx < 0 || idx >= seatTokens.Count || playerComponents[idx] != null) return null;
        foreach (var c in candidates)
            if (c != null && c.clientToken == seatTokens[idx]) return c;
        return null;
    }

    // デバッグ専用: Botを部屋から抜けさせる。人間のLeave(RemovePlayer)と同じ扱いにする。
    // ゲーム中なら席は空席として残り、Botのオブジェクトは戻るときのために残す。
    // ゲーム外なら席ごと取り除き、Botも消す。
    [Server]
    public bool DebugBotLeave(Player bot)
    {
        if (!IsBot(bot)) return false;
        int idx = playerComponents.IndexOf(bot);
        if (idx < 0) return false;

        bool keepSeat = gameManager != null && gameManager.inProgress;
        if (keepSeat) playerComponents[idx] = null;
        else RemoveSeatAt(idx);

        bot.inRoom = false;
        bot.isReadyToStart = false;
        bot.isReadytoTurn = false;
        bot.isReadyForNextRound = false;
        bot.pendingSelection = -1;
        bot.room = null;
        bot.gameManager = null;
        // 本物の退出と同じく、部屋の人からは見えなくする
        bot.GetComponent<NetworkMatch>().matchId = Guid.Empty;
        NineDebugLog.Add(roomId, "Bot退出: 席" + idx + (keepSeat ? "(空席として残す)" : "(席を削除)"));

        if (!keepSeat) NetworkServer.Destroy(bot.gameObject);
        gameManager.RefreshLobbyStatus();
        return true;
    }

    // デバッグ専用: 抜けたBotを元の席に戻す(TryReclaimSeatのBot版)
    [Server]
    public bool DebugBotRejoin(Player bot)
    {
        if (!IsBot(bot) || gameManager == null || !gameManager.inProgress) return false;
        for (int i = 0; i < playerComponents.Count; i++)
        {
            if (playerComponents[i] != null || seatTokens[i] != bot.clientToken) continue;
            playerComponents[i] = bot;
            bot.GetComponent<NetworkMatch>().matchId = matchId;
            bot.Setup(this, i);
            bot.gameManager = gameManager;
            bot.isRoomHost = false;
            bot.isReadyToStart = false;
            bot.isReadytoTurn = false;
            bot.isReadyForNextRound = false;
            bot.pendingSelection = -1;
            gameManager.RestoreSeat(i);
            gameManager.RefreshLobbyStatus();
            NineDebugLog.Add(roomId, "Bot復帰: 席" + i);
            return true;
        }
        return false;
    }
#endif

    [Server]
    public void DeleteRoom()
    {
        RoomManager.Instance.roomDict.Remove(roomId);
        RoomManager.Instance.roomNames.Remove(roomId);
        gameManager.DeleteMatch();
    }

    // 戻り値: パスワードが違うときfalse(入室しなかった)
    [Server]
    public bool JoinRoom(NetworkConnectionToClient conn, string password, string token)
    {
        if (this.password != password) return false;

        var playerCom = conn.identity != null ? conn.identity.GetComponent<Player>() : null;
        if (playerCom == null) return true;
        if (players.Contains(conn)) return true; // 既にこの部屋にいる

        if (gameManager.inProgress)
        {
            // ゲーム中に入ってきた人は、自分の席に戻れるならプレイヤーとして、
            // そうでなければ観戦者として迎える。
            if (!TryReclaimSeat(conn, token)) AddSpectator(conn, token);
            return true;
        }

        RemoveEmptySeats();
        // 席が埋まっていたら、まず観戦者として迎える(本人が「参加する」に切り替えられる)
        if (playerComponents.Count >= gameManager.MaxPlayers) { AddSpectator(conn, token); return true; }

        AddPlayer(conn, token);
        return true;
    }

    [Server]
    public bool AllPlayersReady()
    {
        // 空席(前のゲームで抜けたまま戻らなかった人)は数えない
        int present = 0;
        foreach (var p in playerComponents)
        {
            if (p == null) continue;
            present++;
            if (!p.isReadyToStart) return false;
        }
        return present >= 2;
    }
}
