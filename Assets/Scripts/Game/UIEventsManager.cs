using System.Collections.Generic;
using UnityEngine;
using Mirror;
using TMPro;

public class UIEventsManager : NetworkBehaviour
{
    public TMP_Text inputField;
    
    public void ButtonCreateRoom()
    {
        string txt = inputField.text;
        var rm = RoomManager.Instance;
        rm.CmdCreateRoom(txt, "****");
    }

    public void ButtonJoinRoom()
    {
        string txt = inputField.text;
        var rm = RoomManager.Instance;
        rm.CmdJoinRoom(txt, "****", connectionToClient);
    }
}
