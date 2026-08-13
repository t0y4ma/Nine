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
        var pwField = PasswordInputField;
        if (pwField != null) pwField.gameObject.SetActive(!inRoom);

        if (lobbyPanel != null) lobbyPanel.SetActive(inRoom && !inGame);
        if (myCardParent != null) myCardParent.SetActive(inGame);
        if (othersCardParent != null) othersCardParent.SetActive(inGame);
        if (othersLabelsParent != null) othersLabelsParent.SetActive(inGame); // 以前ここが漏れており、空でも常時アクティブなプレースホルダー矩形がロビーのUIと重なっていた
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

        var btnSettingsGO = GameObject.Find("Canvas")?.transform.Find("BtnSettings")?.gameObject;
        if (btnSettingsGO != null) btnSettingsGO.SetActive(inRoom && !inGame && isHost);
        if (!inRoom || inGame)
        {
            var settingsPanel = FindSettingsPanel();
            if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
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
        float maxRowWidth = canvasWidth * 0.92f;

        // 1行に収めた場合の縮小率を試算し、小さくなりすぎる場合は複数行に折り返す。
        // 間隔の上限は幅だけでなく高さからも制限する(横長画面で幅基準のみだとカードが
        // 大きくなりすぎ、縦方向の余白が確保できなくなるため)。
        float widthBasedCap = maxRowWidth / Mathf.Max(cnt, 1);
        bool cardsIsPortrait = ResponsiveCanvasScaler.IsPortraitMode;
        float heightBasedCap = (canvasHeightForCards * (cardsIsPortrait ? 0.32f : 0.27f) * ResponsiveCanvasScaler.VerticalCompressionScale) / 3.45f; // 345*cardScale <= 高さの一定割合となるよう逆算(ResponsiveCanvasScalerの見積もりと一致させる。圧縮が掛かっている画面ではその分を反映する)
        float cardSpacingCap = Mathf.Clamp(Mathf.Min(widthBasedCap, heightBasedCap), 60f, 210f);

        // 折り返す(複数行にする)かどうかは、幅の制約だけで判断する。高さの制約を含めてしまうと、
        // 「高さが足りないから小さくする」場面でも誤って折り返しが発動してしまう
        // (行数を増やしても高さの制約はむしろ悪化するだけで、折り返す意味が無いため)。
        float widthOnlySingleRowSpacing = Mathf.Min(210f, widthBasedCap);
        float widthOnlySingleRowScale = widthOnlySingleRowSpacing / 120f;

        int perRow = cnt;
        if (widthOnlySingleRowScale < 0.75f && cnt > 5)
        {
            perRow = Mathf.CeilToInt(cnt / 2f);
        }
        int rows = Mathf.CeilToInt((float)cnt / perRow);

        float spacing = Mathf.Min(cardSpacingCap, maxRowWidth / perRow);
        float cardScale = Mathf.Clamp(spacing / 120f, 0.4f, 1.8f); // 画面が広い場合はカードも大きくする
        float rowHeight = 145f * cardScale;

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

    [Client]
    public void RefreshAllCardView(List<bool> used_all, int cnt)
    {
        int pcnt = used_all.Count / cnt;

        // 各行の上にプレイヤー名+ポイント数のラベルを表示する
        if (othersLabelsParent != null)
        {
            var localPlayer = GetDebugOrLocalPlayer();
            var gm = localPlayer != null ? localPlayer.gameManager : null;

            // カード側(othersSpacing)と全く同じ式を使うことで、ラベルとカードの間隔見積もりが必ず一致するようにする。
            // 以前はここだけ幅のみの式・圧縮率未反映だったため、圧縮が効く画面でラベルがカードにはみ出す原因になっていた。
            bool labelIsPortrait = ResponsiveCanvasScaler.IsPortraitMode;
            float labelExpectedRows = labelIsPortrait ? 4f : 2f;
            float labelHeightCap = (GetCanvasHeight() * (labelIsPortrait ? 0.25f : 0.18f) * ResponsiveCanvasScaler.VerticalCompressionScale / labelExpectedRows) / 160f * 110f;
            float othersSpacingForLabel = Mathf.Min(Mathf.Min(130f, labelHeightCap), Mathf.Max(150f, GetCanvasWidth() - 100f) / Mathf.Max(cnt, 1));
            float labelRowHeight = Mathf.Max(45f, 80f * (othersSpacingForLabel / 55f));
            // ラベルとカードの間隔も、カードスケールに比例させる(固定値だとカードが大きい時に重なるため)
            float labelTopMargin = labelRowHeight * 0.6f;
            while (othersLabelsParent.transform.childCount < pcnt)
            {
                int rowIndex = othersLabelsParent.transform.childCount;
                var labelGo = new GameObject("PlayerLabel" + rowIndex);
                labelGo.transform.SetParent(othersLabelsParent.transform, false);
                var labelRt = labelGo.AddComponent<RectTransform>();
                labelRt.pivot = new Vector2(0.5f, 0.5f);
                labelRt.sizeDelta = new Vector2(300, 24);
                labelRt.anchoredPosition = new Vector2(0, rowIndex * -labelRowHeight + labelTopMargin);
                var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
                labelTmp.enableAutoSizing = true;
                labelTmp.fontSizeMin = 10;
                labelTmp.fontSizeMax = 40; // カードが大きくなった分、ラベルも読める大きさまで拡大できるようにする
                labelTmp.enableWordWrapping = false;
                labelTmp.overflowMode = TextOverflowModes.Truncate;
                labelTmp.alignment = TextAlignmentOptions.Center;
                labelTmp.color = Color.white;
            }
            int actualLocalPlayerId = NetworkClient.connection?.identity?.GetComponent<Player>()?.playerId ?? -1;
            for (int i = 0; i < pcnt; i++)
            {
                var labelChild = othersLabelsParent.transform.GetChild(i);
                var labelChildRt = labelChild.GetComponent<RectTransform>();
                labelChildRt.anchoredPosition = new Vector2(0, i * -labelRowHeight + labelTopMargin);
                var labelTmp = labelChild.GetComponent<TextMeshProUGUI>();
                int points = (gm != null && i < gm.roundWins.Count) ? gm.roundWins[i] : 0;
                bool isMe = (i == actualLocalPlayerId);
                labelTmp.text = "Player " + i + (isMe ? " (You)" : "") + "  -  " + points + " pt";
                labelTmp.color = isMe ? new Color(1f, 0.85f, 0.25f, 1f) : Color.white;
                labelTmp.fontStyle = isMe ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        float othersCanvasWidth = GetCanvasWidth();
        float othersCanvasHeight = GetCanvasHeight();
        float othersAvailableWidth = Mathf.Max(150f, othersCanvasWidth - 100f); // 左マージン60+右余白分を差し引く
        // 間隔の上限は幅だけでなく高さからも制限する(横長画面で幅基準のみだと大きくなりすぎるため)。
        // ResponsiveCanvasScaler側の見積もり計算と必ず一致させること。
        bool othersIsPortrait = ResponsiveCanvasScaler.IsPortraitMode;
        float othersExpectedRows = othersIsPortrait ? 4f : 2f;
        float othersHeightCap = (othersCanvasHeight * (othersIsPortrait ? 0.25f : 0.18f) * ResponsiveCanvasScaler.VerticalCompressionScale / othersExpectedRows) / 160f * 110f;
        float othersSpacing = Mathf.Min(Mathf.Min(130f, othersHeightCap), othersAvailableWidth / Mathf.Max(cnt, 1));
        float othersScale = 0.5f * (othersSpacing / 55f);
        float othersRowHeight = Mathf.Max(45f, 80f * othersScale / 0.5f);

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
                othersChild.gameObject.SetActive(true); // 以前に枚数が減った際に非表示化されたカードを再度有効化する
                var rT = othersChild.GetComponent<RectTransform>();
                rT.localScale = new Vector3(othersScale, othersScale, othersScale);
                float centeredX = j * othersSpacing - (cnt - 1) * othersSpacing * 0.5f;
                rT.anchoredPosition = new Vector3(centeredX, i * -othersRowHeight, 0);
            }
        }
for (int i = 0; i < pcnt * cnt; i++)
        {
            int playerIndex = i / cnt;
            int cardIndex = i % cnt;
            othersCardParent.transform.GetChild(playerIndex * cnt + cardIndex).GetComponent<NumberCardUI>().SetUsed(used_all[i]);
        }

        // カード枚数(pcnt*cnt)が以前より減った場合、余分な古いカードが残ったままにならないよう非表示にする
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
float maxPanelWidth = canvasWidthForResults * 0.6f;
        var panelRt = roundResultPanel.GetComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(maxPanelWidth, panelRt.sizeDelta.y);

        int slotCountForSpacing = Mathf.Max(players.Count, 1);
        float resultSlotSpacing = Mathf.Min(220f, (maxPanelWidth - 20f) / slotCountForSpacing);
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
            labelRt.sizeDelta = new Vector2(90, 20);
            labelRt.anchoredPosition = new Vector2(0, 60);
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.enableAutoSizing = true;
            labelTmp.fontSizeMin = 9;
            labelTmp.fontSizeMax = 32; // スロットが大きくなった分、ラベルも読める大きさまで拡大できるようにする
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

            var slotRtLive = slot.GetComponent<RectTransform>();
            slotRtLive.localScale = new Vector3(resultSlotScale, resultSlotScale, resultSlotScale);
            slotRtLive.anchoredPosition = new Vector2((i - (slotCountForSpacing - 1) * 0.5f) * resultSlotSpacing, 0);

            bool isMeResult = (p.playerId == (NetworkClient.connection?.identity?.GetComponent<Player>()?.playerId ?? -1));
            var label = slot.Find("Label").GetComponent<TextMeshProUGUI>();
            label.text = "Player " + p.playerId + (isMeResult ? " (You)" : "");
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
