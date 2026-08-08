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
        float canvasWidth = GetCanvasWidth();
        float maxRowWidth = canvasWidth * 0.92f;

        // 1行に収めた場合の縮小率を試算し、小さくなりすぎる場合は複数行に折り返す
        float cardSpacingCap = Mathf.Clamp(maxRowWidth / Mathf.Max(cnt, 1), 60f, 210f); // 画面幅に応じて上限自体を引き上げる
        float singleRowSpacing = Mathf.Min(cardSpacingCap, maxRowWidth / Mathf.Max(cnt, 1));
        float singleRowScale = singleRowSpacing / 120f;

        int perRow = cnt;
        if (singleRowScale < 0.55f && cnt > 5)
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
            var rT = child.GetComponent<RectTransform>();
            rT.localScale = new Vector3(cardScale, cardScale, cardScale);
            float x = col * spacing - itemsInRow * spacing * 0.5f + spacing * 0.5f;
            float y = -300f * cardScale + (rows - 1 - row) * rowHeight;
            rT.anchoredPosition = new Vector3(x, y, 0);

            var numUI = child.GetComponent<NumberCardUI>();
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

            float othersSpacingForLabel = Mathf.Min(130f, Mathf.Max(150f, GetCanvasWidth() - 100f) / Mathf.Max(cnt, 1));
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
        float othersAvailableWidth = Mathf.Max(150f, othersCanvasWidth - 100f); // 左マージン60+右余白分を差し引く
// 間隔の上限を固定値(55)にすると、画面が広くてもそれ以上大きくならなかったため、130まで引き上げる
        float othersSpacing = Mathf.Min(130f, othersAvailableWidth / Mathf.Max(cnt, 1));
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
var rT = othersCardParent.transform.GetChild(i * cnt + j).GetComponent<RectTransform>();
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
        float resultSlotScale = Mathf.Clamp(resultSlotSpacing / 110f, 0.45f, 1.4f); // パネル高さの上限(220)を超えないよう安全な範囲に留める

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
