using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class myNetworkManager : NetworkManager
{
    // Overrides the base singleton so we don't
    // have to cast to this type everywhere.
    public static new myNetworkManager singleton => (myNetworkManager)NetworkManager.singleton;


    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        var playerCon = conn.identity.GetComponent<Player>();
        if (playerCon != null)
        {
            var room = playerCon.room;
            if(room != null) room.RemovePlayer(conn);
        }
        base.OnServerDisconnect(conn);
    }

}
