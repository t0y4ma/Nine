using System.Collections.Generic;
using UnityEngine;
using Mirror;
using Unity.VisualScripting;

public class GameManager : NetworkBehaviour
{

    private SyncList<bool> used_Players = new();
    
    [SyncVar] public int CARDCOUNT = 9;

    [Server]
    public bool UseCard(int id,int cardindex)
    {
        if(used_Players.Count/CARDCOUNT < id) return false;
        if(used_Players[id*CARDCOUNT+cardindex]) return false;
        used_Players[id*CARDCOUNT+cardindex] = true;
        return true;
    }
}
