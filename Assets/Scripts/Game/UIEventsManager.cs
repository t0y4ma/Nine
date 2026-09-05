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

        // モーダルなので最前面に出す。
        // (他要素が上に重なると、背景が透けているように見えてしまう)
        panel.SetAsLastSibling();

        // 開くたびにレイアウト(位置・サイズ・フォント)を適用し直す。
        // シーン上に残っている固定値のままにならないようにするため。
        // パネルの親階層からScalerを辿る(GameObject.Find("Canvas")は
        // 名前変更や非アクティブ時に失敗するため使わない)
        var scaler = panel.GetComponentInParent<ResponsiveCanvasScaler>();
        if (scaler != null) scaler.ReapplySettingsPanel();
        SetModalDimActive(true);

        var localPlayer = GetDebugOrLocalPlayer();
        var gm = localPlayer != null ? localPlayer.gameManager : null;

        var cardCountInput = panel.Find("CardCountInput")?.GetComponent<TMP_InputField>();
        if (cardCountInput != null) cardCountInput.text = (gm != null ? gm.CARDCOUNT : 9).ToString();

        var maxPlayersInputOpen = panel.Find("MaxPlayersInput")?.GetComponent<TMP_InputField>();
        if (maxPlayersInputOpen != null) maxPlayersInputOpen.text = (gm != null ? gm.MaxPlayers : GameManager.MAX_PLAYERS_LIMIT).ToString();

        _pendingScoringMode = gm != null ? gm.ScoringMode : 0;
        RefreshScoringButtonHighlight(panel);

        panel.gameObject.SetActive(true);
    }

    public void ButtonCloseSettings()
    {
        var panel = FindSettingsPanel();
        if (panel != null) panel.gameObject.SetActive(false);
        SetModalDimActive(false);
    }

    // 暗幕の表示状態を設定画面に追従させる。
    // Applyや外部要因でパネルが閉じられても暗幕が残らないようにするための保険。
    private void SyncModalDimToPanel()
    {
        var panel = FindSettingsPanel();
        if (panel == null) return;
        var dim = panel.parent?.Find("ModalDim");
        if (dim == null) return;
        bool shouldShow = panel.gameObject.activeSelf;
        if (dim.gameObject.activeSelf != shouldShow) dim.gameObject.SetActive(shouldShow);
    }

    // モーダル背後の暗幕の表示を切り替える。
    // 暗幕はCanvas直下にあるため、パネルの表示状態と別に管理する必要がある。
    private void SetModalDimActive(bool active)
    {
        var canvasTf = FindSettingsPanel()?.parent;
        if (canvasTf == null) return;
        var dim = canvasTf.Find("ModalDim");
        if (dim != null) dim.gameObject.SetActive(active);
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
        cardCount = Mathf.Clamp(cardCount, 3, GameManager.CARD_COUNT_LIMIT);

        var maxPlayersInput = panel.Find("MaxPlayersInput")?.GetComponent<TMP_InputField>();
        int maxPlayers = GameManager.MAX_PLAYERS_LIMIT;
        if (maxPlayersInput != null && !string.IsNullOrEmpty(maxPlayersInput.text))
        {
            int.TryParse(maxPlayersInput.text, out maxPlayers);
        }
        maxPlayers = Mathf.Clamp(maxPlayers, 2, GameManager.MAX_PLAYERS_LIMIT);

        string roomId = inputField != null ? inputField.text : "";
        roomManager.CmdUpdateSettings(roomId, cardCount, _pendingScoringMode, maxPlayers, connectionToClient);

        panel.gameObject.SetActive(false);
        SetModalDimActive(false);
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

        // 暗幕は設定画面の表示状態に常に追従させる。
        // 個々の閉じ処理で消し忘れると暗いまま残るため、ここで一元管理する。
        SyncModalDimToPanel();

        // 「今出したカード」はOthersCardParentの兄弟オブジェクトなので、
        // 親の表示切替に自動では追従しない。ここで状態を合わせる。
        // (合わせないと、Leaveでロビーに戻ってもカードが残り続ける)
        if (playedCardsParent != null && othersCardParent != null)
        {
            bool shouldShow = othersCardParent.activeSelf;
            if (playedCardsParent.activeSelf != shouldShow)
                playedCardsParent.SetActive(shouldShow);
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
        // 履歴は常にボタンで開閉するポップアップ形式にする。
        // (常駐モードはゲーム本体の幅を圧迫し、レイアウト計算を複雑にしていたため廃止)
        bool docked = false;
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
            SetModalDimActive(false);
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
            int max = gm != null ? gm.MaxPlayers : GameManager.MAX_PLAYERS_LIMIT;
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
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
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

    // ===== カード表示 =====
    // 基準解像度(1920x1080)上で組む。他解像度へはCanvasScalerが自動スケールする。
    // 以前は画面サイズから毎回スケールを手計算していたが、CanvasScalerが
    // 正しく効くようになったため、固定サイズで組めるようになった。

    // NumberCardUIプレハブの実寸(唯一の定義)
    // NumberCardUIプレハブの実寸(RectTransform.sizeDeltaの実測値)。
    // 以前ここを140としていたため、実際(110)との30pxの誤差が
    // ラベル位置・ブロック高・手札の下端など、全ての縦計算をズラしていた。
    private const float CARD_W = 90f;
    private const float CARD_H = 110f;

    // カード列に使ってよい幅。Canvasの実幅から左右マージンを引いて求める。
    // 固定値(1800)にしていたため、実際のCanvas幅より狭い場合に
    // 横幅を25%しか使わずカードが極端に縮んでいた。
    private float RowMaxWidth => Mathf.Max(300f, GetCanvasWidth() - 80f);

    // 自分の手札: 大きめ。使用済み一覧: 小さめ。の2段階
    private const float MY_CARD_SCALE_MAX = 1.0f;
    private const float OTHERS_CARD_SCALE_MAX = 0.78f;

    // 選択したカードを確定して提出する
    public void ButtonConfirmCard()
    {
        var targetPlayer = GetDebugOrLocalPlayer();
        if (targetPlayer == null || targetPlayer.isReadytoTurn) return;
        if (_selectedCardIndex < 0) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // デバッグで他プレイヤー(Bot等)を操作している場合、そのPlayerの所有権は
        // こちらに無いためCommandが拒否される("called without authority")。
        // サーバー上で直接実行できるDebugUseCardを使う。
        if (debugTargetPlayer != null)
        {
            debugTargetPlayer.DebugUseCard(_selectedCardIndex);
            _selectedCardIndex = -1;
            if (confirmButtonGO != null) confirmButtonGO.SetActive(false);
            return;
        }
#endif
        targetPlayer.CmdUseCard(_selectedCardIndex);
        _selectedCardIndex = -1;
        if (confirmButtonGO != null) confirmButtonGO.SetActive(false);
    }

    // カードをクリックしたときの選択処理(同じカードを再度押すと選択解除)
    public void SelectCard(int cardindex)
    {
        var targetPlayer = GetDebugOrLocalPlayer();
        if (targetPlayer == null || targetPlayer.isReadytoTurn) return;
        if (cardindex < 0 || cardindex >= targetPlayer.used.Count) return;
        if (targetPlayer.used[cardindex]) return; // 使用済みは選べない

        _selectedCardIndex = (cardindex == _selectedCardIndex) ? -1 : cardindex;
        if (confirmButtonGO != null) confirmButtonGO.SetActive(_selectedCardIndex >= 0);
        RefreshMyCardView(targetPlayer.used.ToList(), targetPlayer.used.Count);
    }

    [Client]
    public void RefreshMyCardView(List<bool> used, int cnt)
    {
        try { RefreshMyCardViewInternal(used, cnt); }
        catch (System.Exception ex) { Debug.LogError("RefreshMyCardView失敗: " + ex); }
    }

    private void RefreshMyCardViewInternal(List<bool> used, int cnt)
    {
        if (myCardParent == null || cardUI == null) return;

        while (myCardParent.transform.childCount < cnt)
        {
            var card = Instantiate(cardUI, myCardParent.transform);
            int index = myCardParent.transform.childCount - 1;
            var numUI = card.GetComponent<NumberCardUI>();
            numUI.Setup(index + 1);
            var btn = card.GetComponent<UnityEngine.UI.Button>();
            if (btn != null)
            {
                int captured = index;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SelectCard(captured));
            }
        }

        // 1行に何枚並ぶかを幅から決める。入らなければ行を増やす。
        var layout = CalcCardLayout(cnt, MY_CARD_SCALE_MAX, RowMaxWidth);
        // 確定ボタンを手札の実際の高さに合わせて配置するため、行数を共有する
        ResponsiveCanvasScaler.MyCardRowCountForLayout = layout.rows;

        for (int i = 0; i < myCardParent.transform.childCount; i++)
        {
            var child = myCardParent.transform.GetChild(i);
            child.gameObject.SetActive(i < cnt);
            if (i >= cnt) continue;

            var rt = child.GetComponent<RectTransform>();
            // MyCardParentはpivot下端(0.5,0)なので、アンカーもそれに揃える
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.localScale = Vector3.one * layout.scale;
            rt.anchoredPosition = CardSlotPosition(i, layout, upward: true);

            var numUI = child.GetComponent<NumberCardUI>();
            numUI.SetUsed(used[i]);
            numUI.SetSelected(i == _selectedCardIndex);
        }
    }

    public void RefreshAllCardView(List<bool> used_all, int cnt)
    {
        // 描画中の例外はRpc処理を巻き込んでクライアント切断を招くため、
        // ここで捕捉してログに残す(切断させない)。
        try { RefreshAllCardViewInternal(used_all, cnt); }
        catch (System.Exception ex) { Debug.LogError("RefreshAllCardView失敗: " + ex); }
    }

    private void RefreshAllCardViewInternal(List<bool> used_all, int cnt)
    {
        if (othersCardParent == null || cardUI == null) return;
        int pcnt = used_all.Count / Mathf.Max(cnt, 1);
        if (pcnt <= 0) return;

        // 右端に「今出したカード」枠を置くため、その分だけ狭い幅で計算する
        // === 一覧のレイアウト決定 ===
        // 横持ちは横幅が余る一方で縦が足りない。プレイヤーを2列に並べることで
        // 必要な縦の量が半分になり、その分カードを大きくできる。
        //   A    E
        //   B    F
        //   C    G
        //   D    H
        bool isPortraitOthers = ResponsiveCanvasScaler.IsPortraitMode;
        int columns = (!isPortraitOthers && pcnt >= 3) ? 2 : 1;
        int rowsOfPlayers = Mathf.CeilToInt((float)pcnt / columns);

        // 1列あたりに使える幅。2列なら半分に、さらに列間の余白を引く。
        float colGap = 60f;
        float widthPerColumn = (RowMaxWidth - colGap * (columns - 1)) / columns;
        // 各列の中で「今出したカード」枠の分も確保する
        float cardsWidthPerColumn = widthPerColumn - 150f;

        var layout = CalcCardLayout(cnt, OTHERS_CARD_SCALE_MAX, cardsWidthPerColumn, singleRow: true);

        // 一覧が使える高さ = 画面高 - 上部固定要素 - 手札領域 - 余白
        float canvasH = GetCanvasHeight();
        float topReserved = 230f;
        int myRows = Mathf.Max(1, ResponsiveCanvasScaler.MyCardRowCountForLayout);
        float myCardsReserved = 40f + 145f * myRows + 100f;
        float othersAllowedH = Mathf.Max(200f, canvasH - topReserved - myCardsReserved - 20f);

        float labelH = Mathf.Clamp(othersAllowedH / rowsOfPlayers * 0.22f, 24f, 46f);
        // ラベルとカードの間隔。ブロック高の計算とカード配置で同じ値を使うこと
        // ラベル下端とカード上端の隙間。
        // カードはpivot中心なので、配置基準から CARD_H*scale*0.5 下がった位置が中心になる。
        // そのぶんを差し引かないと、実際の隙間が想定よりずっと広くなり、
        // ラベルが「自分のカード」より「上のプレイヤーのカード」に近づいてしまう。
        const float LABEL_CARD_GAP = 6f;
        // 1人分の縦占有。
        // カードはラベル下端から (labelH + GAP) 下を基準に、中心が CARD_H*scale*0.5 下に来る。
        // つまりカード下端は基準から CARD_H*scale だけ下。行数分ぶん積む。
        // 以前はこの「中心基準で半分ずつ広がる」分を計上しておらず、
        // 次のプレイヤーのラベルが前のプレイヤーのカードに50px食い込んでいた。
        // 1人分の縦占有 = ラベル + 隙間 + カード行 + プレイヤー間の区切り余白。
        // 区切り余白(28)はラベル-カード間の隙間(6)より明確に広くすることで、
        // 「ラベルは自分のカードに属する」と視覚的に分かるようにする。
        float PlayerBlockHeight(float lh, CardLayout lay) =>
            lh + LABEL_CARD_GAP + lay.rows * (CARD_H * lay.scale + 6f) + 28f;
        float playerBlockH = PlayerBlockHeight(labelH, layout);

        // 縦に並ぶのは rowsOfPlayers 行分。収まるまで反復して縮める。
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float needed = playerBlockH * rowsOfPlayers;
            if (needed <= othersAllowedH) break;
            float shrink = Mathf.Max(0.7f, othersAllowedH / needed);
            layout.scale = Mathf.Max(0.22f, layout.scale * shrink);
            layout.rowPitch = CARD_H * layout.scale + 6f;
            layout.spacing = CARD_W * layout.scale + 6f;
            labelH = Mathf.Max(20f, labelH * shrink);
            playerBlockH = PlayerBlockHeight(labelH, layout);
            if (layout.scale <= 0.221f && labelH <= 20.1f) break;
        }

        // 各列の中心X。1列なら0、2列なら左右に振り分ける。
        float ColumnCenterX(int playerIndex)
        {
            if (columns == 1) return 0f;
            int col = playerIndex / rowsOfPlayers; // 左列を上から詰め、次に右列
            return (col == 0 ? -1f : 1f) * (widthPerColumn + colGap) * 0.5f;
        }
        float RowOffsetY(int playerIndex)
        {
            int row = playerIndex % rowsOfPlayers;
            return -row * playerBlockH;
        }

        EnsurePlayedCardsParent();
        var localPlayer = GetDebugOrLocalPlayer();
        var gm = localPlayer != null ? localPlayer.gameManager : null;
        int myId = NetworkClient.connection?.identity?.GetComponent<Player>()?.playerId ?? -1;

        // --- ラベル ---
        if (othersLabelsParent != null)
        {
            while (othersLabelsParent.transform.childCount < pcnt)
            {
                var go = new GameObject("PlayerLabel" + othersLabelsParent.transform.childCount);
                go.transform.SetParent(othersLabelsParent.transform, false);
                var lrt = go.AddComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0.5f, 1f);
                lrt.anchorMax = new Vector2(0.5f, 1f);
                lrt.pivot = new Vector2(0.5f, 1f);
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 14;
                tmp.fontSizeMax = 40;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Truncate;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;
            }
            for (int i = 0; i < othersLabelsParent.transform.childCount; i++)
            {
                var lc = othersLabelsParent.transform.GetChild(i);
                lc.gameObject.SetActive(i < pcnt);
                if (i >= pcnt) continue;
                var lrt = lc.GetComponent<RectTransform>();
                // カードと同じく親上端アンカーに揃える(揃えないと縦位置がずれる)
                lrt.anchorMin = new Vector2(0.5f, 1f);
                lrt.anchorMax = new Vector2(0.5f, 1f);
                lrt.pivot = new Vector2(0.5f, 1f);
                lrt.sizeDelta = new Vector2(Mathf.Min(700f, widthPerColumn * 0.95f), labelH);
                lrt.anchoredPosition = new Vector2(ColumnCenterX(i), RowOffsetY(i));

                var tmp = lc.GetComponent<TextMeshProUGUI>();
                int pts = (gm != null && i < gm.roundWins.Count) ? gm.roundWins[i] : 0;
                bool isMe = (i == myId);
                tmp.text = "Player " + i + (isMe ? " (You)" : "") + "  -  " + pts + " pt";
                tmp.color = isMe ? new Color(1f, 0.85f, 0.25f, 1f) : Color.white;
                tmp.fontStyle = isMe ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // --- 使用済みカード ---
        for (int i = 0; i < pcnt; i++)
        {
            for (int j = 0; j < cnt; j++)
            {
                int flat = i * cnt + j;
                if (othersCardParent.transform.childCount <= flat)
                {
                    var card = Instantiate(cardUI, othersCardParent.transform);
                    card.GetComponent<NumberCardUI>().Setup(j + 1);
                    var b = card.GetComponent<UnityEngine.UI.Button>();
                    if (b != null) b.interactable = false;
                }
                var oc = othersCardParent.transform.GetChild(flat);
                oc.gameObject.SetActive(true);
                var rt = oc.GetComponent<RectTransform>();
                // アンカーを親の上端(0.5,1)に揃える。
                // プレハブ既定の(0.5,0.5)のままだと、親の高さ(100)の半分だけ
                // 位置が下にずれ、ラベルとの間隔が50px広がってしまう。
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.localScale = Vector3.one * layout.scale;
                Vector2 p = CardSlotPosition(j, layout, upward: false);
                // p.y には既に -CARD_H*scale*0.5 (カード中心へのオフセット)が含まれる。
                // ラベル下端(RowOffsetY - labelH)から GAP だけ空けた位置がカード上端になる。
                rt.anchoredPosition = new Vector2(
                    p.x + ColumnCenterX(i),
                    p.y + RowOffsetY(i) - labelH - LABEL_CARD_GAP);
                oc.GetComponent<NumberCardUI>().SetUsed(used_all[flat]);
            }
        }
        for (int i = pcnt * cnt; i < othersCardParent.transform.childCount; i++)
            othersCardParent.transform.GetChild(i).gameObject.SetActive(false);

        // --- 今出したカード(各行の右端) ---
        // 「今出したカード」は各プレイヤーのカード列の右隣に置く
        RefreshPlayedCards(pcnt, layout, labelH, LABEL_CARD_GAP, gm,
            ColumnCenterX, RowOffsetY);
    }

    // カード列のレイアウト結果
    private struct CardLayout
    {
        public float scale;
        public int perRow;
        public int rows;
        public float spacing;
        public float rowPitch;
    }

    // 幅に収まる枚数から、スケールと行数を決める。
    // 収まらない場合は行を増やす(縮めて押し込まない)。
    // カード列のレイアウトを決める。
    // singleRow=true の場合は「必ず1行に収める」ことを優先し、
    // 入らなければスケールを下げる。
    // (使用済み一覧は人数分だけ縦に積むため、行数が増えると縦を大きく圧迫する。
    //  6人x15枚のようなケースでは、多少小さくしてでも1行にした方が全体が収まる)
    private CardLayout CalcCardLayout(int cnt, float maxScale, float availableWidth, bool singleRow = false)
    {
        var r = new CardLayout();
        r.scale = maxScale;

        if (singleRow)
        {
            r.perRow = cnt;
            r.rows = 1;
            // cnt枚が availableWidth に収まるスケールを求める
            float needPitch = availableWidth / Mathf.Max(cnt, 1);
            float fitScale = (needPitch - 6f) / CARD_W;
            r.scale = Mathf.Clamp(Mathf.Min(maxScale, fitScale), 0.3f, maxScale);
            r.spacing = CARD_W * r.scale + 6f;
            r.rowPitch = CARD_H * r.scale + 8f;
            return r;
        }

        float pitch = CARD_W * r.scale + 10f;
        r.perRow = Mathf.Max(1, Mathf.FloorToInt(availableWidth / pitch));
        if (r.perRow >= cnt)
        {
            r.perRow = cnt;
        }
        else
        {
            // 行数を均等に割る
            int need = Mathf.CeilToInt((float)cnt / r.perRow);
            r.perRow = Mathf.CeilToInt((float)cnt / need);
        }
        r.rows = Mathf.CeilToInt((float)cnt / r.perRow);
        r.spacing = pitch;
        r.rowPitch = CARD_H * r.scale + 8f;
        return r;
    }

    // index番目のカードの位置(親のpivot基準)。
    // upward=true なら下から上へ積む(手札)、false なら上から下へ積む(一覧)。
    private Vector2 CardSlotPosition(int index, CardLayout layout, bool upward)
    {
        int row = index / layout.perRow;
        int col = index % layout.perRow;
        int inThisRow = Mathf.Min(layout.perRow, 999);
        float x = (col - (inThisRow - 1) * 0.5f) * layout.spacing;
        float y = upward
            ? (layout.rows - 1 - row) * layout.rowPitch + CARD_H * layout.scale * 0.5f
            : -(row * layout.rowPitch) - CARD_H * layout.scale * 0.5f;
        return new Vector2(x, y);
    }

    private void EnsurePlayedCardsParent()
    {
        if (playedCardsParent != null) return;

        // OthersCardParentの子にすると、カードをindexで走査する処理が
        // この管理用オブジェクトを掴んでしまいNullReferenceになる。
        // そのため兄弟として作り、位置だけ毎回OthersCardParentに合わせる。
        var staleChild = othersCardParent.transform.Find("PlayedCardsParent");
        if (staleChild != null) DestroyImmediate(staleChild.gameObject);

        var existing = othersCardParent.transform.parent?.Find("PlayedCardsParent");
        if (existing != null)
        {
            playedCardsParent = existing.gameObject;
            return;
        }
        var go = new GameObject("PlayedCardsParent");
        go.transform.SetParent(othersCardParent.transform.parent, false);
        go.AddComponent<RectTransform>();
        playedCardsParent = go;
    }

    // 各プレイヤー行の右端に「今出したカード」を表示する
    // 各プレイヤーのカード列の右隣に「今出したカード」を表示する
    private void RefreshPlayedCards(int pcnt, CardLayout layout, float labelH, float labelGap,
        GameManager gm, System.Func<int, float> columnCenterX, System.Func<int, float> rowOffsetY)
    {
        if (playedCardsParent == null) return;
        playedCardsParent.SetActive(othersCardParent.activeSelf);

        // 兄弟オブジェクトなので、OthersCardParentと同じ座標系になるよう毎回同期する
        var ocRt = othersCardParent.GetComponent<RectTransform>();
        var pcRt = playedCardsParent.GetComponent<RectTransform>();
        pcRt.anchorMin = ocRt.anchorMin;
        pcRt.anchorMax = ocRt.anchorMax;
        pcRt.pivot = ocRt.pivot;
        pcRt.anchoredPosition = ocRt.anchoredPosition;

        while (playedCardsParent.transform.childCount < pcnt)
        {
            var pc = Instantiate(cardUI, playedCardsParent.transform);
            var b = pc.GetComponent<UnityEngine.UI.Button>();
            if (b != null) b.interactable = false;
        }

        float rowW = (layout.perRow - 1) * layout.spacing;

        for (int i = 0; i < playedCardsParent.transform.childCount; i++)
        {
            var c = playedCardsParent.transform.GetChild(i);
            c.gameObject.SetActive(i < pcnt);
            if (i >= pcnt) continue;

            var rt = c.GetComponent<RectTransform>();
            // 使用済みカードと同じく親上端アンカーに揃える
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.localScale = Vector3.one * layout.scale;
            rt.anchoredPosition = new Vector2(
                columnCenterX(i) + rowW * 0.5f + layout.spacing,
                rowOffsetY(i) - labelH - labelGap - CARD_H * layout.scale * 0.5f);

            var num = c.GetComponent<NumberCardUI>();
            // gm.roomはクライアント側では未同期のことがあるためnullチェックが必須。
            var pl = (gm != null && gm.room != null && i < gm.room.playerComponents.Count)
                ? gm.room.playerComponents[i] : null;
            int revealed = (gm != null && i < gm.lastRevealedPicks.Count) ? gm.lastRevealedPicks[i] : 0;

            if (revealed > 0)
            {
                num.SetupDisplay(revealed.ToString());
                num.SetSelected(false);
            }
            else if (pl != null && pl.isReadytoTurn)
            {
                // 提出済みだが未公開: 選択済みと分かるようハイライトする
                num.SetupDisplay("?");
                num.SetSelected(true);
            }
            else
            {
                num.SetupDisplay("-");
                num.SetSelected(false);
            }
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

        // 全画面オーバーレイなので最前面に出す
        panel.SetAsLastSibling();
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
        if (panel == null) return;
        bool willShow = !panel.gameObject.activeSelf;
        // オーバーレイなので、開くときは最前面に持ってくる
        // (他の要素が上に重なると、背景が透けているように見えてしまう)
        if (willShow) panel.SetAsLastSibling();
        panel.gameObject.SetActive(willShow);
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
        badgeTmp.textWrappingMode = TextWrappingModes.NoWrap;
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
        float cardScaleByHeight = (rowHeight * 0.88f) / CARD_H;
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
        resultTmp.textWrappingMode = TextWrappingModes.NoWrap;
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
        var players = FindObjectsByType<Player>(UnityEngine.FindObjectsSortMode.None)
            .Where(p => p.gameManager == gm)
            .OrderBy(p => p.playerId)
            .ToList();

        // 画面幅に収まるようパネル自体の幅とスロット間隔を動的に決める
float canvasWidthForResults = GetCanvasWidth();
        // パネル幅・スロット間隔の上限を固定値(480/110)にすると、画面が広くてもそれ以上大きくならなかったため、
        // 画面幅に応じて上限自体を引き上げる。
// 履歴パネルが右側に常駐している場合は、その分だけ結果パネルの幅を狭めて重ならないようにする
float maxPanelWidth = canvasWidthForResults * 0.6f;
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
            labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
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
            label.textWrappingMode = TextWrappingModes.NoWrap;

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
