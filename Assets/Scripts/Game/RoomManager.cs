using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

public class RoomManager : NetworkBehaviour
{
    #region singleton
    public static RoomManager Instance { get; private set;}
    
    [ServerCallback]
    public override void OnStartServer()
    {
        if(Instance == null) Instance = this;
    }

    [ServerCallback]
    public override void OnStopServer()
    {
        if(Instance == this) Instance = null;
    }
    #endregion

    public Dictionary<string,RoomInfo> roomDict;
    public SyncDictionary<string,string> roomNames;

    [Command]
    public void CmdJoinRoom(string roomId, string password = "****")
    {
        if (!roomDict.ContainsKey(roomId)) return;
        roomDict[roomId].room.JoinRoom(connectionToClient,password);
    }
}
