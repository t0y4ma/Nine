using Mirror;

public struct JoinRoomRequest : NetworkMessage
{
    public string roomId;
    public string password;
}

public struct AuthResponse : NetworkMessage
{
    public bool success;
}