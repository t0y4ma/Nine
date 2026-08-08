using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class myNetworkManager : NetworkManager
{
    // Overrides the base singleton so we don't
    // have to cast to this type everywhere.
    public static new myNetworkManager singleton => (myNetworkManager)NetworkManager.singleton;

    // "Manager"(ロビーUI)は接続確立後にしかアクティブにならないため、
    // 接続前に必要な処理(自動接続・トランスポート設定・接続失敗判定)は
    // 常時アクティブなこのクラス側で完結させる。
    private static bool _hasEverConnected = false;

    // WebGL版が自動接続する本番サーバーのアドレス(Caddy経由でwssを待ち受ける)
    private const string ProductionServerAddress = "nine.freeddns.org";

    private void Start()
    {
        ConfigureWebGLTransport();

        // VM/専用サーバー向けビルド(Dedicated Serverビルド、または -server 起動引数)の場合、
        // 起動した瞬間に自動でサーバーとして立ち上がる(UIクリック不要)
        if (ShouldAutoStartServer())
        {
            StartServer();
            return;
        }

        // WebGLではHostが押せないため、どうせ押せないなら自動でConnectを試みる。
        // "Manager"(ロビーUI)は接続が確立するまで非アクティブでRefreshLobbyPanelsが動かないため、
        // ConnectPanel(Host/Connect/Server)はここで直接隠しておく。
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            var connectPanel = GameObject.Find("ConnectPanel");
            if (connectPanel != null) connectPanel.SetActive(false);

            networkAddress = ProductionServerAddress;
            StartClient();
        }
    }

    private static bool ShouldAutoStartServer()
    {
#if UNITY_SERVER
        return true;
#else
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg == "-server") return true;
        }
        return false;
#endif
    }

    // WebGLでHTTPS配信されている場合はwss(暗号化WebSocket)を使う必要がある。
    // ブラウザはHTTPSページから非暗号化のws://接続を許可しないため。
    private void ConfigureWebGLTransport()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer) return;
        var transport = Transport.active;
        if (transport == null) return;

        var wssField = transport.GetType().GetField("clientUseWss");
        if (wssField == null) return;

        bool useWss = Application.absoluteURL.StartsWith("https");
        wssField.SetValue(transport, useWss);
    }

    public override void OnClientConnect()
    {
        _hasEverConnected = true;
        base.OnClientConnect();
    }

    public override void OnClientDisconnect()
    {
        if (!_hasEverConnected)
        {
            // "Manager"(ロビーUI)は未接続時は非アクティブで見つからないため、
            // 常に存在するStatusTextを直接操作して失敗を知らせる。
            // 接続先を手入力させるのはセキュリティ・UX上望ましくないため、ConnectPanelは再表示しない。
            var statusGo = GameObject.Find("StatusText");
            var tmp = statusGo != null ? statusGo.GetComponent<TMPro.TextMeshProUGUI>() : null;
            if (tmp != null) tmp.text = "Could not find a server to connect to.";
        }
        base.OnClientDisconnect();
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        var playerCon = conn.identity.GetComponent<Player>();
        if (playerCon != null)
        {
            var room = playerCon.room;
            if (room != null) room.RemovePlayer(conn);
        }
        base.OnServerDisconnect(conn);
    }
}
