using System.Collections.Generic;
using UnityEngine;
using Mirror;
using TMPro;
using Unity.VisualScripting;
using System;
using Mirror.BouncyCastle.Asn1.X509.Qualified;
using UnityEngine.Events;

public class UIEventsManager : NetworkBehaviour
{
    [SerializeField] private RoomManager roomManager;
    public TMP_Text inputField;

    public GameObject othersCardParent;
    
    public GameObject myCardParent;

    public GameObject cardUI;
    
    public void ButtonCreateRoom()
    {
        string txt = inputField.text;
        roomManager.CmdCreateRoom(txt, "****");
    }

    public void ButtonJoinRoom()
    {
        string txt = inputField.text;
        roomManager.CmdJoinRoom(txt, "****", connectionToClient);
    }

    public void ButtonStartGame()
    {
        string txt = inputField.text;
        roomManager.CmdStartGame(txt, connectionToClient);
    }

    [ContextMenu("Refresh My Card View")]
    public void RefreshMyCardMenu()
    {
        List<bool> a = new(5);
        for(int i = 0;i < 5;i++) a.Add(i%2 == 0);
        RefreshMyCardView(a,5);
    }

    [Client]
    public void RefreshMyCardView(List<bool> used,int cnt)
    {
        if(cnt > myCardParent.transform.childCount){
            for(int i = myCardParent.transform.childCount;i < cnt; i++)
            {
                var card = Instantiate(cardUI,new Vector3(),Quaternion.identity,myCardParent.transform);
                var rT = card.GetComponent<RectTransform>();
                rT.anchoredPosition = new Vector3(i*-120+used.Count*60-30,-300,0);
                var numUI = card.GetComponent<NumberCardUI>();
                numUI.Setup(i+1);
                UnityAction func = () => {
                    var player = connectionToClient.identity.GetComponent<Player>();
                    player.CmdUseCard(i);
                };
                numUI.SetListener(func);
            }
        }
        for(int i = 0;i < cnt; i++)
        {
            myCardParent.transform.GetChild(i).GetComponent<NumberCardUI>().SetUsed(used[i]);
        }
    }
    
    [ContextMenu("Refresh All Card View")]
    public void RefreshAllCardMenu()
    {
        List<bool> a = new(10);
        for(int i = 0;i < 10;i++) a.Add(i%2 == 0);
        RefreshAllCardView(a,5);
    }

    
    [Client]
    public void RefreshAllCardView(List<bool> used_all,int cnt)
    {
        int pcnt = used_all.Count/cnt;
        for(int i = 0;i < pcnt; i++)
        {
            for(int j = 0;j < cnt; j++)
            {
                if(othersCardParent.transform.childCount <= i * cnt + j)
                {
                    var card = Instantiate(cardUI,new Vector3(),Quaternion.identity,othersCardParent.transform);
                    var rT = card.GetComponent<RectTransform>();
                    rT.localScale = new Vector3(0.5f,0.5f,0.5f);
                    rT.anchoredPosition = new Vector3(j*50-915,350+i*-70,0);
                }
            }
        }
        for(int i = 0;i < pcnt*cnt; i++)
        {
            othersCardParent.transform.GetChild(i).GetComponent<NumberCardUI>().SetUsed(used_all[i]);
        }
    }
}
