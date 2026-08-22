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

    // タイトル画面のパスワード入力欄。実オブジェクトはGameObject.Findで動的に探す(既存のinputField等と
    // 同様の追加方法のため、Inspector参照ではなくコード内で解決する)。
    private TMP_InputField _passwordInputField;
    private TMP_InputField PasswordInputField
    {
        get
        {
            if (_passwordInputField == null)
            {
                var canvas = GameObject.Find("Canvas");
                var t = canvas != null ? canvas.transform.Find("RoomPassword") : null;
                if (t != null) _passwordInputField = t.GetComponent<TMP_InputField>();
            }
            return _passwordInputField;
        }
    }

    private string GetRoomPassword()
    {
        var pw = PasswordInputField;
        return (pw != null && !string.IsNullOrEmpty(pw.text)) ? pw.text : "";
    }

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
    // 各プレイヤー行の右端に表示する「今出したカード」。旧RoundResultPanelの役割を統合したもの。
    private GameObject playedCardsParent;
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

    // 現在のCanvasの実効幅(デザイン単位)を取得する。カード等の間隔を画面幅に応じて動的に詰めるために使う。
    private RectTransform _canvasRt;

    // "Manager"と"Canvas"は親子関係にないため、GetComponentInParentでは取得できない。GameObject.Findで探す。
    private float GetCanvasWidth()
    {
        if (_canvasRt == null)
        {
            var canvasGo = GameObject.Find("Canvas");
            if (canvasGo != null) _canvasRt = canvasGo.GetComponent<RectTransform>();
        }
return _canvasRt != null ? _canvasRt.rect.width : 1920f;
    }

    private float GetCanvasHeight()
    {
        if (_canvasRt == null)
        {
            var canvasGo = GameObject.Find("Canvas");
            if (canvasGo != null) _canvasRt = canvasGo.GetComponent<RectTransform>();
        }
        return _canvasRt != null ? _canvasRt.rect.height : 1080f;
    }

    private void Start()
    {
        RefreshLobbyPanels();
        SetupSettingsUI();
    }

    // --- 設定画面(カード枚数・得点方式) ---
    private int _pendingScoringMode = 0;

    private Transform FindSettingsPanel()
    {
        var canvas = GameObject.Find("Canvas");
        return canvas != null ? canvas.transform.Find("SettingsPanel") : null;
    }

    private void SetupSettingsUI()
    {
        var canvas = GameObject.Find("Canvas");
        if (canvas == null) return;

        var btnSettings = canvas.transform.Find("BtnSettings")?.GetComponent<UnityEngine.UI.Button>();
        if (btnSettings != null) { btnSettings.onClick.RemoveAllListeners(); btnSettings.onClick.AddListener(ButtonOpenSettings); }

        var panel = FindSettingsPanel();
        if (panel == null) return;

        var btnApply = panel.Find("BtnApplySettings")?.GetComponent<UnityEngine.UI.Button>();
        if (btnApply != null) { btnApply.onClick.RemoveAllListeners(); btnApply.onClick.AddListener(ButtonApplySettings); }

        var btnClose = panel.Find("BtnCloseSettings")?.GetComponent<UnityEngine.UI.Button>();
        if (btnClose != null) { btnClose.onClick.RemoveAllListeners(); btnClose.onClick.AddListener(ButtonCloseSettings); }

        var btnFixed = panel.Find("BtnScoringFixed")?.GetComponent<UnityEngine.UI.Button>();
        if (btnFixed != null) { btnFixed.onClick.RemoveAllListeners(); btnFixed.onClick.AddListener(() => SelectScoringMode(0)); }

        var btnSum = panel.Find("BtnScoringSum")?.GetComponent<UnityEngine.UI.Button>();
        if (btnSum != null) { btnSum.onClick.RemoveAllListeners(); btnSum.onClick.AddListener(() => SelectScoringMode(1)); }

        // GameOverPanel / HistoryPanel / LeaveRoomボタンの結線
        var btnCloseGameOver = canvas.transform.Find("GameOverPanel/BtnCloseGameOver")?.GetComponent<UnityEngine.UI.Button>();
        if (btnCloseGameOver != null) { btnCloseGameOver.onClick.RemoveAllListeners(); btnCloseGameOver.onClick.AddListener(ButtonCloseGameOver); }

        var btnHistory = canvas.transform.Find("BtnHistory")?.GetComponent<UnityEngine.UI.Button>();
        if (btnHistory != null) { btnHistory.onClick.RemoveAllListeners(); btnHistory.onClick.AddListener(ButtonToggleHistory); }

        var btnCloseHistory = canvas.transform.Find("HistoryPanel/BtnCloseHistory")?.GetComponent<UnityEngine.UI.Button>();
        if (btnCloseHistory != null) { btnCloseHistory.onClick.RemoveAllListeners(); btnCloseHistory.onClick.AddListener(ButtonToggleHistory); }

        var btnLeaveRoom = canvas.transform.Find("BtnLeaveRoom")?.GetComponent<UnityEngine.UI.Button>();
        if (btnLeaveRoom != null) { btnLeaveRoom.onClick.RemoveAllListeners(); btnLeaveRoom.onClick.AddListener(ButtonLeaveRoom); }
    }

    public void ButtonOpenSettings()
    {
        var panel = FindSettingsPanel();
        if (panel == null) return;

        var localPlayer = GetDebugOrLocalPlayer();
        var gm = localPlayer != null ? localPlayer.gameManager : null;

        var cardCountInput = panel.Find("CardCountInput")?.GetComponent<TMP_InputField>();
        if (cardCountInput != null) cardCountInput.text = (gm != null ? gm.CARDCOUNT : 9).ToString();

        var maxPlayersInputOpen = panel.Find("MaxPlayersInput")?.GetComponent<TMP_InputField>();
        if (maxPlayersInputOpen != null) maxPlayersInputOpen.text = (gm != null ? gm.MaxPlayers : 8).ToString();

        _pendingScoringMode = gm != null ? gm.ScoringMode : 0;
        RefreshScoringButtonHighlight(panel);

        panel.gameObject.SetActive(true);
    }

    public void ButtonCloseSettings()
    {
        var panel = FindSettingsPanel();
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void SelectScoringMode(int mode)
    {
        _pendingScoringMode = mode;
        var panel = FindSettingsPanel();
        if (panel != null) RefreshScoringButtonHighlight(panel);
    }

    private void RefreshScoringButtonHighlight(Transform panel)
    {
        var btnFixedImg = panel.Find("BtnScoringFixed")?.GetComponent<UnityEngine.UI.Image>();
        var btnSumImg = panel.Find("BtnScoringSum")?.GetComponent<UnityEngine.UI.Image>();
        Color selectedColor = new Color(0.29f, 0.72f, 0.56f, 1f);
        Color unselectedColor = new Color(0.35f, 0.38f, 0.45f, 1f);
        if (btnFixedImg != null) btnFixedImg.color = (_pendingScoringMode == 0) ? selectedColor : unselectedColor;
        if (btnSumImg != null) btnSumImg.color = (_pendingScoringMode == 1) ? selectedColor : unselectedColor;
    }

    public void ButtonApplySettings()
    {
        var panel = FindSettingsPanel();
        if (panel == null) return;

        var cardCountInput = panel.Find("CardCountInput")?.GetComponent<TMP_InputField>();
        int cardCount = 9;
        if (cardCountInput != null && !string.IsNullOrEmpty(cardCountInput.text))
        {
            int.TryParse(cardCountInput.text, out cardCount);
        }
        cardCount = Mathf.Clamp(cardCount, 3, 20);

        var maxPlayersInput = panel.Find("MaxPlayersInput")?.GetComponent<TMP_InputField>();
        int maxPlayers = 8;
        if (maxPlayersInput != null && !string.IsNullOrEmpty(maxPlayersInput.text))
        {
            int.TryParse(maxPlayersInput.text, out maxPlayers);
        }
        maxPlayers = Mathf.Clamp(maxPlayers, 2, 20);

        string roomId = inputField != null ? inputField.text : "";
        roomManager.CmdUpdateSettings(roomId, cardCount, _pendingScoringMode, maxPlayers, connectionToClient);

        panel.gameObject.SetActive(false);
    }

    // 接続に失敗した(一度も繋がらないまま切断された)場合、その旨を表示する


    // --- タイマーバーの滑らかな表示 ---
    // サーバーからのRpc(0.1秒間隔)を受信した瞬間だけ代入していたため、秒間10回のカクついた
    // 動きになり「ラグがある」ように見えていた。受信値は「目標値」として保持し、実際の表示は
    // 毎フレーム補間することで滑らかに動かす。
    private float _roundTimerTargetFill = 0f;
    private float _transitionBarTargetFill = 0f;
    private const float BAR_LERP_SPEED = 3f;

    private void Update()
    {
        bool connected = NetworkClient.isConnected;
        if (connected != _lastConnected)
        {
            _lastConnected = connected;
            RefreshLobbyPanels();
        }

        if (roundTimerBarFill != null)
        {
            roundTimerBarFill.fillAmount = Mathf.MoveTowards(
                roundTimerBarFill.fillAmount, _roundTimerTargetFill, Time.deltaTime * BAR_LERP_SPEED);
        }
        if (transitionBarFill != null)
        {
            transitionBarFill.fillAmount = Mathf.MoveTowards(
                transitionBarFill.fillAmount, _transitionBarTargetFill, Time.deltaTime * BAR_LERP_SPEED);
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
        _transitionBarTargetFill = Mathf.Clamp01(remainingFraction);
        // 満タンから始める瞬間・消す瞬間は補間せず即座に反映する
        if (transitionBarFill != null && (!show || remainingFraction > transitionBarFill.fillAmount + 0.3f))
            transitionBarFill.fillAmount = _transitionBarTargetFill;
    }

    // ラウンド制限時間の残り時間バーを更新する
    public void UpdateRoundTimerBar(float remainingFraction)
    {
        if (roundTimerBarPanel != null) roundTimerBarPanel.SetActive(true);
        float clamped = Mathf.Clamp01(remainingFraction);
        // ラウンド開始時(満タンにリセット)や全員確定による大幅短縮は補間せず即反映する
        if (roundTimerBarFill != null && Mathf.Abs(clamped - roundTimerBarFill.fillAmount) > 0.3f)
            roundTimerBarFill.fillAmount = clamped;
        _roundTimerTargetFill = clamped;
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
        var pwField = PasswordInputField;
        if (pwField != null) pwField.gameObject.SetActive(!inRoom);

        if (lobbyPanel != null) lobbyPanel.SetActive(inRoom && !inGame);
        if (myCardParent != null) myCardParent.SetActive(inGame);
        if (othersCardParent != null) othersCardParent.SetActive(inGame);
        if (othersLabelsParent != null) othersLabelsParent.SetActive(inGame); // 以前ここが漏れており、空でも常時アクティブなプレースホルダー矩形がロビーのUIと重なっていた
        // RoundResultPanelの役割は「各プレイヤー行の右端に出したカードを表示する」形で
        // 使用済み一覧(All)側に統合したため、旧パネル自体はもう使わない。
        if (roundResultPanel != null) roundResultPanel.SetActive(false);

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

        var btnSettingsGO = GameObject.Find("Canvas")?.transform.Find("BtnSettings")?.gameObject;
        if (btnSettingsGO != null) btnSettingsGO.SetActive(inRoom && !inGame && isHost);

        // Leave Room / History ボタンは部屋にいる間ずっと表示する(ゲーム中/ロビー中どちらでも離脱・履歴確認できるように)
        var canvasForButtons = GameObject.Find("Canvas")?.transform;
        var btnLeaveGO = canvasForButtons?.Find("BtnLeaveRoom")?.gameObject;
        if (btnLeaveGO != null) btnLeaveGO.SetActive(inRoom);
        // 横持ちで余裕がある画面では履歴を常駐表示し、開閉ボタン自体を不要にする。
        // 狭い画面ではボタンによるポップアップ形式にする。
        bool docked = ResponsiveCanvasScaler.IsHistoryDockedMode;
        var btnHistoryGO = canvasForButtons?.Find("BtnHistory")?.gameObject;
        if (btnHistoryGO != null) btnHistoryGO.SetActive(inRoom && !docked);
        var historyPanelForDock = canvasForButtons?.Find("HistoryPanel")?.gameObject;
        if (historyPanelForDock != null && docked) historyPanelForDock.SetActive(inRoom);
        var btnCloseHistoryGO = canvasForButtons?.Find("HistoryPanel/BtnCloseHistory")?.gameObject;
        if (btnCloseHistoryGO != null) btnCloseHistoryGO.SetActive(!docked);
        if (!inRoom)
        {
            var historyPanelGO = canvasForButtons?.Find("HistoryPanel")?.gameObject;
            if (historyPanelGO != null) historyPanelGO.SetActive(false);
            var gameOverPanelGO = canvasForButtons?.Find("GameOverPanel")?.gameObject;
            if (gameOverPanelGO != null) gameOverPanelGO.SetActive(false);
        }
        if (!inRoom || inGame)
        {
            var settingsPanel = FindSettingsPanel();
            if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
        }

        // StatusTextはゲーム中の状況表示専用。Leaveやゲーム終了でロビー/タイトルに戻った際に
        // 前回の結果表示(「Player 0 wins the round」等)が残り続けてしまっていたため、
        // ゲーム中以外では明示的にクリアする。
        if (statusText != null && !inGame)
        {
            statusText.text = "";
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

        // 現在人数/参加上限をロビーに表示する
        var localPlayer = NetworkClient.connection?.identity?.GetComponent<Player>();
        var gm = localPlayer != null ? localPlayer.gameManager : null;
        var canvas = GameObject.Find("Canvas");
        var playerCountText = canvas != null ? canvas.transform.Find("LobbyPanel/PlayerCountText")?.GetComponent<TMP_Text>() : null;
        if (playerCountText != null)
        {
            int max = gm != null ? gm.MaxPlayers : 8;
            playerCountText.text = "Players: " + totalCount + " / " + max;
        }
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
        roomManager.CmdCreateRoom(txt, GetRoomPassword());
    }

    private System.Collections.IEnumerator AutoHostThenCreateRoom(string txt)
    {
        NetworkManager.singleton.StartHost();
        yield return new WaitUntil(() => NetworkClient.ready);
        roomManager.CmdCreateRoom(txt, GetRoomPassword());
    }

    public void ButtonJoinRoom()
    {
        string txt = inputField.text;
        roomManager.CmdJoinRoom(txt, GetRoomPassword(), connectionToClient);
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
        float canvasWidth = GetCanvasWidth();
        float canvasHeightForCards = GetCanvasHeight();
        // 履歴パネルが右側に常駐している場合、その分だけ使える幅が減る
        float maxRowWidth = canvasWidth * 0.92f * ResponsiveCanvasScaler.AvailableGameWidthRatio;

        // 1行に収めた場合の縮小率を試算し、小さくなりすぎる場合は複数行に折り返す。
        // 間隔の上限は幅だけでなく高さからも制限する(横長画面で幅基準のみだとカードが
        // 大きくなりすぎ、縦方向の余白が確保できなくなるため)。
        bool cardsIsPortrait = ResponsiveCanvasScaler.IsPortraitMode;
        float heightBasedCap = (canvasHeightForCards * (cardsIsPortrait ? 0.32f : 0.27f) * ResponsiveCanvasScaler.VerticalCompressionScale) / 3.45f; // 345*cardScale <= 高さの一定割合となるよう逆算(ResponsiveCanvasScalerの見積もりと一致させる。圧縮が掛かっている画面ではその分を反映する)
        // widthBasedCap/cardSpacingCapは折り返し行数(perRow)確定後に計算する(下記参照)

        // 折り返す(複数行にする)かどうかは、幅の制約だけで判断する。高さの制約を含めてしまうと、
        // 「高さが足りないから小さくする」場面でも誤って折り返しが発動してしまう
        // (行数を増やしても高さの制約はむしろ悪化するだけで、折り返す意味が無いため)。
        // 以前は「2行にするかどうか」の二択だったため、20枚のような多い枚数では
        // 1行10枚となり、カードの数字が読めないサイズ(20px程度)まで縮んでいた。
        // ここでは「1枚あたり最低これだけの幅が欲しい」という目標値から必要な行数を求め、
        // 必要なら3行以上にも折り返すことで、常に読めるサイズを保つ。
        // 横持ちは縦が狭いため、行を増やすと縦を圧迫する。横幅に余裕があるので
        // 1枚あたりの目標幅を下げて、なるべく少ない行数で横に並べる。
        float desiredCardSpacing = cardsIsPortrait ? 95f : 60f;
        int desiredPerRow = Mathf.Max(1, Mathf.FloorToInt(maxRowWidth / desiredCardSpacing));
        int maxRows = cardsIsPortrait ? 6 : 2; // 横持ちは2行までに抑える
        int rows = Mathf.Clamp(Mathf.CeilToInt((float)cnt / desiredPerRow), 1, maxRows);
        int perRow = Mathf.CeilToInt((float)cnt / rows);
        // 実際の行数をレイアウト側と共有する(行数が増えると縦方向の占有も増えるため)
        ResponsiveCanvasScaler.MyCardRowCount = rows;

        // 幅の制約は「折り返した後の1行あたり枚数(perRow)」で計算する。
        // 以前は全枚数(cnt)で割っていたため、20枚を2行に折り返しても
        // 「20枚を1行に並べる」前提の狭い値になり、下限60でクランプされてカードが
        // 極端に小さくなっていた。
        float widthBasedCap = maxRowWidth / Mathf.Max(perRow, 1);
        float cardSpacingCap = Mathf.Clamp(Mathf.Min(widthBasedCap, heightBasedCap), 60f, 210f);
        float spacing = Mathf.Min(cardSpacingCap, maxRowWidth / perRow);
        // 基準値を120にしていたため、実際のカード幅(約90)に対して大きすぎ、
        // 画面幅に余裕があってもスケールが1.0付近で頭打ちになり、カードが小さいままだった。
        // 実際のカード幅+最小間隔(95)を基準にして、余った幅を活かせるようにする。
        // 縦が狭い画面では、手札が縦を占有しすぎて使用済み一覧を圧迫する。
        // 手札に割ける高さから逆算した上限も掛けることで、両者のバランスを取る。
        float myAllowedHeight = canvasHeightForCards * (cardsIsPortrait ? 0.34f : 0.30f);
        float scaleCapByMyHeight = Mathf.Max(0.4f, (myAllowedHeight / Mathf.Max(rows, 1)) / 150f);
        float cardScale = Mathf.Clamp(Mathf.Min(spacing / 95f, scaleCapByMyHeight), 0.4f, 1.8f);
        float rowHeight = 145f * cardScale;

        // 実際にアンカーから下方向へ広がる距離をレイアウト側と共有する。
        // 見積もり式(345*scale等)は実配置とズレることがあり、それが原因で
        // 手札が画面下端からはみ出していたため、実値を使えるようにする。
        // rowHeightは行ピッチ(145*scale)。最下段カードはアンカーから rowHeight*rows だけ下がり、
        // さらにカード自身の高さの半分だけ下に広がる。余裕を持たせて確実に画面内に収める。
        // 実際の配置式( y = -300*cardScale + (rows-1-row)*rowHeight )に合わせる。
        // 最下段(row = rows-1)のyは -300*cardScale なので、そこからカード半分下がる。
        // 以前はこの-300オフセットを考慮しておらず、手札が画面外にはみ出していた。
        float newExtent = 300f * cardScale + 140f * cardScale * 0.5f + 30f;
        if (!Mathf.Approximately(newExtent, ResponsiveCanvasScaler.MyCardActualDownwardExtent))
        {
            // 値が変わった場合はレイアウトを再適用して手札の位置に反映させる
            // (カード描画はレイアウト計算より後に走るため、1回では反映されない)
            ResponsiveCanvasScaler.MyCardActualDownwardExtent = newExtent;
            var scalerComp = GameObject.Find("Canvas")?.GetComponent<ResponsiveCanvasScaler>();
            if (scalerComp != null) scalerComp.ReapplyLayout();
        }

        if (cnt > myCardParent.transform.childCount)
        {
            for (int i = myCardParent.transform.childCount; i < cnt; i++)
            {
                var card = Instantiate(cardUI, new Vector3(), Quaternion.identity, myCardParent.transform);
                var numUI = card.GetComponent<NumberCardUI>();
                numUI.Setup(i + 1);
                int capturedIndex = i;
                UnityAction func = () => { SelectCard(capturedIndex); };
                numUI.SetListener(func);
            }
        }
        for (int i = 0; i < cnt; i++)
        {
            int row = i / perRow;
            int rowStart = row * perRow;
            int itemsInRow = Mathf.Min(perRow, cnt - rowStart);
            int col = i - rowStart;

var child = myCardParent.transform.GetChild(i);
            child.gameObject.SetActive(true); // 以前に枚数が減った際に非表示化されたカードを再度有効化する
            var rT = child.GetComponent<RectTransform>();
            rT.localScale = new Vector3(cardScale, cardScale, cardScale);
            float x = col * spacing - itemsInRow * spacing * 0.5f + spacing * 0.5f;
            float y = -300f * cardScale + (rows - 1 - row) * rowHeight;
            rT.anchoredPosition = new Vector3(x, y, 0);

var numUI = child.GetComponent<NumberCardUI>();
            numUI.SetUsed(used[i]);
            numUI.SetSelected(i == _selectedCardIndex);
        }

        // カード枚数が(以前の部屋等より)減った場合、余分な古いカードが残ったままにならないよう非表示にする
        for (int i = cnt; i < myCardParent.transform.childCount; i++)
        {
            myCardParent.transform.GetChild(i).gameObject.SetActive(false);
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

    public void RefreshAllCardView(List<bool> used_all, int cnt)
    {
        int pcnt = used_all.Count / cnt;

        // ==== レイアウト計算(ラベルとカードで共通に使う) ====
        // 以前はラベル用とカード用で同じ計算を別々に書いていたため、片方だけ修正すると
        // 値がズレてラベルとカードが重なる不具合を繰り返していた。ここで一度だけ計算する。
        float canvasW = GetCanvasWidth();
        float canvasH = GetCanvasHeight();
        // 左右マージン分を差し引く。履歴パネルが常駐している場合はその分も除く。
        // さらに、右端に「今出したカード」枠(カード1枚分+間隔)を置くための幅も確保する。
        // 固定160だと画面によっては右端ギリギリになるため、やや広めに取る。
        float availableWidth = Mathf.Max(150f, canvasW * ResponsiveCanvasScaler.AvailableGameWidthRatio - 100f - 220f);
        bool isPortrait = ResponsiveCanvasScaler.IsPortraitMode;
        float expectedRows = isPortrait ? 4f : 2f;
        float heightCap = (canvasH * (isPortrait ? 0.25f : 0.18f) * ResponsiveCanvasScaler.VerticalCompressionScale / expectedRows) / 160f * 110f;

        // 枚数が多い場合は複数行に折り返す(1枚あたり最低限の幅を確保して数字を読めるようにする)。
        // 横持ちは縦が狭く横に余裕があるため、折り返して行数を増やすより
        // 「横幅を使い切って1行に収める」方が有利(行が増えると縦を圧迫するため)。
        // 実際、WXGAでは使用可能幅1046pxに対し429px(31%)しか使わずに折り返していた。
        float desiredSpacing = isPortrait ? 60f : 45f;
        int desiredPerRow = Mathf.Max(1, Mathf.FloorToInt(availableWidth / desiredSpacing));
        int maxSubRows = isPortrait ? 3 : 2; // 横持ちは縦が厳しいので行数を抑える
        int subRows = Mathf.Clamp(Mathf.CeilToInt((float)cnt / desiredPerRow), 1, maxSubRows);
        int perRow = Mathf.CeilToInt((float)cnt / subRows);

        // 幅から決まる間隔。heightCapで抑えると横の余白を活かせないため、
        // 幅の余裕がある場合はそちらを優先する。
        float widthSpacing = availableWidth / Mathf.Max(perRow, 1);
        float spacing = Mathf.Min(130f, isPortrait ? Mathf.Min(heightCap, widthSpacing) : widthSpacing);
        // 高さ圧縮が強い画面ではscaleが0.26まで落ちて数字が8pxとなり判読不能だったため下限を設ける。
        // ただし固定0.6にすると、縦が非常に狭い画面(WXGAで20枚×2人など)では
        // 一覧が画面高を超えて手札と重なってしまう。
        // そこで「一覧に使ってよい高さ」から逆算した上限も考慮し、両者の折り合いを取る。
        // 上部の余白配分を減らした分、一覧に高さを回して読めるサイズを確保する
        float allowedTotalHeight = canvasH * (isPortrait ? 0.42f : 0.46f);
        // 1プレイヤー分の高さ = 140*scale*subRows + 余白 + ラベル。これがallowed/pcnt以内に収まるscale上限
        float perPlayerAllowed = allowedTotalHeight / Mathf.Max(pcnt, 1);
        float scaleCapByHeight = Mathf.Max(0.34f, (perPlayerAllowed - 60f) / (140f * Mathf.Max(subRows, 1)));
        float scale = Mathf.Clamp(Mathf.Max(0.5f * (spacing / 55f), Mathf.Min(0.6f, scaleCapByHeight)), 0.34f, 1.8f);
        // NumberCardUIのネイティブ高さは約140。実際の描画高さはこれにscaleを掛けた値になる。
        // 以前は 80/0.5*scale という別の式を使っていたため実高さと合わず、行が重なっていた。
        float cardRowH = 140f * scale;
        // scaleを高さ制約側で引き上げた場合、幅由来のspacingのままだとカード同士が
        // 重なって隙間なく詰まって見える。カード幅(90*scale)+余白を最低間隔として確保し、
        // それで横幅に収まらない場合は逆にscaleを下げて辻褄を合わせる。
        float neededSpacing = 90f * scale + 6f;
        if (neededSpacing * perRow > availableWidth)
        {
            scale = Mathf.Max(0.3f, (availableWidth / perRow - 6f) / 90f);
            cardRowH = 140f * scale;
            neededSpacing = 90f * scale + 6f;
        }
        spacing = Mathf.Max(spacing, neededSpacing);
        // ラベルは補助情報なので、主役であるカードより大きくならないようにする。
        // 以前は画面サイズから独立に決めていたため、カード高さ123pxに対しラベル69px(56%)と
        // 主従が逆転して見えていた。カード高さの約35%を上限とする。
        float labelFontMax = Mathf.Clamp(cardRowH * 0.35f, 22f, 48f);
        float labelHeight = labelFontMax * 1.4f; // 1行分

        // 1プレイヤー分の縦占有 = (カード実高さ × サブ行数) + サブ行間の余白 + ラベル高さ + 余白
        float rowHeight = cardRowH * subRows + (subRows - 1) * 8f + labelHeight + 24f;
        // ラベルはそのプレイヤーの領域の最上部に配置する(pivotが中心なので半分だけ下げる)
        float labelTopMargin = -labelHeight * 0.5f;

        // 一覧全体が実際に占有する高さをレイアウト側と共有する。
        // scale下限などで見積もりとズレると、手札と重なってしまうため実値を使う。
        float actualTotalHeight = rowHeight * pcnt;
        if (!Mathf.Approximately(actualTotalHeight, ResponsiveCanvasScaler.OthersActualTotalHeight))
        {
            ResponsiveCanvasScaler.OthersActualTotalHeight = actualTotalHeight;
            var scalerForOthers = GameObject.Find("Canvas")?.GetComponent<ResponsiveCanvasScaler>();
            if (scalerForOthers != null) scalerForOthers.ReapplyLayout();
        }

        // ==== ラベル ====
        if (othersLabelsParent != null)
        {
            var localPlayer = GetDebugOrLocalPlayer();
            var gm = localPlayer != null ? localPlayer.gameManager : null;

            while (othersLabelsParent.transform.childCount < pcnt)
            {
                var labelGo = new GameObject("PlayerLabel" + othersLabelsParent.transform.childCount);
                labelGo.transform.SetParent(othersLabelsParent.transform, false);
                var newLabelRt = labelGo.AddComponent<RectTransform>();
                newLabelRt.pivot = new Vector2(0.5f, 0.5f);
                var newLabelTmp = labelGo.AddComponent<TextMeshProUGUI>();
                newLabelTmp.enableAutoSizing = true;
                newLabelTmp.fontSizeMin = 10;
                newLabelTmp.enableWordWrapping = false;
                newLabelTmp.overflowMode = TextOverflowModes.Truncate;
                newLabelTmp.alignment = TextAlignmentOptions.Center;
                newLabelTmp.color = Color.white;
            }

            int actualLocalPlayerId = NetworkClient.connection?.identity?.GetComponent<Player>()?.playerId ?? -1;
            for (int i = 0; i < othersLabelsParent.transform.childCount; i++)
            {
                var labelChild = othersLabelsParent.transform.GetChild(i);
                labelChild.gameObject.SetActive(i < pcnt);
                if (i >= pcnt) continue;

                var labelChildRt = labelChild.GetComponent<RectTransform>();
                // 枠が小さいとautoSizeで文字が縮んで読めなくなるため、使いたいフォントサイズに合わせる
                // 枠が広すぎると右側の「今出したカード」枠にかぶるため、
                // 実際のカード列の幅に収まるようにする。
                float labelBoxWidth = Mathf.Min(Mathf.Max(300f, canvasW * 0.5f), Mathf.Max(200f, (perRow - 1) * spacing));
                labelChildRt.sizeDelta = new Vector2(labelBoxWidth, labelHeight);
                labelChildRt.anchoredPosition = new Vector2(0, i * -rowHeight + labelTopMargin);

                var labelTmp = labelChild.GetComponent<TextMeshProUGUI>();
                labelTmp.fontSizeMax = labelFontMax;
                int points = (gm != null && i < gm.roundWins.Count) ? gm.roundWins[i] : 0;
                bool isMe = (i == actualLocalPlayerId);
                // ここは横に十分な幅があり、かつ行ごとに縦に並んでいるので、
                // 横に長くなるデメリットがない。改行せず1行で表示する。
                labelTmp.richText = true;
                labelTmp.text = "Player " + i + (isMe ? " (You)" : "") + "  -  " + points + " pt";
                labelTmp.color = isMe ? new Color(1f, 0.85f, 0.25f, 1f) : Color.white;
                labelTmp.fontStyle = isMe ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // ==== 「今出したカード」枠(旧RoundResultPanelの統合先) ====
        // 各プレイヤー行の右端に、そのラウンドで出したカードを表示する。
        // 別パネルに分けていた頃はサイズ体系がAll側とズレていたため、ここに統合して
        // 同じscale・同じ計算で描画することで大きさを揃える。
        var localPlayerForPick = GetDebugOrLocalPlayer();
        var gmForPick = localPlayerForPick != null ? localPlayerForPick.gameManager : null;
        // 「使える幅の右端」を基準にすると画面の右端まで飛んで一覧から離れすぎるため、
        // 実際に並んでいるカード列の右端から、カード1枚分ほど間を空けた位置に置く。
        float actualRowWidth = (perRow - 1) * spacing;
        float playedCardX = actualRowWidth * 0.5f + spacing * 1.2f;

        if (playedCardsParent == null)
        {
            var pcGo = new GameObject("PlayedCardsParent");
            pcGo.transform.SetParent(othersCardParent.transform.parent, false);
            var pcRt = pcGo.AddComponent<RectTransform>();
            pcRt.anchorMin = new Vector2(0.5f, 1f);
            pcRt.anchorMax = new Vector2(0.5f, 1f);
            pcRt.pivot = new Vector2(0.5f, 1f);
            playedCardsParent = pcGo;
        }
        var othersParentRt = othersCardParent.GetComponent<RectTransform>();
        var playedParentRt = playedCardsParent.GetComponent<RectTransform>();
        playedParentRt.anchoredPosition = othersParentRt.anchoredPosition;
        playedCardsParent.SetActive(othersCardParent.activeSelf);

        while (playedCardsParent.transform.childCount < pcnt)
        {
            var pc = Instantiate(cardUI, playedCardsParent.transform);
            pc.name = "PlayedCard" + (playedCardsParent.transform.childCount - 1);
        }
        for (int i = 0; i < playedCardsParent.transform.childCount; i++)
        {
            var pcTf = playedCardsParent.transform.GetChild(i);
            pcTf.gameObject.SetActive(i < pcnt);
            if (i >= pcnt) continue;

            var pcRt = pcTf.GetComponent<RectTransform>();
            pcRt.localScale = new Vector3(scale, scale, scale); // All側と完全に同じスケール
            // その行のカード群と同じ高さ(1行目の中心)に置く
            float yForPlayed = -labelHeight - 12f - cardRowH * 0.5f;
            pcRt.anchoredPosition = new Vector2(playedCardX, i * -rowHeight + yForPlayed);

            var pcNum = pcTf.GetComponent<NumberCardUI>();
            var pl = (gmForPick != null && i < gmForPick.room.playerComponents.Count) ? gmForPick.room.playerComponents[i] : null;
            if (pl != null && pl.isReadytoTurn)
            {
                pcNum.SetupDisplay("?"); // 提出済みだが未公開
            }
            else
            {
                int revealed = (gmForPick != null && i < gmForPick.lastRevealedPicks.Count) ? gmForPick.lastRevealedPicks[i] : 0;
                pcNum.SetupDisplay(revealed > 0 ? revealed.ToString() : "-");
            }
        }

        // ==== カード ====
        for (int i = 0; i < pcnt; i++)
        {
            for (int j = 0; j < cnt; j++)
            {
                if (othersCardParent.transform.childCount <= i * cnt + j)
                {
                    var card = Instantiate(cardUI, new Vector3(), Quaternion.identity, othersCardParent.transform);
                    var numUI = card.GetComponent<NumberCardUI>();
                    numUI.Setup(j + 1);
                }
                var othersChild = othersCardParent.transform.GetChild(i * cnt + j);
                othersChild.gameObject.SetActive(true);
                var rT = othersChild.GetComponent<RectTransform>();
                rT.localScale = new Vector3(scale, scale, scale);

                int subRow = j / perRow;
                int col = j % perRow;
                int cardsInThisSubRow = Mathf.Min(perRow, cnt - subRow * perRow);
                float centeredX = col * spacing - (cardsInThisSubRow - 1) * spacing * 0.5f;
                // ラベルの下から各サブ行を積む
                // ラベル(高さlabelHeight)の下から余白を空けてカードを積む。
                // カードのpivotも中心なので、行の中心位置を指定する。
                float yInPlayer = -labelHeight - 12f - subRow * (cardRowH + 8f) - cardRowH * 0.5f;
                rT.anchoredPosition = new Vector3(centeredX, i * -rowHeight + yInPlayer, 0);
            }
        }

        for (int i = 0; i < pcnt * cnt; i++)
        {
            int playerIndex = i / cnt;
            int cardIndex = i % cnt;
            othersCardParent.transform.GetChild(playerIndex * cnt + cardIndex).GetComponent<NumberCardUI>().SetUsed(used_all[i]);
        }

        // カード枚数が以前より減った場合、余分な古いカードが残らないよう非表示にする
        for (int i = pcnt * cnt; i < othersCardParent.transform.childCount; i++)
        {
            othersCardParent.transform.GetChild(i).gameObject.SetActive(false);
        }
    }

    public void ShowResult(string message)
    {
        Debug.Log(message);
        if (statusText != null) statusText.text = message;
    }

    // ゲーム終了時、通常のラウンド結果表示(小さく目立たない)とは別に、
    // 明確に分かる専用オーバーレイを表示する。プレイヤーがタップ/クリックするまで残り続ける。
    public void ShowGameOverPanel(string message)
    {
        var canvas = GameObject.Find("Canvas");
        var panel = canvas != null ? canvas.transform.Find("GameOverPanel") : null;
        if (panel == null) return;

        var text = panel.Find("GameOverText")?.GetComponent<TMP_Text>();
        if (text != null) text.text = message;

        panel.gameObject.SetActive(true);
    }

    public void ButtonCloseGameOver()
    {
        var canvas = GameObject.Find("Canvas");
        var panel = canvas != null ? canvas.transform.Find("GameOverPanel") : null;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    // --- ラウンド履歴パネル ---
    private Transform FindHistoryPanel()
    {
        var canvas = GameObject.Find("Canvas");
        return canvas != null ? canvas.transform.Find("HistoryPanel") : null;
    }

    public void ButtonToggleHistory()
    {
        var panel = FindHistoryPanel();
        if (panel != null) panel.gameObject.SetActive(!panel.gameObject.activeSelf);
    }

    // 新しいゲーム開始時に、前回のゲームの履歴が残ったままにならないようクリアする
    public void ClearHistoryPanel()
    {
        var panel = FindHistoryPanel();
        if (panel == null) return;
        var content = panel.Find("Viewport/Content");
        if (content == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Destroy(content.GetChild(i).gameObject);
        }
    }

    // roundHistoryLog(SyncList)の変更を受けて、スクロールビューに1行追記する。
    // 表示はテキストの羅列ではなく、カードUIを使ったリッチな1行にする。
    public void AppendHistoryLine(int roundNumber, List<int> playedCards, int winnerId, bool tie, string resultLabel)
    {
        var panel = FindHistoryPanel();
        if (panel == null) return;
        var content = panel.Find("Viewport/Content");
        if (content == null) return;

        float canvasH = GetCanvasHeight();

        // 1行を角丸カード風のパネルにして、デバッグログ然とした見た目から脱却する。
        // 構成: [角丸背景] - [ラウンド番号バッジ] - [各プレイヤーの出したカード(NumberCardUIを流用)] - [勝敗テキスト]
        var lineGo = new GameObject("Line" + content.childCount);
        lineGo.transform.SetParent(content, false);
        lineGo.AddComponent<RectTransform>();
        var layoutElem = lineGo.AddComponent<UnityEngine.UI.LayoutElement>();
        float rowHeight = Mathf.Clamp(canvasH * 0.075f, 70f, 150f); // カードの数字が読める高さを確保する
        layoutElem.preferredHeight = rowHeight;
        layoutElem.flexibleWidth = 1;

        var bgImg = lineGo.AddComponent<UnityEngine.UI.Image>();
        if (roundedButtonSprite != null)
        {
            bgImg.sprite = roundedButtonSprite;
            bgImg.type = UnityEngine.UI.Image.Type.Sliced;
        }
        bgImg.color = new Color(1f, 1f, 1f, 0.07f); // ごく薄い背景で行を区切る

        // ラウンド番号バッジ
        var badgeGo = new GameObject("RoundBadge");
        badgeGo.transform.SetParent(lineGo.transform, false);
        var badgeRt = badgeGo.AddComponent<RectTransform>();
        badgeRt.anchorMin = new Vector2(0f, 0.5f);
        badgeRt.anchorMax = new Vector2(0f, 0.5f);
        badgeRt.pivot = new Vector2(0f, 0.5f);
        float badgeSize = rowHeight * 0.6f;
        badgeRt.sizeDelta = new Vector2(badgeSize * 1.6f, badgeSize);
        badgeRt.anchoredPosition = new Vector2(8f, 0f);
        var badgeImg = badgeGo.AddComponent<UnityEngine.UI.Image>();
        if (roundedButtonSprite != null)
        {
            badgeImg.sprite = roundedButtonSprite;
            badgeImg.type = UnityEngine.UI.Image.Type.Sliced;
        }
        badgeImg.color = new Color(0.29f, 0.56f, 0.89f, 0.9f);

        var badgeTextGo = new GameObject("Text");
        badgeTextGo.transform.SetParent(badgeGo.transform, false);
        var badgeTextRt = badgeTextGo.AddComponent<RectTransform>();
        badgeTextRt.anchorMin = Vector2.zero; badgeTextRt.anchorMax = Vector2.one;
        badgeTextRt.offsetMin = new Vector2(4, 2); badgeTextRt.offsetMax = new Vector2(-4, -2);
        var badgeTmp = badgeTextGo.AddComponent<TextMeshProUGUI>();
        badgeTmp.text = "R" + roundNumber;
        badgeTmp.enableAutoSizing = true;
        badgeTmp.fontSizeMin = 10;
        badgeTmp.fontSizeMax = Mathf.Clamp(canvasH * 0.02f, 20f, 36f);
        badgeTmp.fontStyle = FontStyles.Bold;
        badgeTmp.enableWordWrapping = false;
        badgeTmp.overflowMode = TextOverflowModes.Truncate;
        badgeTmp.alignment = TextAlignmentOptions.Center;
        badgeTmp.color = Color.white;

        // 各プレイヤーが出したカードをNumberCardUIで表示する。
        // 横幅が狭い画面(縦持ちのポップアップ等)では、カード枚数が多いとバッジや結果テキストと
        // 重なってしまうため、実際に使える幅からカードサイズを逆算して収める。
        var panelRtForWidth = panel.GetComponent<RectTransform>();
        float panelWidth = panelRtForWidth != null ? panelRtForWidth.rect.width : 500f;
        float resultTextWidth = Mathf.Max(90f, panelWidth * 0.22f);
        float cardsAreaStart = badgeSize * 1.6f + 16f;
        float cardsAreaWidth = Mathf.Max(40f, panelWidth - cardsAreaStart - resultTextWidth - 24f);

        int cardCountInRow = Mathf.Max(1, playedCards.Count);
        float cardScaleByHeight = (rowHeight * 0.88f) / 140f;
        float cardScaleByWidth = (cardsAreaWidth / cardCountInRow) / 95f; // 95 = カード幅90 + 最小間隔
        float cardScale = Mathf.Min(cardScaleByHeight, cardScaleByWidth);
        float cardSpacing = 90f * cardScale + 4f;
        float cardsStartX = cardsAreaStart;
        for (int i = 0; i < playedCards.Count; i++)
        {
            var cardGo = Instantiate(cardUI, lineGo.transform);
            var cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0f, 0.5f);
            cardRt.anchorMax = new Vector2(0f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.localScale = new Vector3(cardScale, cardScale, cardScale);
            cardRt.anchoredPosition = new Vector2(cardsStartX + i * cardSpacing + cardSpacing * 0.5f, 0f);
            var numUI = cardGo.GetComponent<NumberCardUI>();
            numUI.SetupDisplay(playedCards[i].ToString());
            // 勝者のカードだけ強調する
            numUI.SetSelected(!tie && i == winnerId);
        }

        // 結果テキスト(右寄せ)
        var resultTextGo = new GameObject("ResultText");
        resultTextGo.transform.SetParent(lineGo.transform, false);
        var resultRt = resultTextGo.AddComponent<RectTransform>();
        resultRt.anchorMin = new Vector2(1f, 0.5f);
        resultRt.anchorMax = new Vector2(1f, 0.5f);
        resultRt.pivot = new Vector2(1f, 0.5f);
        resultRt.sizeDelta = new Vector2(resultTextWidth, rowHeight * 0.8f);
        resultRt.anchoredPosition = new Vector2(-10f, 0f);
        var resultTmp = resultTextGo.AddComponent<TextMeshProUGUI>();
        resultTmp.text = resultLabel;
        resultTmp.enableAutoSizing = true;
        resultTmp.fontSizeMin = 12;
        resultTmp.fontSizeMax = Mathf.Clamp(canvasH * 0.018f, 18f, 32f);
        resultTmp.enableWordWrapping = false;
        resultTmp.overflowMode = TextOverflowModes.Truncate;
        resultTmp.alignment = TextAlignmentOptions.MidlineRight;
        resultTmp.color = tie ? new Color(0.8f, 0.8f, 0.8f, 1f) : new Color(1f, 0.85f, 0.25f, 1f);

        // 追加のたびに一番下(最新)まで自動スクロールする
        var scrollRect = panel.GetComponent<UnityEngine.UI.ScrollRect>();
        if (scrollRect != null) Canvas.ForceUpdateCanvases();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }

    // 部屋を離脱してタイトル画面に戻る
    public void ButtonLeaveRoom()
    {
        var localPlayer = NetworkClient.connection?.identity?.GetComponent<Player>();
        if (localPlayer == null || localPlayer.room == null) return;
        roomManager.CmdLeaveRoom(localPlayer.room.roomId, connectionToClient);
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

        // 画面幅に収まるようパネル自体の幅とスロット間隔を動的に決める
float canvasWidthForResults = GetCanvasWidth();
        // パネル幅・スロット間隔の上限を固定値(480/110)にすると、画面が広くてもそれ以上大きくならなかったため、
        // 画面幅に応じて上限自体を引き上げる。
// 履歴パネルが右側に常駐している場合は、その分だけ結果パネルの幅を狭めて重ならないようにする
float maxPanelWidth = canvasWidthForResults * 0.6f * ResponsiveCanvasScaler.AvailableGameWidthRatio;
        var panelRt = roundResultPanel.GetComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(maxPanelWidth, panelRt.sizeDelta.y);

        int slotCountForSpacing = Mathf.Max(players.Count, 1);
        // 上限を固定220にしていたため、パネル幅が1572もある4Kでも間隔が220に制限され、
        // その狭い間隔にラベルを収めようとして文字が極端に縮んでいた。
        // 画面サイズに応じた上限にして、広い画面では余裕を活かす。
        float slotSpacingCap = Mathf.Clamp(canvasWidthForResults * 0.12f, 220f, 480f);
        float resultSlotSpacing = Mathf.Min(slotSpacingCap, (maxPanelWidth - 20f) / slotCountForSpacing);
        // スロットのスケールは、幅由来の見積もりだけでなく、パネルの実際の高さ(圧縮されている場合は
        // その圧縮後の値)を必ず超えないようにする。以前は幅のみで決めていたため、パネルの高さが
        // 圧縮された画面でスロット(ネイティブ高さ140)がパネルからはみ出すことがあった。
        float panelActualHeight = panelRt.sizeDelta.y;
        float heightBasedSlotScaleCap = Mathf.Max(0.3f, (panelActualHeight - 20f) / 140f);
        float resultSlotScale = Mathf.Clamp(Mathf.Min(resultSlotSpacing / 110f, heightBasedSlotScaleCap), 0.3f, 1.4f);

        while (roundResultPanel.transform.childCount < players.Count)
        {
            int slotIndex = roundResultPanel.transform.childCount;

            var slotGo = new GameObject("Slot" + slotIndex);
            slotGo.transform.SetParent(roundResultPanel.transform, false);
            var slotRt = slotGo.AddComponent<RectTransform>();
            slotRt.sizeDelta = new Vector2(90, 140);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(slotGo.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            // 枠が小さいままだと文字が自動縮小されて読めなくなるため、フォントサイズに合わせて広げる。
        // ただし、隣のスロットのラベルと重ならないよう、スロット間隔(スケール適用後の実間隔)を超えないようにする。
        float slotLabelFontMax = Mathf.Clamp(Mathf.Min(GetCanvasWidth(), GetCanvasHeight() * 1.6f) * 0.018f, 32f, 60f);
        float labelMaxWidth = Mathf.Max(60f, (resultSlotSpacing / Mathf.Max(resultSlotScale, 0.01f)) - 8f);
        labelRt.sizeDelta = new Vector2(Mathf.Min(Mathf.Max(90f, slotLabelFontMax * 6f), labelMaxWidth), slotLabelFontMax * 1.4f);
            labelRt.anchoredPosition = new Vector2(0, 60);
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.enableAutoSizing = true;
            labelTmp.fontSizeMin = 9;
            labelTmp.fontSizeMax = Mathf.Clamp(Mathf.Min(GetCanvasWidth(), GetCanvasHeight() * 1.6f) * 0.018f, 32f, 60f); // 画面連動
            labelTmp.enableWordWrapping = false;
            labelTmp.overflowMode = TextOverflowModes.Truncate;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.color = Color.white;

            var cardGo = Instantiate(cardUI, slotGo.transform);
            var cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchoredPosition = new Vector2(0, -15);
            // 0.7だとスロットスケールと掛け合わさって、使用済み一覧のカードより小さくなり
            // 「今出したカード」なのに目立たなくなっていた。等倍にして主役として見せる。
            cardRt.localScale = Vector3.one;
        }

        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            var slot = roundResultPanel.transform.GetChild(i);
            slot.gameObject.SetActive(true);

            var slotRtLive = slot.GetComponent<RectTransform>();
            slotRtLive.localScale = new Vector3(resultSlotScale, resultSlotScale, resultSlotScale);
            slotRtLive.anchoredPosition = new Vector2((i - (slotCountForSpacing - 1) * 0.5f) * resultSlotSpacing, 0);

            bool isMeResult = (p.playerId == (NetworkClient.connection?.identity?.GetComponent<Player>()?.playerId ?? -1));
            var labelTf = slot.Find("Label");
            var label = labelTf.GetComponent<TextMeshProUGUI>();

            // ラベルの枠サイズは生成時にしか設定されておらず、解像度やスロット数が変わっても
            // 更新されないままだった。そのため枠が狭すぎて文字が極端に縮小され、
            // 文字数の違いでプレイヤーごとにフォントサイズがバラつく問題が起きていた。
            // スロット間隔(スケール適用後の実間隔)に収まる範囲で毎回更新する。
            var labelRtLive = labelTf.GetComponent<RectTransform>();
            // ラベルは補助情報なので、主役であるカード(ネイティブ高さ140)より大きくしない。
            // 使用済み一覧側と同じ「カード高さの35%」基準に揃えて、画面内で大きさを統一する。
            float slotLabelFontMaxLive = Mathf.Clamp(140f * 0.35f, 22f, 48f);
            float labelWidthLive = Mathf.Max(80f, (resultSlotSpacing / Mathf.Max(resultSlotScale, 0.01f)) - 6f);
            // (You)を改行して2行にするため、高さは2行分確保する
            labelRtLive.sizeDelta = new Vector2(labelWidthLive, slotLabelFontMaxLive * 2.4f);
            label.fontSizeMax = slotLabelFontMaxLive;
            label.enableWordWrapping = false;

            // 「(You)」は改行した上で小さく表示する。横に並べたまま縮小しても幅は詰まらないため、
            // 改行することで幅を抑えつつ、2行目が縦に長くなりすぎないようサイズも落とす。
            label.richText = true;
            label.text = "Player " + p.playerId + (isMeResult ? "\n<size=65%>(You)</size>" : "");
            label.color = isMeResult ? new Color(1f, 0.85f, 0.25f, 1f) : Color.white;

            var cardGo = slot.GetChild(1).gameObject;
            var numUI = cardGo.GetComponent<NumberCardUI>();
            cardGo.SetActive(true); // 未選択でも空欄プレースホルダーとして常に表示し、誰が出した/出していないか分かりやすくする

            if (p.isReadytoTurn)
            {
                numUI.SetupDisplay("?");
            }
            else
            {
                int idx = p.playerId;
                int revealed = (idx >= 0 && idx < gm.lastRevealedPicks.Count) ? gm.lastRevealedPicks[idx] : 0;
                if (revealed > 0)
                {
                    numUI.SetupDisplay(revealed.ToString());
                }
                else
                {
                    numUI.SetupDisplay("-"); // まだ確定していない(待機中)ことを示すプレースホルダー
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
