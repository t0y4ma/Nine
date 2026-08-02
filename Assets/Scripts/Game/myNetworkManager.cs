using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class myNetworkManager : NetworkManager
{
    // Overrides the base singleton so we don't
    // have to cast to this type everywhere.
    public static new myNetworkManager singleton => (myNetworkManager)NetworkManager.singleton;

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
