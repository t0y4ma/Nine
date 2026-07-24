using System.Collections.Generic;
using UnityEngine;
using Mirror;
using Unity.VisualScripting;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set;}

    private SyncList<bool> used_Players = new();
    
    [SyncVar] public int CARDCOUNT = 9;
    
    public override void OnStartServer()
    {
        if(Instance == null) Instance = this;
    }

    public override void OnStopServer()
    {
        if(Instance == this) Instance = null;
    }

    [Server]
    public bool UseCard(int id,int cardindex)
    {
        if(used_Players.Count/CARDCOUNT < id) return false;
        if(used_Players[id*CARDCOUNT+cardindex]) return false;
        used_Players[id*CARDCOUNT+cardindex] = true;
        return true;
    }
}
