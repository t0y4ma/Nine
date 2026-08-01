using System.Collections.Generic;
using UnityEngine;
using Mirror;
using Unity.VisualScripting;

public class GameManager : NetworkBehaviour
{
    public Room room;
    public readonly SyncList<bool> used_Players = new();
    private List<int> turncards = new();
    
    [SyncVar] public int CARDCOUNT = 9;

    [Server]
    public void DeleteMatch()
    {
        NetworkServer.Destroy(gameObject);
    }

    [Server]
    public void AddPlayer()
    {
        for(int i = 0;i < CARDCOUNT;i++) used_Players.Add(false);
    }

    [Server]
    public void StartGame()
    {
        int pcount = room.players.Count;
        for(int i = 0;i < pcount; i++)
        {
            turncards.Add(0);
        }
        EndTurn();
    }

    [Server]
    public bool UseCard(int id,int cardindex)
    {
        if(used_Players.Count/CARDCOUNT <= id) return false;
        if(used_Players[id*CARDCOUNT+cardindex]) return false;
        used_Players[id*CARDCOUNT+cardindex] = true;
        turncards[id] = cardindex+1;
        return true;
    }

    [Server]
    public void EndTurn()
    {
        var players = room.players;
        foreach(var player in players)
        {
            var playerCom = player.identity.GetComponent<Player>();
            playerCom.isReadytoTurn = true;
            playerCom.isReadytoTurn = false;
        }
    }
}
