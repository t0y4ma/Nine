#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Mirror;

// Play Mode終了時に、SimpleWebTransportのOnDestroy/OnApplicationQuitに頼らず、
// 確実にサーバー/クライアントのソケットを閉じるための追加セーフティネット。
//
// 背景: SimpleWebTransport側にOnDestroy/OnApplicationQuitを実装したことで基本的には
// ポートが正しく解放されるようになったが、MonoBehaviourの破棄順序やドメインリロードの
// タイミング次第では、稀にそれだけでは不十分なケースが起こりうる。
// EditorApplication.playModeStateChangedはUnity Editor自体が管理するイベントで、
// MonoBehaviourの破棄順序に依存しない、より確実なタイミングで発火するため、
// ここで明示的にTransportを停止しておくことで二重の安全策とする。
[InitializeOnLoad]
public static class EditorPlayModePortCleanup
{
    static EditorPlayModePortCleanup()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // ExitingPlayMode: Play Modeを抜ける処理が始まった直後、まだオブジェクトが
        // 破棄される前のタイミング。ここで明示的にTransportを止めておく。
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            try
            {
                // Transport.ServerStop()/ClientDisconnect()だけでは、Transportのソケットは閉じても
                // NetworkServer.active/NetworkClient.active等のMirror内部の静的フラグ自体はリセット
                // されないことが判明した(これが原因で、次のPlay Modeセッション開始時にも
                // NetworkServer.active=trueが引き継がれ、「Server or Client already started」等の
                // 不整合が起きていた)。NetworkManager.StopHost()相当の、より上位のShutdown APIを
                // 呼ぶことで、フラグも含めて確実にリセットする。
                if (NetworkServer.active || NetworkClient.active)
                {
                    if (NetworkManager.singleton != null)
                    {
                        NetworkManager.singleton.StopHost();
                    }
                    else
                    {
                        if (NetworkServer.active) NetworkServer.Shutdown();
                        if (NetworkClient.active) NetworkClient.Shutdown();
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[EditorPlayModePortCleanup] 停止処理中に例外(無視して続行): " + e.Message);
            }
        }
    }
}
#endif
