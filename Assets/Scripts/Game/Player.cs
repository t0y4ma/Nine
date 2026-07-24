using System.Collections.Generic;
using System.Linq;
using Mirror;
public class Player : NetworkBehaviour
{
    [SyncVar]
    public string playerName;
    [SyncVar]
    public int playerId;
    public SyncList<int> cards = new();
    public SyncList<bool> used = new();
    [SyncVar]
    public bool isReady;

    public override void OnStartServer()
    {
        int cardCnt = GameManager.Instance.CARDCOUNT;
        for(int i = 1;i <= cardCnt;i++){ cards.Add(i); used.Add(false); }
    }

    public int GetPlayerId()
    {
        return playerId;
    }

    [Command]
    public void CmdUseCard(int cardindex)
    {
        if(cardindex < 0 || cardindex >= cards.Count) return;
        if(used[cardindex]) return;
        
        int id = GetPlayerId();
        if (GameManager.Instance.UseCard(id, cardindex))
        {
            used[cardindex] = true;   
        }
    }
}
