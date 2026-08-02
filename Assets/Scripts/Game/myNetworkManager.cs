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
    // 接続失敗の判定・表示は常時アクティブなこのクラス側で完結させる。
    private static bool _hasEverConnected = false;

    private void Start()
    {
        // VM/専用サーバー向けビルド(Dedicated Serverビルド、または -server 起動引数)の場合、
        // 起動した瞬間に自動でサーバーとして立ち上がる(UIクリック不要)
        if (ShouldAutoStartServer())
        {
            StartServer();
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
            // 常に存在するStatusTextを直接操作して失敗を知らせる
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
