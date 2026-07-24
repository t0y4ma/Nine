using Mirror;
using UnityEngine;

public class PasswordAuthenticator : NetworkAuthenticator
{

    // クライアント側
    public override void OnStartClient()
    {
        NetworkClient.RegisterHandler<JoinRoomRequest>(OnJoinRoom);
    }

    public void OnJoinRoom(JoinRoomRequest request)
    {
        RoomManager.Instance.CmdJoinRoom(request.roomId, request.password);
    }
}