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
    public NetworkConnectionToClient hostConnection;

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
        conn.identity.GetComponent<NetworkMatch>().matchId = matchId;
        if (!players.Contains(conn)) players.Add(conn);
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
            // 部屋を作った本人なら、接続が変わっていても(再読み込み等)ホスト権限を戻す
            if (token == hostToken) hostConnection = conn;

            playerComponents[i] = playerCom;
            SeatPlayer(conn, playerCom, i);
            gameManager.RestoreSeat(i);
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

        players.Remove(player);

        if (idx >= 0)
        {
            if (gameManager != null && gameManager.inProgress)
            {
                // ゲーム中は席を残して空席にする。
                // 空席の間は毎ラウンド未使用のカードからランダムに出す(時間切れと同じ扱い)。
                // このラウンドで確定済みのカードがあれば、それはそのまま出す。
                // 同じ部屋にJoinし直せば、この席に戻って続きから遊べる。
                playerComponents[idx] = null;
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
            playerCom.room = null;
            playerCom.gameManager = null;
            playerCom.myRoomId = "";
            if (player.identity != null)
                player.identity.GetComponent<NetworkMatch>().matchId = Guid.Empty;
            if (notifyClient) playerCom.TargetLeftRoom(player);
        }

        // 接続している人が誰もいなくなったら部屋ごと消す
        if (players.Count == 0) { DeleteRoom(); return; }
        gameManager.RefreshLobbyStatus();
    }

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
        playerCom.Setup(this, id);
        playerCom.gameManager = gameManager;
        // matchIdはSpawnより前に設定する(NetworkMatchはインタレスト管理のため、
        // Spawn時点のmatchIdで配信先が決まる)
        obj.GetComponent<NetworkMatch>().matchId = matchId;

        NetworkServer.Spawn(obj);

        playerComponents.Add(playerCom);
        seatTokens.Add("");   // Botは戻ってこないので識別子は持たない
        gameManager.AddPlayer();
        gameManager.RefreshLobbyStatus();

        return playerCom;
    }
#endif

    [Server]
    public void DeleteRoom()
    {
        RoomManager.Instance.roomDict.Remove(roomId);
        RoomManager.Instance.roomNames.Remove(roomId);
        gameManager.DeleteMatch();
    }

    [Server]
    public void JoinRoom(NetworkConnectionToClient conn, string password, string token)
    {
        if (this.password != password) return;

        var playerCom = conn.identity != null ? conn.identity.GetComponent<Player>() : null;
        if (playerCom == null) return;
        if (playerComponents.Contains(playerCom)) return; // 既にこの部屋にいる

        if (gameManager.inProgress)
        {
            // ゲーム中は新しい人は入れない。
            // 途中で抜けた本人が、自分の席に戻る場合だけ受け付ける。
            TryReclaimSeat(conn, token);
            return;
        }

        RemoveEmptySeats();
        if (playerComponents.Count >= gameManager.MaxPlayers) return; // 参加人数上限に達している

        AddPlayer(conn, token);
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
