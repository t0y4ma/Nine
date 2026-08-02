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

    [SyncVar(hook = nameof(OnIsReadytoTurnChanged))]
    public bool isReadytoTurn;

    [SyncVar(hook = nameof(OnInRoomChanged))]
    public bool inRoom;

    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReadyToStart;

    [SyncVar]
    public bool isReadyForNextRound;

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
        inRoom = true;
    }

[Command]
    public void CmdSetReady(bool ready)
    {
        if(room == null) return;
        if(gameManager != null && gameManager.inProgress) return;
        isReadyToStart = ready;
        if(gameManager != null) gameManager.RefreshLobbyStatus();
    }

    private void OnInRoomChanged(bool oldVal, bool newVal)
    {
        if(!isOwned) return;
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.RefreshLobbyPanels();
    }

    private void OnReadyChanged(bool oldVal, bool newVal)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.RefreshLobbyPanels();
    }

private void OnIsReadytoTurnChanged(bool oldVal, bool newVal)
    {
        var uiManager = GameObject.Find("Manager")?.GetComponent<UIEventsManager>();
        uiManager?.RefreshRoundResultPanel();
    }



[Command]
    public void CmdUseCard(int cardindex)
    {
        if(cardindex < 0 || cardindex >= cards.Count) return;
        if(used[cardindex]) return;
        if(isReadytoTurn) return;

        int id = GetPlayerId();
        if (room.gameManager.UseCard(id, cardindex))
        {
            used[cardindex] = true;
        }
    }

[Command]
    public void CmdReadyForNextRound()
    {
        if(gameManager == null) return;
        isReadyForNextRound = true;
    }


#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // デバッグ専用: Command(オーナー権限)を経由せず、サーバー上で直接どのプレイヤーとしても操作できる
    [Server]
    public bool DebugUseCard(int cardindex)
    {
        if(cardindex < 0 || cardindex >= cards.Count) return false;
        if(used[cardindex]) return false;
        if(isReadytoTurn) return false;

        int id = GetPlayerId();
        if(room.gameManager.UseCard(id, cardindex))
        {
            used[cardindex] = true;
            return true;
        }
        return false;
    }


#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // デバッグ専用: オーナー権限のないBotプレイヤーのReady状態を直接設定する
    [Server]
    public void DebugSetReady(bool ready)
    {
        if(room == null) return;
        if(gameManager != null && gameManager.inProgress) return;
        isReadyToStart = ready;
        if(gameManager != null) gameManager.RefreshLobbyStatus();
    }

// デバッグ専用: オーナー権限のないBotプレイヤーの「次へ」準備状態を直接設定する
    [Server]
    public void DebugReadyForNextRound()
    {
        isReadyForNextRound = true;
    }

#endif

#endif



}
