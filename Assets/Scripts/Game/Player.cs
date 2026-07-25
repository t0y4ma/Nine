using System.Collections.Generic;
using System.Linq;
using Mirror;
public class Player : NetworkBehaviour
{
    public Room room;

    [SyncVar]
    public string playerName;
    [SyncVar]
    public int playerId;
    public SyncList<int> cards = new();
    public SyncList<bool> used = new();
    [SyncVar]
    public bool isReady;

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
        }
    }
}
