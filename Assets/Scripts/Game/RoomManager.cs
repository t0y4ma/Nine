using System;
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

    [SerializeField] private GameObject GM;

    public Dictionary<string,RoomInfo> roomDict = new();
    public readonly SyncDictionary<string,string> roomNames = new();

    [Command(requiresAuthority = false)]
    public void CmdJoinRoom(string roomId, string password, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.ContainsKey(roomId)) return;
        if(password == "") password = "****";
        Debug.Log("Join to the room with id of " + roomId + ", password of " + password);
        roomDict[roomId].room.JoinRoom(sender,password);
    }

    [Command(requiresAuthority = false)]
    public void CmdCreateRoom(string roomId, string password)
    {
        if(roomDict.ContainsKey(roomId)) return;
        if(password == "") password = "****";
        Debug.Log("Create a room with id of " + roomId + ", password of " + password);
        var gm = Instantiate(GM);
        NetworkServer.Spawn(gm);
        Room room = new Room(gm.GetComponent<GameManager>(),password);
        room.matchId = Guid.NewGuid();
        room.roomId = roomId;
        room.gameManager.GetComponent<NetworkMatch>().matchId = room.matchId;
        RoomInfo roomInfo= new RoomInfo();
        roomInfo.name = roomId;
        roomInfo.password = password;
        roomInfo.room = room;
        roomDict[roomId] = roomInfo;
    }

    [Command(requiresAuthority = false)]
    public void CmdStartGame(string roomId, NetworkConnectionToClient sender = null)
    {
        if (!roomDict.TryGetValue(roomId, out var info)) return;
        Debug.Log("Start the game in the room with id of " + roomId);
        info.room.gameManager.StartGame();
    }

    [ClientCallback]
    public override void OnStartClient()
    {
        roomNames.OnChange += OnRoomsChanged;
    }

    [ClientCallback]
    public void OnRoomsChanged(SyncIDictionary<string, string>.Operation op, string key, string item)
    {
        
    }
}
