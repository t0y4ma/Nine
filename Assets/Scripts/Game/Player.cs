using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Analytics;
public class Player : NetworkBehaviour
{
    public Room room;

    [SyncVar]
    public string playerName;
    [SyncVar]
    public int playerId;
    [SyncVar]
    public GameManager gameManager;
    public readonly SyncList<int> cards = new();
    public readonly SyncList<bool> used = new();

    [SyncVar(hook = "OnReadyToTurnChanged")]
    public bool isReadytoTurn;

    public int GetPlayerId()
    {
        return playerId;
    }

    public void Setup(Room room,int playerId)
    {
        this.room = room;
        this.playerId = playerId;
        int cardCnt = room.gameManager.CARDCOUNT;
        for(int i = 1;i <= cardCnt;i++){ cards.Add(i); used.Add(false); }
    }

    [Command]
    public void CmdUseCard(int cardindex)
    {
        if(cardindex < 0 || cardindex >= cards.Count) return;
        if(used[cardindex]) return;
        
        int id = GetPlayerId();
        if (room.gameManager.UseCard(id, cardindex))
        {
            used[cardindex] = true; 
            isReadytoTurn = true;
        }
    }

    [ClientCallback]
    public void OnReadyToTurnChanged(bool oldVar, bool newVar)
    {
        GameObject.Find("Manager").GetComponent<UIEventsManager>().RefreshMyCardView(used.ToList(),used.Count);
        GameObject.Find("Manager").GetComponent<UIEventsManager>().RefreshAllCardView(gameManager.used_Players.ToList(), gameManager.used_Players.Count);
    }
}
