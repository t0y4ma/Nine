using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using Mirror;
using TMPro;

public class UIEventsManager : NetworkBehaviour
{
    [SerializeField] private RoomManager roomManager;
    public TMP_InputField inputField;
    public TMP_Text statusText;

    public GameObject othersCardParent;
    public GameObject myCardParent;
    public GameObject cardUI;

    [Header("Lobby / Connection")]
    public GameObject connectPanel;
    public TMP_InputField addressInput;
    public GameObject hostButtonGO;
    public GameObject roomCreateButtonGO;
    public GameObject roomJoinButtonGO;
    public GameObject lobbyPanel;
    public TMP_Text readyStatusText;
    public UnityEngine.UI.Button startGameButton;
    public TMP_Text readyButtonLabel;
    public GameObject serverButtonGO;
    public GameObject roundResultPanel;
    public TMP_Text roundResultText;
    public GameObject transitionBarPanel;
    public UnityEngine.UI.Image transitionBarFill;
    public GameObject nextRoundButtonGO;
    public GameObject roundTimerBarPanel;
    public UnityEngine.UI.Image roundTimerBarFill;
    public GameObject confirmButtonGO;
    public GameObject cutInPanel;
    public TMP_Text cutInText;
    public GameObject othersLabelsParent;
    public GameObject roomListPanelGO;
    public Transform roomListContent;
    public Sprite roundedButtonSprite;

    private int _selectedCardIndex = -1;
    private Coroutine _cutInCoroutine;
    private bool _lastConnected = false;

private void Start()
    {
        RefreshLobbyPanels();
    }

    // 接続に失敗した(一度も繋がらないまま切断された)場合、その旨を表示する


    private void Update()
    {
        bool connected = NetworkClient.isConnected;
        if (connected != _lastConnected)
        {
            _lastConnected = connected;
            RefreshLobbyPanels();
        }
    }

    // WebGLはリスニングソケットを開けないためホスト/サーバーになれない
    private static bool IsHostingSupported()
    {
        return Application.platform != RuntimePlatform.WebGLPlayer;
    }

    // WebGLでHTTPS配信されている場合はwss(暗号化WebSocket)を使う必要がある。
    // ブラウザはHTTPSページから非暗号化のws://接続を許可しないため。


    public void ButtonHost()
    {
        if (!IsHostingSupported()) return;
        NetworkManager.singleton.StartHost();
    }

    public void ButtonConnect()
    {
        string addr = (addressInput != null && !string.IsNullOrEmpty(addressInput.text)) ? addressInput.text : "localhost";
        NetworkManager.singleton.networkAddress = addr;
        NetworkManager.singleton.StartClient();
    }

    // 専用フラグ(UNITY_SERVERビルド、または起動時の -server コマンドライン引数)が立っている場合のみ
    // Serverモード(自分ではプレイせず、サーバーとしてのみ起動)を許可する
    private static bool IsServerModeAllowed()
    {
        if (!IsHostingSupported()) return false;
#if UNITY_SERVER
        return true;
#else
        foreach (var arg in System.Environment.GetCommandLineArgs())
        {
            if (arg == "-server") return true;
        }
        return false;
#endif
    }

    public void ButtonServer()
    {
        if (!IsServerModeAllowed()) return;
        NetworkManager.singleton.StartServer();
    }

    public void ButtonReady()
    {
        var targetPlayer = GetDebugOrLocalPlayer();
        if (targetPlayer == null) return;
        bool newReady = !targetPlayer.isReadyToStart;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugTargetPlayer != null)
        {
            debugTargetPlayer.DebugSetReady(newReady);
            UpdateReadyButtonLabel(targetPlayer);
            return;
        }
#endif
        targetPlayer.CmdSetReady(newReady);
    }

    public void ButtonNextRound()
    {
        var targetPlayer = GetDebugOrLocalPlayer();
        if (targetPlayer == null) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugTargetPlayer != null)
        {
            debugTargetPlayer.DebugReadyForNextRound();
            return;
        }
#endif
        targetPlayer.CmdReadyForNextRound();
    }

    // ラウンド遷移までの残り時間バーを更新する。remainingFractionが0より大きい間は表示する。
    public void UpdateTransitionBar(float remainingFraction)
    {
        bool show = remainingFraction > 0f;
        if (transitionBarPanel != null) transitionBarPanel.SetActive(show);
        if (nextRoundButtonGO != null) nextRoundButtonGO.SetActive(show);
        if (transitionBarFill != null) transitionBarFill.fillAmount = Mathf.Clamp01(remainingFraction);
    }

    // ラウンド制限時間の残り時間バーを更新する
    public void UpdateRoundTimerBar(float remainingFraction)
    {
        if (roundTimerBarPanel != null) roundTimerBarPanel.SetActive(true);
        if (roundTimerBarFill != null) roundTimerBarFill.fillAmount = Mathf.Clamp01(remainingFraction);
    }

    // ラウンド開始時のカットイン演出を表示する。選択状態もここでリセットする。
    public void ShowRoundCutIn(int roundNumber, int totalRounds)
    {
        _selectedCardIndex = -1;
        if (confirmButtonGO != null) confirmButtonGO.SetActive(false);

        if (cutInPanel == null || cutInText == null) return;
        cutInText.text = "ROUND " + roundNumber + " / " + totalRounds;
        if (_cutInCoroutine != null) StopCoroutine(_cutInCoroutine);
        _cutInCoroutine = StartCoroutine(CutInRoutine());
    }

    private System.Collections.IEnumerator CutInRoutine()
    {
        cutInPanel.SetActive(true);
        yield return new WaitForSeconds(1.5f);
        cutInPanel.SetActive(false);
    }

    public void RefreshLobbyPanels()
    {
        bool connected = NetworkClient.isConnected;
        var localPlayer = NetworkClient.connection?.identity?.GetComponent<Player>();
        bool inRoom = localPlayer != null && localPlayer.inRoom;
        bool inGame = inRoom && localPlayer.gameManager != null && localPlayer.gameManager.inProgress;
        bool isHost = localPlayer != null && localPlayer.isRoomHost;

        // WebGLは自動接続されるためHost/Connect/Server類のパネル自体が不要
        bool isWebGL = Application.platform == RuntimePlatform.WebGLPlayer;
        if (connectPanel != null) connectPanel.SetActive(!connected && !isWebGL);
        if (serverButtonGO != null) serverButtonGO.SetActive(IsServerModeAllowed());
        if (hostButtonGO != null) hostButtonGO.SetActive(IsHostingSupported());

        // 部屋作成は未接続でも押せる(押した際に自動でホストになる)。参加は接続済みが前提。
        bool showCreate = !inRoom;
        bool showJoin = connected && !inRoom;
        if (roomCreateButtonGO != null) roomCreateButtonGO.SetActive(showCreate);
        if (roomListPanelGO != null) roomListPanelGO.SetActive(showCreate);
        if (roomJoinButtonGO != null) roomJoinButtonGO.SetActive(showJoin);
        if (inputField != null) inputField.gameObject.SetActive(!inRoom);

        if (lobbyPanel != null) lobbyPanel.SetActive(inRoom && !inGame);
        if (myCardParent != null) myCardParent.SetActive(inGame);
        if (othersCardParent != null) othersCardParent.SetActive(inGame);
        if (roundResultPanel != null) roundResultPanel.SetActive(inGame);

        if (!inGame)
        {
            if (transitionBarPanel != null) transitionBarPanel.SetActive(false);
            if (nextRoundButtonGO != null) nextRoundButtonGO.SetActive(false);
            if (roundTimerBarPanel != null) roundTimerBarPanel.SetActive(false);
            if (confirmButtonGO != null) confirmButtonGO.SetActive(false);
            if (cutInPanel != null) cutInPanel.SetActive(false);
        }

        if (startGameButton != null)
        {
            bool showStart = inRoom && !inGame && isHost;
            startGameButton.gameObject.SetActive(showStart);
        }

        // ロビー内であれば、SyncVarの現在値を直接読んで即座に反映する(初回同期ではフックが発火しないため)
        if (inRoom && localPlayer.gameManager != null)
        {
            UpdateLobbyStatus(localPlayer.gameManager.readyCount, localPlayer.gameManager.totalPlayerCount);
        }
        else
        {
            UpdateLobbyStatus(0, 0);
        }

        UpdateReadyButtonLabel(GetDebugOrLocalPlayer());

        if (inGame) RefreshRoundResultPanel();
    }

    private void UpdateReadyButtonLabel(Player localPlayer)
    {
        if (readyButtonLabel == null) return;
        readyButtonLabel.text = (localPlayer != null && localPlayer.isReadyToStart) ? "Ready (cancel)" : "Ready";
    }

    public void UpdateLobbyStatus(int readyCount, int totalCount)
    {
        if (readyStatusText != null) readyStatusText.text = "Ready: " + readyCount + " / " + totalCount;
        if (startGameButton != null) startGameButton.interactable = (totalCount >= 2 && readyCount == totalCount);
    }

    public void ButtonCreateRoom()
    {
        string txt = inputField.text;
        if (!NetworkClient.isConnected && !NetworkServer.active)
        {
            if (!IsHostingSupported())
            {
                // WebGLはホストになれないため、先に実サーバーへConnectしてもらう必要がある
                ShowResult("Connect to a server first (WebGL can't host)");
                return;
            }
            // サーバーが見つからない場合は自分が仮のホスト(サーバー)になる。
            // Managerはホスト開始前は非アクティブなため、常時アクティブなNetworkManager側でコルーチンを走らせる。
            NetworkManager.singleton.StartCoroutine(AutoHostThenCreateRoom(txt));
            return;
        }
        roomManager.CmdCreateRoom(txt, "****");
    }

    private System.Collections.IEnumerator AutoHostThenCreateRoom(string txt)
    {
        NetworkManager.singleton.StartHost();
        yield return new WaitUntil(() => NetworkClient.ready);
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

    // 現在稼働中の部屋一覧をスクロールリストに反映する。クリックすると部屋IDが入力欄に自動入力される。
    public void RefreshRoomList()
    {
        if (roomListContent == null || roomManager == null) return;

        for (int i = roomListContent.childCount - 1; i >= 0; i--)
        {
            Destroy(roomListContent.GetChild(i).gameObject);
        }

        var roundedSmall = roundedButtonSprite;

        foreach (var kv in roomManager.roomNames)
        {
            string roomId = kv.Key;

            var btnGo = new GameObject("Room_" + roomId);
            btnGo.transform.SetParent(roomListContent, false);
            btnGo.AddComponent<RectTransform>();
            var layoutElem = btnGo.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElem.preferredHeight = 40;
            layoutElem.flexibleWidth = 1;

            var img = btnGo.AddComponent<UnityEngine.UI.Image>();
            if (roundedSmall != null) { img.sprite = roundedSmall; img.type = UnityEngine.UI.Image.Type.Sliced; }
            img.color = new Color(0.29f, 0.56f, 0.89f, 1f);

            var btn = btnGo.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(btnGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = roomId;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 12;
            tmp.fontSizeMax = 20;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            string capturedId = roomId;
            btn.onClick.AddListener(() => { if (inputField != null) inputField.text = capturedId; });
        }
    }

    [ContextMenu("Refresh My Card View")]
    public void RefreshMyCardMenu()
    {
        List<bool> a = new(5);
        for (int i = 0; i < 5; i++) a.Add(i % 2 == 0);
        RefreshMyCardView(a, 5);
    }

    [Client]
    public void RefreshMyCardView(List<bool> used, int cnt)
    {
        if (cnt > myCardParent.transform.childCount)
        {
            for (int i = myCardParent.transform.childCount; i < cnt; i++)
            {
                var card = Instantiate(cardUI, new Vector3(), Quaternion.identity, myCardParent.transform);
                var rT = card.GetComponent<RectTransform>();
                rT.anchoredPosition = new Vector3(i * 120 - used.Count * 60 + 30, -300, 0);
                var numUI = card.GetComponent<NumberCardUI>();
                numUI.Setup(i + 1);
                int capturedIndex = i;
                UnityAction func = () => { SelectCard(capturedIndex); };
                numUI.SetListener(func);
            }
        }
        for (int i = 0; i < cnt; i++)
        {
            var numUI = myCardParent.transform.GetChild(i).GetComponent<NumberCardUI>();
            numUI.SetUsed(used[i]);
            numUI.SetSelected(i == _selectedCardIndex);
        }
    }

    // カードをクリックした際、まだ確定せず選択状態にするだけ(別のカードを選び直せる)
    public void SelectCard(int cardindex)
    {
        var targetPlayer = GetDebugOrLocalPlayer();
        if (targetPlayer == null || targetPlayer.isReadytoTurn) return; // 既に確定済みなら選択不可
        if (cardindex >= 0 && cardindex < targetPlayer.used.Count && targetPlayer.used[cardindex]) return; // 使用済みカードは選べない

        _selectedCardIndex = (cardindex == _selectedCardIndex) ? -1 : cardindex; // 同じカードをもう一度押すと選択解除
        RefreshMyCardView(targetPlayer.used.ToList(), targetPlayer.used.Count);
        if (confirmButtonGO != null) confirmButtonGO.SetActive(_selectedCardIndex >= 0);
    }

    // 選択中のカードを確定し、サーバーに送信する
    public void ButtonConfirmCard()
    {
        var targetPlayer = GetDebugOrLocalPlayer();
        if (targetPlayer == null || targetPlayer.isReadytoTurn) return;
        if (_selectedCardIndex < 0) return;

        int cardindex = _selectedCardIndex;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugTargetPlayer != null)
        {
            debugTargetPlayer.DebugUseCard(cardindex);
            _selectedCardIndex = -1;
            if (confirmButtonGO != null) confirmButtonGO.SetActive(false);
            return;
        }
#endif
        var player = NetworkClient.connection?.identity?.GetComponent<Player>();
        if (player != null) player.CmdUseCard(cardindex);
        _selectedCardIndex = -1;
        if (confirmButtonGO != null) confirmButtonGO.SetActive(false);
    }

    [ContextMenu("Refresh All Card View")]
    public void RefreshAllCardMenu()
    {
        List<bool> a = new(10);
        for (int i = 0; i < 10; i++) a.Add(i % 2 == 0);
        RefreshAllCardView(a, 5);
    }

    [Client]
    public void RefreshAllCardView(List<bool> used_all, int cnt)
    {
        int pcnt = used_all.Count / cnt;

        // 各行の上にプレイヤー名+ポイント数のラベルを表示する
        if (othersLabelsParent != null)
        {
            var localPlayer = GetDebugOrLocalPlayer();
            var gm = localPlayer != null ? localPlayer.gameManager : null;

            while (othersLabelsParent.transform.childCount < pcnt)
            {
                int rowIndex = othersLabelsParent.transform.childCount;
                var labelGo = new GameObject("PlayerLabel" + rowIndex);
                labelGo.transform.SetParent(othersLabelsParent.transform, false);
                var labelRt = labelGo.AddComponent<RectTransform>();
                labelRt.pivot = new Vector2(0, 0.5f);
                labelRt.sizeDelta = new Vector2(300, 24);
                labelRt.anchoredPosition = new Vector2(0, rowIndex * -80 + 45);
                var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
                labelTmp.enableAutoSizing = true;
                labelTmp.fontSizeMin = 10;
                labelTmp.fontSizeMax = 18;
                labelTmp.enableWordWrapping = false;
                labelTmp.overflowMode = TextOverflowModes.Truncate;
                labelTmp.alignment = TextAlignmentOptions.Left;
                labelTmp.color = Color.white;
            }
            for (int i = 0; i < pcnt; i++)
            {
                var labelTmp = othersLabelsParent.transform.GetChild(i).GetComponent<TextMeshProUGUI>();
                int points = (gm != null && i < gm.roundWins.Count) ? gm.roundWins[i] : 0;
                labelTmp.text = "Player " + i + "  -  " + points + " pt";
            }
        }

        for (int i = 0; i < pcnt; i++)
        {
            for (int j = 0; j < cnt; j++)
            {
                if (othersCardParent.transform.childCount <= i * cnt + j)
                {
                    var card = Instantiate(cardUI, new Vector3(), Quaternion.identity, othersCardParent.transform);
                    var rT = card.GetComponent<RectTransform>();
                    rT.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                    rT.anchoredPosition = new Vector3(j * 55, i * -80, 0);

                    var numUI = card.GetComponent<NumberCardUI>();
                    numUI.Setup(j + 1);
                }
            }
        }
        for (int i = 0; i < pcnt * cnt; i++)
        {
            int playerIndex = i / cnt;
            int cardIndex = i % cnt;
            othersCardParent.transform.GetChild(playerIndex * cnt + cardIndex).GetComponent<NumberCardUI>().SetUsed(used_all[i]);
        }
    }

    public void ShowResult(string message)
    {
        Debug.Log(message);
        if (statusText != null) statusText.text = message;
    }

    public Player GetDebugOrLocalPlayer()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugTargetPlayer != null) return debugTargetPlayer;
#endif
        return NetworkClient.connection?.identity?.GetComponent<Player>();
    }

    // ラウンドの選択状況・結果一覧を更新する。
    // 各プレイヤーについて、選択済みだが未公開なら"?"、未選択なら"-"、公開済みなら実際の数字を表示する。
    public void RefreshRoundResultPanel()
    {
        if (roundResultPanel == null || cardUI == null) return;

        var localPlayer = GetDebugOrLocalPlayer();
        if (localPlayer == null || localPlayer.gameManager == null) return;

        var gm = localPlayer.gameManager;
        var players = FindObjectsOfType<Player>()
            .Where(p => p.gameManager == gm)
            .OrderBy(p => p.playerId)
            .ToList();

        while (roundResultPanel.transform.childCount < players.Count)
        {
            int slotIndex = roundResultPanel.transform.childCount;

            var slotGo = new GameObject("Slot" + slotIndex);
            slotGo.transform.SetParent(roundResultPanel.transform, false);
            var slotRt = slotGo.AddComponent<RectTransform>();
            slotRt.sizeDelta = new Vector2(90, 140);
            slotRt.anchoredPosition = new Vector2(-140 + slotIndex * 110, 0);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(slotGo.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.sizeDelta = new Vector2(90, 20);
            labelRt.anchoredPosition = new Vector2(0, 60);
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.enableAutoSizing = true;
            labelTmp.fontSizeMin = 9;
            labelTmp.fontSizeMax = 16;
            labelTmp.enableWordWrapping = false;
            labelTmp.overflowMode = TextOverflowModes.Truncate;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.color = Color.white;

            var cardGo = Instantiate(cardUI, slotGo.transform);
            var cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchoredPosition = new Vector2(0, -15);
            cardRt.localScale = new Vector3(0.7f, 0.7f, 0.7f);
        }

        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            var slot = roundResultPanel.transform.GetChild(i);
            slot.gameObject.SetActive(true);

            var label = slot.Find("Label").GetComponent<TextMeshProUGUI>();
            label.text = "Player " + p.playerId;

            var cardGo = slot.GetChild(1).gameObject;
            var numUI = cardGo.GetComponent<NumberCardUI>();

            if (p.isReadytoTurn)
            {
                cardGo.SetActive(true);
                numUI.SetupDisplay("?");
            }
            else
            {
                int idx = p.playerId;
                int revealed = (idx >= 0 && idx < gm.lastRevealedPicks.Count) ? gm.lastRevealedPicks[idx] : 0;
                if (revealed > 0)
                {
                    cardGo.SetActive(true);
                    numUI.SetupDisplay(revealed.ToString());
                }
                else
                {
                    cardGo.SetActive(false);
                }
            }
        }

        for (int i = players.Count; i < roundResultPanel.transform.childCount; i++)
        {
            roundResultPanel.transform.GetChild(i).gameObject.SetActive(false);
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Debug (Editor / Development Build only)")]
    public GameObject debugPanel;
    public TMP_Text debugPlayerLabel;
    public GameObject debugPlayerPrefab;
    private Player debugTargetPlayer;

    private void Awake()
    {
        if (debugPanel != null) debugPanel.SetActive(Debug.isDebugBuild);
    }

    public void OnClickDebugAddBot()
    {
        var localPlayer = NetworkClient.connection?.identity?.GetComponent<Player>();
        if (localPlayer == null || localPlayer.room == null || debugPlayerPrefab == null) return;

        localPlayer.room.AddBotPlayer(debugPlayerPrefab);
        RefreshDebugLabel();
    }

    public void OnClickDebugNextPlayer()
    {
        CycleDebugPlayer(1);
    }

    public void OnClickDebugPrevPlayer()
    {
        CycleDebugPlayer(-1);
    }

    private void CycleDebugPlayer(int dir)
    {
        var localPlayer = NetworkClient.connection?.identity?.GetComponent<Player>();
        if (localPlayer == null || localPlayer.room == null) return;

        var list = localPlayer.room.playerComponents;
        if (list.Count == 0) return;

        int currentIndex = debugTargetPlayer != null ? list.IndexOf(debugTargetPlayer) : -1;
        int nextIndex = ((currentIndex + dir) % list.Count + list.Count) % list.Count;
        debugTargetPlayer = list[nextIndex];

        RefreshDebugLabel();
        RefreshMyCardView(debugTargetPlayer.used.ToList(), debugTargetPlayer.used.Count);
        RefreshLobbyReadyLabelOnly();
    }

    private void RefreshLobbyReadyLabelOnly()
    {
        UpdateReadyButtonLabel(GetDebugOrLocalPlayer());
    }

    private void RefreshDebugLabel()
    {
        if (debugPlayerLabel == null) return;
        debugPlayerLabel.text = debugTargetPlayer != null
            ? ("Debug: Player " + debugTargetPlayer.GetPlayerId())
            : "Debug: (local)";
    }
#endif
}
