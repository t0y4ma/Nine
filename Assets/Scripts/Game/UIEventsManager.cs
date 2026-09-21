using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
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

    private RectTransform GetCanvasRect()
    {
        if (_canvasRt == null)
        {
            var canvasGo = GameObject.Find("Canvas");
            if (canvasGo != null) _canvasRt = canvasGo.GetComponent<RectTransform>();
        }
        return _canvasRt;
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

        // 得点カード(ハゲタカのえじき)のボタン。シーンには無いので、既存のボタンを複製して見た目を揃える。
        var btnPointTf = panel.Find("BtnScoringPointCards");
        if (btnPointTf == null && btnSum != null)
        {
            var clone = Instantiate(btnSum.gameObject, panel);
            clone.name = "BtnScoringPointCards";
            var cloneLabel = clone.GetComponentInChildren<TMP_Text>(true);
            if (cloneLabel != null) cloneLabel.text = "Point Cards (Hagetaka)";
            btnPointTf = clone.transform;
        }
        var btnPoint = btnPointTf != null ? btnPointTf.GetComponent<UnityEngine.UI.Button>() : null;
        if (btnPoint != null)
        {
            // 複製元のインスペクタ設定のリスナーが残らないよう、イベントごと作り直す
            btnPoint.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            btnPoint.onClick.AddListener(() => SelectScoringMode(GameManager.SCORING_POINT_CARDS));
        }

        // 選択時間のスライダー(シーンには無いのでここで作る。位置はResponsiveCanvasScalerが決める)
        EnsureTimeLimitControls(panel);

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

        var timeSlider = EnsureTimeLimitControls(panel);
        if (timeSlider != null)
        {
            int idx = SecondsToTimeSliderIndex(gm != null ? gm.RoundTimeLimit : GameManager.ROUND_TIME_DEFAULT);
            timeSlider.SetValueWithoutNotify(idx);
            UpdateTimeLimitLabel(panel, idx);
        }

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
        if (panel == null) return;
        RefreshScoringButtonHighlight(panel);

        // ハゲタカのえじきは1~15の15枚で遊ぶので、選んだらカード枚数を15にしておく(変更は可能)
        if (mode == GameManager.SCORING_POINT_CARDS)
        {
            var cardCountInput = panel.Find("CardCountInput")?.GetComponent<TMP_InputField>();
            if (cardCountInput != null) cardCountInput.text = GameManager.CARD_COUNT_LIMIT.ToString();
        }
    }

    private void RefreshScoringButtonHighlight(Transform panel)
    {
        var btnFixedImg = panel.Find("BtnScoringFixed")?.GetComponent<UnityEngine.UI.Image>();
        var btnSumImg = panel.Find("BtnScoringSum")?.GetComponent<UnityEngine.UI.Image>();
        Color selectedColor = new Color(0.29f, 0.72f, 0.56f, 1f);
        Color unselectedColor = new Color(0.35f, 0.38f, 0.45f, 1f);
        if (btnFixedImg != null) btnFixedImg.color = (_pendingScoringMode == 0) ? selectedColor : unselectedColor;
        if (btnSumImg != null) btnSumImg.color = (_pendingScoringMode == 1) ? selectedColor : unselectedColor;
        var btnPointImg = panel.Find("BtnScoringPointCards")?.GetComponent<UnityEngine.UI.Image>();
        if (btnPointImg != null) btnPointImg.color = (_pendingScoringMode == GameManager.SCORING_POINT_CARDS) ? selectedColor : unselectedColor;
    }

    // ===== 選択時間(スライダー) =====
    // 目盛りの位置 → 秒数。30秒から15秒刻みで5分まで、右端は無制限(0)。
    private const int TIME_SLIDER_UNLIMITED_INDEX =
        (GameManager.ROUND_TIME_MAX - GameManager.ROUND_TIME_MIN) / GameManager.ROUND_TIME_STEP + 1;

    private static int TimeSliderIndexToSeconds(int index)
    {
        if (index >= TIME_SLIDER_UNLIMITED_INDEX) return 0;
        return GameManager.ROUND_TIME_MIN + Mathf.Max(0, index) * GameManager.ROUND_TIME_STEP;
    }

    private static int SecondsToTimeSliderIndex(int seconds)
    {
        if (seconds <= 0) return TIME_SLIDER_UNLIMITED_INDEX;
        int clamped = Mathf.Clamp(seconds, GameManager.ROUND_TIME_MIN, GameManager.ROUND_TIME_MAX);
        return Mathf.RoundToInt((clamped - GameManager.ROUND_TIME_MIN) / (float)GameManager.ROUND_TIME_STEP);
    }

    private static string FormatTimeLimit(int seconds)
    {
        if (seconds <= 0) return "Unlimited";
        int m = seconds / 60;
        int sec = seconds % 60;
        if (m == 0) return sec + "s";
        return sec == 0 ? m + " min" : m + " min " + sec + "s";
    }

    private void UpdateTimeLimitLabel(Transform panel, int index)
    {
        var label = panel.Find("TimeLimitLabel")?.GetComponent<TMP_Text>();
        if (label != null) label.text = "Time Limit: " + FormatTimeLimit(TimeSliderIndexToSeconds(index));
    }

    // 選択時間のラベルとスライダーを(無ければ)作る。
    // スライダー本体はUnity標準の部品(DefaultControls)をそのまま使う。
    private Slider EnsureTimeLimitControls(Transform panel)
    {
        if (panel.Find("TimeLimitLabel") == null)
        {
            var go = new GameObject("TimeLimitLabel", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            // 他の設定項目のラベルと同じ見た目にする
            var refLabel = panel.Find("CardCountLabel")?.GetComponent<TMP_Text>();
            if (refLabel != null)
            {
                t.font = refLabel.font;
                t.color = refLabel.color;
                t.alignment = refLabel.alignment;
            }
            t.enableAutoSizing = true;
            t.raycastTarget = false;
        }

        var sliderTf = panel.Find("TimeLimitSlider");
        if (sliderTf == null)
        {
            var res = new DefaultControls.Resources
            {
                background = roundedButtonSprite,
                standard = roundedButtonSprite,
                knob = roundedButtonSprite
            };
            var go = DefaultControls.CreateSlider(res);
            go.name = "TimeLimitSlider";
            go.transform.SetParent(panel, false);

            var slider = go.GetComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = TIME_SLIDER_UNLIMITED_INDEX;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(v => UpdateTimeLimitLabel(panel, Mathf.RoundToInt(v)));

            // 得点方式ボタン(選択中/非選択)と同じ配色にする
            var bg = go.transform.Find("Background")?.GetComponent<Image>();
            if (bg != null) bg.color = new Color(0.35f, 0.38f, 0.45f, 1f);
            var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
            if (fill != null) fill.color = new Color(0.29f, 0.72f, 0.56f, 1f);
            var knob = go.transform.Find("Handle Slide Area/Handle")?.GetComponent<Image>();
            if (knob != null) knob.color = Color.white;

            sliderTf = go.transform;
        }
        return sliderTf.GetComponent<Slider>();
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

        var timeSliderApply = panel.Find("TimeLimitSlider")?.GetComponent<Slider>();
        int roundTimeLimit = timeSliderApply != null
            ? TimeSliderIndexToSeconds(Mathf.RoundToInt(timeSliderApply.value))
            : GameManager.ROUND_TIME_DEFAULT;

        string roomId = inputField != null ? inputField.text : "";
        roomManager.CmdUpdateSettings(roomId, cardCount, _pendingScoringMode, maxPlayers, roundTimeLimit, connectionToClient);

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
        HandleShortcuts();
        bool connected = NetworkClient.isConnected;
        if (connected != _lastConnected)
        {
            _lastConnected = connected;
            RefreshLobbyPanels();
        }

        // 暗幕は設定画面の表示状態に常に追従させる。
        // 個々の閉じ処理で消し忘れると暗いまま残るため、ここで一元管理する。
        SyncModalDimToPanel();


        // 履歴の暗幕も同様に、パネルの表示状態へ常に追従させる
        var histPanel = FindHistoryPanel();
        if (histPanel != null)
        {
            var hdim = histPanel.parent?.Find("HistoryDim");
            if (hdim != null && hdim.gameObject.activeSelf != histPanel.gameObject.activeSelf)
                hdim.gameObject.SetActive(histPanel.gameObject.activeSelf);
        }

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

    // この遷移中にNextを押したプレイヤー。押した人にはボタンを出し直さない。
    // (デバッグでBotを切り替えて操作する場合があるため、プレイヤーごとに覚える)
    private readonly HashSet<Player> _nextPressedBy = new();

    public void ButtonNextRound()
    {
        // 次へ進むならポップアップは役目を終えるので閉じる
        HideRoundResultPopup();

        var targetPlayer = GetDebugOrLocalPlayer();
        if (targetPlayer == null) return;

        // 一度押したら消す(遷移が終わるまで再表示しない)
        _nextPressedBy.Add(targetPlayer);
        if (nextRoundButtonGO != null) nextRoundButtonGO.SetActive(false);

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
        // 遷移が終わったら「押した」記録を消し、次のラウンド後にまた出せるようにする
        if (!show) _nextPressedBy.Clear();
        if (transitionBarPanel != null) transitionBarPanel.SetActive(show);
        var nextTarget = GetDebugOrLocalPlayer();
        bool alreadyPressed = nextTarget != null && _nextPressedBy.Contains(nextTarget);
        if (nextRoundButtonGO != null) nextRoundButtonGO.SetActive(show && !alreadyPressed);
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
    // subtitle: 得点カードモードでは今回の得点カード(例: "Card +7")。通常はnull。
    public void ShowRoundCutIn(int roundNumber, int totalRounds, string subtitle)
    {
        _selectedCardIndex = -1;
        if (confirmButtonGO != null) confirmButtonGO.SetActive(false);

        if (cutInPanel == null || cutInText == null) return;
        cutInText.text = "ROUND " + roundNumber + " / " + totalRounds
            + (string.IsNullOrEmpty(subtitle) ? "" : "   " + subtitle);
        if (_cutInCoroutine != null) StopCoroutine(_cutInCoroutine);
        _cutInCoroutine = StartCoroutine(CutInRoutine());
    }

    private System.Collections.IEnumerator CutInRoutine()
    {
        // 画面サイズに合わせてから表示する。
        // 900x260固定だと、縦持ち(Canvas幅979)では画面いっぱいになり、
        // さらに他要素の下に隠れて正しく見えていなかった。
        var rt = cutInPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(
            Mathf.Min(GetCanvasWidth() * 0.8f, 900f),
            Mathf.Min(GetCanvasHeight() * 0.16f, 260f));
        cutInPanel.transform.SetAsLastSibling(); // 最前面に出す

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
        // 部屋を抜けるとGameBoardごと非表示にしているので、ゲームに(戻って)入ったら表示し直す。
        // これが無いと、一度Leaveした後はゲームが始まっても盤面が出なかった。
        if (inGame)
        {
            var boardTf = GameObject.Find("Canvas")?.transform.Find("GameBoard");
            if (boardTf != null && !boardTf.gameObject.activeSelf) boardTf.gameObject.SetActive(true);
        }
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
            // 前ゲームの表示値が次のゲームに持ち越されないようにする
            ClearDisplayOverrides();
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
        roomManager.CmdJoinRoom(txt, GetRoomPassword(), GetClientToken());
    }

    // このブラウザ(端末)の識別子。ゲーム中に抜けた後、同じ部屋にJoinすると
    // この値で元の席を見つけて戻れる。再読み込みしても変わらないよう保存しておく。
    private const string CLIENT_TOKEN_KEY = "Nine.ClientToken";
    private static string GetClientToken()
    {
        string token = PlayerPrefs.GetString(CLIENT_TOKEN_KEY, "");
        if (string.IsNullOrEmpty(token))
        {
            token = System.Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(CLIENT_TOKEN_KEY, token);
            PlayerPrefs.Save();
        }
        return token;
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
            tmp.fontSizeMin = 1;
            tmp.fontSizeMax = 300;
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

    // サーバーから明示的に渡された「今出したカード」の値。
    // SyncListの同期はRpcと到着順が保証されないため、表示にはこちらを優先する。
    private int[] _revealedPicksOverride;
    public void SetRevealedPicksOverride(int[] picks) => _revealedPicksOverride = picks;
    // 得点も同様。SyncList(roundWins)の同期待ちで表示が1ラウンド遅れるのを防ぐ。
    private int[] _scoresOverride;
    public void SetScoresOverride(int[] scores) => _scoresOverride = scores;
    // ゲーム開始/退室時に、前ゲームの値が残らないようクリアする
    public void ClearDisplayOverrides() { _revealedPicksOverride = null; _scoresOverride = null; }

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
    // 使用済み一覧のカード上限。手札(1.0)より小さくして主従を明確にしつつ、
    // 縦持ちで折り返した際に十分大きくできる値にする。
    private const float OTHERS_CARD_SCALE_MAX = 0.90f;

    // 選択したカードを確定して提出する
    // 確定ボタンの表示とラベルを現在の状態に合わせる。
    // 選択中は「Confirm」、確定済みは「Cancel」。
    // 全員が確定してタイマーが短縮されても、ラウンドが解決するまでは
    // 押せる状態を保つ(キャンセルできるようにするため)。
    public void RefreshConfirmButton()
    {
        if (confirmButtonGO == null) return;
        var pl = GetDebugOrLocalPlayer();
        bool submitted = pl != null && pl.isReadytoTurn;
        confirmButtonGO.SetActive(_selectedCardIndex >= 0 || submitted);
        var label = confirmButtonGO.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = submitted ? "Cancel" : "Confirm";
    }

    public void ButtonConfirmCard()
    {
        var targetPlayer = GetDebugOrLocalPlayer();
        if (targetPlayer == null) return;

        // 確定済みならキャンセルする(もう一度選び直せる)
        if (targetPlayer.isReadytoTurn)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugTargetPlayer != null) { debugTargetPlayer.DebugCancelCard(); return; }
#endif
            targetPlayer.CmdCancelCard();
            return;
        }

        if (_selectedCardIndex < 0) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // デバッグで他プレイヤー(Bot等)を操作している場合、そのPlayerの所有権は
        // こちらに無いためCommandが拒否される("called without authority")。
        // サーバー上で直接実行できるDebugUseCardを使う。
        if (debugTargetPlayer != null)
        {
            debugTargetPlayer.DebugUseCard(_selectedCardIndex);
            _selectedCardIndex = -1;
            // ボタンは残す(Cancelとして使えるようにするため)。
            // 表示とラベルはRefreshConfirmButtonが切り替える。
            RefreshConfirmButton();
            return;
        }
#endif
        targetPlayer.CmdUseCard(_selectedCardIndex);
        _selectedCardIndex = -1;
        RefreshConfirmButton();
    }

    // カードをクリックしたときの選択処理(同じカードを再度押すと選択解除)
    public void SelectCard(int cardindex)
    {
        var targetPlayer = GetDebugOrLocalPlayer();
        if (targetPlayer == null || targetPlayer.isReadytoTurn) return;
        if (cardindex < 0 || cardindex >= targetPlayer.used.Count) return;
        if (targetPlayer.used[cardindex]) return; // 使用済みは選べない

        _selectedCardIndex = (cardindex == _selectedCardIndex) ? -1 : cardindex;

        // 選択状態をサーバーにも伝える。
        // 時間切れの自動提出で、ランダムではなく
        // 選んでいたカードが出るようにするため。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (debugTargetPlayer != null) debugTargetPlayer.DebugSetPendingSelection(_selectedCardIndex);
        else targetPlayer.CmdSetPendingSelection(_selectedCardIndex);
#else
        targetPlayer.CmdSetPendingSelection(_selectedCardIndex);
#endif

        RefreshConfirmButton();
        RefreshMyCardView(targetPlayer.used.ToList(), targetPlayer.used.Count);
    }

    [Client]
    public void RefreshMyCardView(List<bool> used, int cnt)
    {
        try { RefreshMyCardViewInternal(used, cnt); }
        catch (System.Exception ex) { Debug.LogError("RefreshMyCardView失敗: " + ex); }
    }

    // 手札。一覧と同じくLayoutGroup + AspectRatioFitterで組む。
    private void RefreshMyCardViewInternal(List<bool> used, int cnt)
    {
        if (myCardParent == null || cardUI == null) return;

        var hl = myCardParent.GetComponent<HorizontalLayoutGroup>();
        if (hl == null) hl = myCardParent.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.spacing = 8f;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;   // 縦長になるのを防ぐ
        hl.padding = new RectOffset(10, 10, 6, 6);

        ResponsiveCanvasScaler.MyCardRowCountForLayout = 1;

        bool createdCard = false;
        while (myCardParent.transform.childCount < cnt)
        {
            createdCard = true;
            var card = Instantiate(cardUI, myCardParent.transform);
            int index = myCardParent.transform.childCount - 1;
            card.GetComponent<NumberCardUI>().Setup(index + 1);
            SetupAutoCard(card);
            var btn = card.GetComponent<UnityEngine.UI.Button>();
            if (btn != null)
            {
                int captured = index;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SelectCard(captured));
            }
        }

        for (int i = 0; i < myCardParent.transform.childCount; i++)
        {
            var child = myCardParent.transform.GetChild(i);
            child.gameObject.SetActive(i < cnt);
            if (i >= cnt) continue;
            var numUI = child.GetComponent<NumberCardUI>();
            numUI.SetUsed(used[i]);
            numUI.SetSelected(i == _selectedCardIndex);
        }

        // 手札も、親サイズ確定後(LateUpdate)にカードサイズを決める
        if (createdCard || !IsCardSized(myCardParent.transform))
            _pendingCardFit.Add((myCardParent.transform, cnt));
    }



    public void RefreshAllCardView(List<bool> used_all, int cnt)
    {
        // 描画中の例外はRpc処理を巻き込んでクライアント切断を招くため、
        // ここで捕捉してログに残す(切断させない)。
        try { RefreshAllCardViewInternal(used_all, cnt); }
        catch (System.Exception ex) { Debug.LogError("RefreshAllCardView失敗: " + ex); }
    }

    // 使用済みカード一覧。
    // 構造はすべてUnityのLayoutGroupで組み、サイズ計算はUnityに任せる。
    //   OthersCardParent (Vertical: プレイヤー行を縦に積む)
    //     └ PlayerRow (Horizontal: 横持ちは2人ずつ / 縦持ちは1人)
    //          └ PlayerBlock (Vertical)
    //               ├ Label
    //               └ CardRows (Horizontal: カードを横に並べる)
    //                    └ Card (AspectRatioFitterで縦横比を保証)
    private void RefreshAllCardViewInternal(List<bool> used_all, int cnt)
    {
        if (othersCardParent == null || cardUI == null) return;
        int pcnt = used_all.Count / Mathf.Max(cnt, 1);
        if (pcnt <= 0) return;

        if (othersLabelsParent != null && othersLabelsParent.activeSelf)
            othersLabelsParent.SetActive(false);

        bool portrait = ResponsiveCanvasScaler.IsPortraitMode;
        int perRow = (!portrait && pcnt >= 3) ? 2 : 1;   // 1行に並べるプレイヤー数
        int rowCount = Mathf.CeilToInt((float)pcnt / perRow);

        var localPlayer = GetDebugOrLocalPlayer();
        var gm = localPlayer != null ? localPlayer.gameManager : null;
        int myId = NetworkClient.connection?.identity?.GetComponent<Player>()?.playerId ?? -1;

        // 親: プレイヤー行を縦に積む。高さは各行で等分する。
        var rootVl = othersCardParent.GetComponent<VerticalLayoutGroup>();
        if (rootVl == null) rootVl = othersCardParent.AddComponent<VerticalLayoutGroup>();
        rootVl.childAlignment = TextAnchor.UpperCenter;
        rootVl.spacing = 2f;
        rootVl.childControlWidth = true;
        rootVl.childControlHeight = true;
        rootVl.childForceExpandWidth = true;
        rootVl.childForceExpandHeight = true;   // 余った高さを行で分け合う

        // 旧構造の残骸を掃除
        for (int i = othersCardParent.transform.childCount - 1; i >= 0; i--)
        {
            var ch = othersCardParent.transform.GetChild(i);
            if (!ch.name.StartsWith("PlayerRow")) DestroyImmediate(ch.gameObject);
        }

        while (othersCardParent.transform.childCount < rowCount)
            CreatePlayerRow(othersCardParent.transform);

        for (int r = 0; r < othersCardParent.transform.childCount; r++)
        {
            var row = othersCardParent.transform.GetChild(r);
            row.gameObject.SetActive(r < rowCount);
            if (r >= rowCount) continue;

            while (row.childCount < perRow) CreatePlayerBlock(row);

            // この行に実際に入るプレイヤー数(最終行は端数になることがある)
            int inThisRow = Mathf.Min(perRow, pcnt - r * perRow);

            for (int k = 0; k < row.childCount; k++)
            {
                int i = r * perRow + k;            // プレイヤー番号
                var block = row.GetChild(k);
                // 使わないブロックは非表示にし、幅の配分から除外する。
                // (残しておくと空きスペースができて一覧が崩れる)
                block.gameObject.SetActive(k < inThisRow);
                if (!block.gameObject.activeSelf) continue;

                // ラベル
                var label = block.Find("Label").GetComponent<TextMeshProUGUI>();
                int pts;
                if (_scoresOverride != null && i < _scoresOverride.Length) pts = _scoresOverride[i];
                else pts = (gm != null && i < gm.roundWins.Count) ? gm.roundWins[i] : 0;
                bool isMe = (i == myId);
                label.text = "Player " + i + (isMe ? " (You)" : "") + "  -  " + pts + " pt";
                label.color = isMe ? new Color(1f, 0.85f, 0.25f, 1f) : Color.white;
                label.fontStyle = isMe ? FontStyles.Bold : FontStyles.Normal;

                // カード行
                var rowsTf = block.Find("CardRows");
                bool createdCard = false;
                while (rowsTf.childCount < cnt)
                {
                    createdCard = true;
                    var card = Instantiate(cardUI, rowsTf);
                    card.GetComponent<NumberCardUI>().Setup(rowsTf.childCount);
                    // 一覧は表示専用。押せないようにし、
                    // クリック判定(raycast)も持たせない。
                    // Button自体は残す(NumberCardUIが参照しているため)。
                    var b = card.GetComponent<UnityEngine.UI.Button>();
                    if (b != null) b.interactable = false;
                    foreach (var g in card.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                        g.raycastTarget = false;
                    SetupAutoCard(card);
                }
                for (int j = 0; j < rowsTf.childCount; j++)
                {
                    var c = rowsTf.GetChild(j);
                    c.gameObject.SetActive(j < cnt);
                    if (j < cnt) c.GetComponent<NumberCardUI>().SetUsed(used_all[i * cnt + j]);
                }
                // カードサイズはブロックの実サイズが確定してから決める。
                // 生成直後はまだ高さが未確定なので、この場では行わず
                // レイアウト確定後に実行するよう予約する。
                // カードを新しく作ったとき、またはまだサイズが決まっていない
                // ときだけ予約する。既に決まっていれば再計算しない
                // (Confirmのたびに計算し直すとサイズがガタつくため)。
                if (createdCard || !IsCardSized(rowsTf))
                    _pendingCardFit.Add((rowsTf, cnt));
            }
        }

        EnsurePlayedCardsParent();
        // 帯は後から生成されるため、生成後に改めて高さの配分を適用する
        var scaler = othersCardParent.GetComponentInParent<ResponsiveCanvasScaler>();
        if (scaler != null) scaler.ReapplyBoardSlots();
        RefreshPlayedCards(pcnt, gm);
    }

    // プレイヤーを横に並べる行
    private void CreatePlayerRow(Transform parent)
    {
        var row = new GameObject("PlayerRow" + parent.childCount);
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>();
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.spacing = 24f;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        hl.childForceExpandWidth = true;
        hl.childForceExpandHeight = true;
    }

    // カードプレハブの中身(背景と数字)を親に追従させる。
    // プレハブは固定サイズで作られているため、そのままでは
    // LayoutGroupがカードのサイズを変えても中身が追従しない。
    private static void StretchCardContents(Transform card)
    {
        foreach (Transform ch in card)
        {
            if (ch.name == "OwnerLabel") continue;
            var r = ch.GetComponent<RectTransform>();
            if (r == null) continue;
            // 親いっぱいに広げるが、プレハブ本来の余白比率は残す
            float mx = ch.name == "NumberText" ? 0.26f : 0.08f;
            float my = ch.name == "NumberText" ? 0.30f : 0.14f;
            r.anchorMin = new Vector2(mx, my);
            r.anchorMax = new Vector2(1f - mx, 1f - my);
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;

            // 数字はカードの大きさに追従させる
            var tmp = ch.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 1f;
                tmp.fontSizeMax = 300f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
    }

    // カードをLayoutGroupで並べるための基本設定。
    // AspectRatioFitterはRectTransformを直接書き換えるため
    // HorizontalLayoutGroupの配置計算と競合し、カードが重なる。
    // そのため縦横比はLayoutElementのpreferredサイズで表現する。
    private void SetupAutoCard(GameObject card)
    {
        card.GetComponent<RectTransform>().localScale = Vector3.one;
        StretchCardContents(card.transform);

        var arf = card.GetComponent<AspectRatioFitter>();
        if (arf != null) arf.enabled = false;

        var le = card.GetComponent<LayoutElement>();
        if (le == null) le = card.AddComponent<LayoutElement>();
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
    }

    // カードサイズの適用待ちリスト。
    // 生成直後は親の高さが未確定なため、レイアウトが確定する
    // LateUpdateまで待ってから実サイズを見てカードサイズを決める。
    private readonly List<(Transform row, int count)> _pendingCardFit = new();
    private int _pendingPlayedFit = 0;   // 「今出したカード」の適用待ち人数

    // 画面サイズを監視し、変わったらカードサイズを計算し直す。
    // WebGLはブラウザのリサイズやデバイス回転で頻繁にサイズが変わるため、
    // 一度計算して終わりにすると古いサイズのまま残ってしまう。
    private Vector2 _lastLayoutSize;

    // 画面サイズが変わったときだけカードサイズを計算し直す。
    // プレイヤー数やカード枚数はゲーム中に変わらないので、
    // カードを出すたびに計算し直す必要はない。
    // (以前は微小なレイアウト変動にも反応して再計算が走り、
    //  Confirmのたびにカードサイズがガタついていた)
    private void RequeueCardFitIfResized()
    {
        var canvasRt = GetCanvasRect();
        if (canvasRt == null) return;
        var now = new Vector2(canvasRt.rect.width, canvasRt.rect.height);
        if (Mathf.Abs(now.x - _lastLayoutSize.x) < 2f &&
            Mathf.Abs(now.y - _lastLayoutSize.y) < 2f) return;
        _lastLayoutSize = now;

        if (othersCardParent == null) return;

        // 一覧の各カード行
        foreach (Transform row in othersCardParent.transform)
        {
            if (!row.gameObject.activeSelf) continue;
            foreach (Transform blk in row)
            {
                if (!blk.gameObject.activeSelf) continue;
                var rows = blk.Find("CardRows");
                if (rows == null) continue;
                int cnt = 0;
                foreach (Transform c in rows) if (c.gameObject.activeSelf) cnt++;
                if (cnt > 0) _pendingCardFit.Add((rows, cnt));
            }
        }
        // 手札
        if (myCardParent != null && myCardParent.activeSelf)
        {
            int cnt = 0;
            foreach (Transform c in myCardParent.transform) if (c.gameObject.activeSelf) cnt++;
            if (cnt > 0) _pendingCardFit.Add((myCardParent.transform, cnt));
        }
        // 今出したカード
        if (playedCardsParent != null && playedCardsParent.activeSelf)
        {
            int cnt = 0;
            foreach (Transform c in playedCardsParent.transform) if (c.gameObject.activeSelf) cnt++;
            if (cnt > 0) _pendingPlayedFit = cnt;
        }
    }


    // ---- ショートカットキー ----
    // R: Ready/Cancel、C: Confirm/Cancel
    // 数字キー: カード選択(2桁以上にも対応)
    // ←/→: デバッグ対象プレイヤーの切り替え(ローカルのみ)
    private string _numberBuffer = "";
    private float _numberBufferUntil = 0f;
    private const float NUMBER_INPUT_WINDOW = 0.8f;   // 連続入力を1つの数とみなす時間

    private void HandleShortcuts()
    {
        // Input Systemを使う設定のため、UnityEngine.Inputは使えない。
        // Keyboard.currentはstaticフィールドへの参照なので、
        // 毎フレーム読んでもコストはほぼ無い。
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        // 入力欄にフォーカスがあるときは無効(部屋名の入力を邪魔しない)
        var sel = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        if (sel != null && sel.GetComponent<TMP_InputField>() != null) return;

        if (kb.rKey.wasPressedThisFrame) ButtonReady();
        if (kb.cKey.wasPressedThisFrame) ButtonConfirmCard();
        if (kb.hKey.wasPressedThisFrame) ButtonToggleHistory();

        // Spaceは状況に応じて使い分ける。
        // 実際に押せるボタンが出ているときだけ反応させる。
        if (kb.spaceKey.wasPressedThisFrame)
        {
            if (nextRoundButtonGO != null && nextRoundButtonGO.activeInHierarchy)
                ButtonNextRound();
            else if (confirmButtonGO != null && confirmButtonGO.activeInHierarchy)
                ButtonConfirmCard();
            else
                ButtonStartGame();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (kb.leftArrowKey.wasPressedThisFrame) OnClickDebugPrevPlayer();
        if (kb.rightArrowKey.wasPressedThisFrame) OnClickDebugNextPlayer();
#endif

        // 数字キー: 桁を溜めてカードを選ぶ
        if (Time.time > _numberBufferUntil) _numberBuffer = "";

        var digitKeys = new[]
        {
            kb.digit0Key, kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key,
            kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key
        };
        var numpadKeys = new[]
        {
            kb.numpad0Key, kb.numpad1Key, kb.numpad2Key, kb.numpad3Key, kb.numpad4Key,
            kb.numpad5Key, kb.numpad6Key, kb.numpad7Key, kb.numpad8Key, kb.numpad9Key
        };

        for (int d = 0; d <= 9; d++)
        {
            if (!digitKeys[d].wasPressedThisFrame && !numpadKeys[d].wasPressedThisFrame) continue;

            _numberBuffer += d.ToString();
            _numberBufferUntil = Time.time + NUMBER_INPUT_WINDOW;

            if (int.TryParse(_numberBuffer, out int num) && num >= 1)
            {
                var pl = GetDebugOrLocalPlayer();
                int max = pl != null ? pl.used.Count : 0;
                if (num <= max) SelectCard(num - 1);
                // これ以上桁を足しても範囲外になるなら、バッファを流す
                if (num * 10 > max) _numberBuffer = "";
            }
            break;
        }
    }


    private void LateUpdate()
    {
        // ラウンド結果ポップアップの更新/自動クローズ
        UpdateResultPopup();

        // 画面サイズが変わっていたらカードサイズを再計算する
        RequeueCardFitIfResized();

        ApplyPendingCardFits();
    }

    // 予約されているカードサイズの計算を、今の領域の大きさで適用する。
    // 大きさがまだ確定していない項目は予約を残し、次のフレームで再挑戦する。
    private void ApplyPendingCardFits()
    {
        // 「今出したカード」: 帯の実サイズからカードサイズを決める
        if (_pendingPlayedFit > 0 && playedCardsParent != null)
        {
            var pRt = playedCardsParent.GetComponent<RectTransform>();
            float bandH = pRt.rect.height;
            float bandW = pRt.rect.width;
            if (bandH >= 40f && bandW >= 40f)
            {
                int n = _pendingPlayedFit;
                float ch = bandH * 0.76f;          // ラベル(0.24)との比率
                float cw = ch * (CARD_W / CARD_H);
                float maxW = (bandW - 24f * (n - 1)) / n;
                if (cw > maxW) { cw = maxW; ch = cw * (CARD_H / CARD_W); }

                // 確定していなければ予約を残し、次のフレームで再挑戦する
                if (cw < 8f || ch < 8f) return;

                for (int i = 0; i < n && i < playedCardsParent.transform.childCount; i++)
                {
                    var slot = playedCardsParent.transform.GetChild(i);
                    // スロット自体の高さも明示する。
                    // 指定しないとラベル分しか確保されず、カードが潰れる。
                    var sle = slot.GetComponent<LayoutElement>();
                    if (sle != null)
                    {
                        sle.preferredWidth = cw;
                        sle.preferredHeight = bandH;
                        sle.minWidth = cw;
                        sle.minHeight = ch;
                    }

                    var card = slot.Find("Card");
                    var le = card != null ? card.GetComponent<LayoutElement>() : null;
                    if (le == null) continue;
                    le.preferredWidth = cw;
                    le.preferredHeight = ch;
                    le.minWidth = cw;
                    le.minHeight = ch;
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(pRt);
                _pendingPlayedFit = 0;
            }
        }

        if (_pendingCardFit.Count == 0) return;

        // 適用できたものだけをリストから外す。
        // サイズ未確定のままクリアしてしまうと、その項目は二度と
        // 処理されず、カードが極小のまま残ってしまう。
        // (エディタは初期化が速く偶然1フレーム目で確定するが、
        //  WebGLでは間に合わずこの問題が表面化していた)
        // 一覧のカードは全プレイヤーで同じサイズにする。
        // 行ごとに計算すると、行の高さのわずかな差や計算タイミングのずれで
        // サイズがバラバラになる(P0とP4で大きさが違う、という状態になっていた)。
        var ocRt = othersCardParent != null ? othersCardParent.GetComponent<RectTransform>() : null;
        if (ocRt != null && ocRt.rect.height > 40f && ocRt.rect.width > 40f)
        {
            int rowsOfPlayers = 0;
            int blocksPerRow = 1;
            int cardsPerBlock = 0;
            foreach (Transform row in othersCardParent.transform)
            {
                if (!row.gameObject.activeSelf) continue;
                rowsOfPlayers++;
                int b = 0;
                foreach (Transform blk in row)
                {
                    if (!blk.gameObject.activeSelf) continue;
                    b++;
                    var cr = blk.Find("CardRows");
                    if (cr != null)
                    {
                        int c = 0;
                        foreach (Transform card in cr) if (card.gameObject.activeSelf) c++;
                        if (c > cardsPerBlock) cardsPerBlock = c;
                    }
                }
                if (b > blocksPerRow) blocksPerRow = b;
            }

            if (rowsOfPlayers > 0 && cardsPerBlock > 0)
            {
                // 1ブロック(=1プレイヤー)に使える幅と高さを求める
                float rowSpacing = 2f;   // 行間
                float colSpacing = 24f;  // 列間
                float blockW = (ocRt.rect.width - colSpacing * (blocksPerRow - 1)) / blocksPerRow;
                float blockH = (ocRt.rect.height - rowSpacing * (rowsOfPlayers - 1)) / rowsOfPlayers;

                // ラベルとカード行の比率(CreatePlayerBlockと同じ値にする)
                float cardRatio = ResponsiveCanvasScaler.IsPortraitMode ? 0.74f : 0.80f;
                float cardAreaH = blockH * cardRatio;
                float cardSpacing = 4f;

                // 高さと幅の両方に収まるサイズを求める(全行共通)
                float ch = cardAreaH;
                float cw = ch * (CARD_W / CARD_H);
                float maxW = (blockW - cardSpacing * (cardsPerBlock - 1)) / cardsPerBlock;
                if (cw > maxW) { cw = maxW; ch = cw * (CARD_H / CARD_W); }

                if (cw >= 8f && ch >= 8f)
                {
                    foreach (Transform row in othersCardParent.transform)
                    {
                        if (!row.gameObject.activeSelf) continue;
                        foreach (Transform blk in row)
                        {
                            if (!blk.gameObject.activeSelf) continue;
                            var cr = blk.Find("CardRows");
                            if (cr == null) continue;
                            for (int i = 0; i < cr.childCount; i++)
                            {
                                var le = cr.GetChild(i).GetComponent<LayoutElement>();
                                if (le == null) continue;
                                le.preferredWidth = cw;
                                le.preferredHeight = ch;
                                le.minWidth = cw;
                                le.minHeight = ch;
                            }
                        }
                    }
                    LayoutRebuilder.ForceRebuildLayoutImmediate(ocRt);
                    // 一覧側の予約は済んだので、手札だけ残す
                    _pendingCardFit.RemoveAll(p => p.row != null && p.row.gameObject != myCardParent);
                }
            }
        }

        // 手札は独立して計算する
        for (int idx = _pendingCardFit.Count - 1; idx >= 0; idx--)
        {
            var (row, count) = _pendingCardFit[idx];
            if (row == null) { _pendingCardFit.RemoveAt(idx); continue; }
            var rowRt = row as RectTransform;
            if (rowRt == null || row.gameObject != myCardParent) { _pendingCardFit.RemoveAt(idx); continue; }

            float w = rowRt.rect.width - 20f;
            float h = rowRt.rect.height - 8f;
            if (h < 20f || w < 20f) continue;

            if (FitCardsInRow(row, count, 8f, w, h))
                _pendingCardFit.RemoveAt(idx);
        }
    }

    // ===== ゲーム開始時のレイアウト組み直し =====
    // 盤面は画面サイズに合わせて計算しているが、作った直後は親の大きさがまだ確定しておらず、
    // 何か再計算のきっかけ(Confirm等)が起きるまで表示範囲から見切れていることがあった。
    // ゲーム開始時(と途中復帰時)に、全LayoutGroupを正しい順で組み直す。
    private Coroutine _fullRebuildRoutine;

    public void RequestFullLayoutRebuild()
    {
        if (!isActiveAndEnabled) return;
        if (_fullRebuildRoutine != null) StopCoroutine(_fullRebuildRoutine);
        _fullRebuildRoutine = StartCoroutine(FullLayoutRebuildRoutine());
    }

    private System.Collections.IEnumerator FullLayoutRebuildRoutine()
    {
        // 盤面の表示切り替え(SetActive)や画面サイズの変化が
        // RectTransformに反映されるのを1フレーム待つ
        yield return null;
        ForceRebuildAllLayouts();
        _fullRebuildRoutine = null;
    }

    public void ForceRebuildAllLayouts()
    {
        var canvasRt = GetCanvasRect();
        if (canvasRt == null) return;

        // 1. 保留中のCanvas更新を先に済ませ、今の画面サイズを確定させる
        Canvas.ForceUpdateCanvases();

        // 2. 盤面の各帯(一覧/今出したカード/ボタン/手札)の高さ配分を、今の画面サイズで適用し直す
        var scaler = canvasRt.GetComponent<ResponsiveCanvasScaler>();
        if (scaler == null && othersCardParent != null)
            scaler = othersCardParent.GetComponentInParent<ResponsiveCanvasScaler>();
        if (scaler != null) scaler.ReapplyBoardSlots();

        // 3. 全LayoutGroupを親→子の順で組み直し、各領域の実際の大きさを確定させる
        RebuildLayoutGroupsParentFirst(canvasRt);

        // 4. 確定した領域の大きさから、カードの大きさを計算し直す
        //    (画面サイズが変わったものとして、全カード列を予約し直す)
        _lastLayoutSize = Vector2.zero;
        RequeueCardFitIfResized();
        ApplyPendingCardFits();

        // 5. カードの大きさが変わったので、もう一度親→子の順で組み直して並びを確定させる
        RebuildLayoutGroupsParentFirst(canvasRt);
    }

    // 全LayoutGroupを親→子の順にForceRebuildする。
    // LayoutGroupは「親が決めた自分の大きさ」の中に子を並べるので、親が先に確定している必要がある。
    // また、ForceRebuildLayoutImmediateは、Layoutを持たない要素を途中に挟んだ先の
    // LayoutGroupまでは辿らないため、1つずつ明示的に組み直す。
    private static void RebuildLayoutGroupsParentFirst(RectTransform root)
    {
        var groups = root.GetComponentsInChildren<LayoutGroup>(false);
        // 浅いものから順に(同じ深さの中では階層の並び順のまま)処理する
        var ordered = groups.OrderBy(g => HierarchyDepth(g.transform, root)).ToList();
        foreach (var g in ordered)
        {
            if (g == null || !g.isActiveAndEnabled) continue;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)g.transform);
        }
    }

    private static int HierarchyDepth(Transform t, Transform root)
    {
        int d = 0;
        while (t != null && t != root) { d++; t = t.parent; }
        return d;
    }

    // その列のカードが既に適切なサイズを持っているか
    private static bool IsCardSized(Transform row)
    {
        if (row == null || row.childCount == 0) return false;
        var rt = row.GetChild(0).GetComponent<RectTransform>();
        return rt != null && rt.rect.width > 1f && rt.rect.height > 1f;
    }

    // カード列の高さと幅を「呼び出し側から実数で」受け取り、
    // 各カードのpreferredサイズを決める。
    // rect.heightを内部で測ると、子のサイズ変更が親に跳ね返って
    // 測り直すたびに小さくなる自己参照ループに陥るため。
    // 適用できたらtrue。サイズが確定していなければfalseを返し、
    // 呼び出し側で次のフレームに再挑戦させる。
    private bool FitCardsInRow(Transform row, int count, float spacing, float rowW, float rowH)
    {
        if (rowH < 5f || rowW < 5f || count <= 0) return false;

        float cardH = rowH;
        float cardW = cardH * (CARD_W / CARD_H);
        float maxW = (rowW - spacing * (count - 1)) / count;
        if (cardW > maxW)
        {
            cardW = maxW;
            cardH = cardW * (CARD_H / CARD_W);
        }

        // 計算結果が不正(負や極小)なら適用しない。
        // 親のサイズがまだ確定していない状態で計算すると、
        // カードが消えたり1px程度に潰れたりする。
        if (cardW < 8f || cardH < 8f) return false;

        for (int i = 0; i < row.childCount; i++)
        {
            var le = row.GetChild(i).GetComponent<LayoutElement>();
            if (le == null) continue;
            le.preferredWidth = cardW;
            le.preferredHeight = cardH;
            le.minWidth = cardW;
            le.minHeight = cardH;
        }

        // LayoutElementの値を変えただけではLayoutGroupは再計算しない。
        // 明示的にリビルドを要求する。
        LayoutRebuilder.ForceRebuildLayoutImmediate(row.GetComponent<RectTransform>());
        return true;
    }



    // 1プレイヤー分のブロック(ラベル + カード行)を作る
    private void CreatePlayerBlock(Transform parent)
    {
        var block = new GameObject("PlayerBlock" + parent.childCount);
        block.transform.SetParent(parent, false);
        block.AddComponent<RectTransform>();
        var vl = block.AddComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.UpperCenter;
        vl.spacing = 1f;
        vl.childControlWidth = true;
        vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        // 高さは比率(flexibleHeight)で分け合うのでtrueにする。
        // ラベル0.22 : カード行0.78 の割合になり、
        // 画面サイズが変わっても同じ見た目の比率が保たれる。
        vl.childForceExpandHeight = true;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(block.transform, false);
        labelGo.AddComponent<RectTransform>();
        var t = labelGo.AddComponent<TextMeshProUGUI>();
        t.enableAutoSizing = true;
        // 下限を設けると枠からはみ出し、上限を設けると
        // 大画面で小さすぎる。枠の大きさに任せる。
        t.fontSizeMin = 1;
        t.fontSizeMax = 300;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        var labelLe = labelGo.AddComponent<LayoutElement>();
        // ラベルとカード行を比率で分ける。
        // 固定値やクランプを入れると、それが効いた時点で
        // 比率が崩れ、解像度によって見た目が破綻する。
        // 縦持ちは1行が高いのでラベルに余裕を持たせ、
        // 横持ちは1行が低いのでラベルを抑えてカードに回す。
        bool portraitBlk = ResponsiveCanvasScaler.IsPortraitMode;
        labelLe.flexibleHeight = portraitBlk ? 0.26f : 0.20f;
        labelLe.preferredHeight = -1f;
        labelLe.minHeight = -1f;

        // カードを横一列に並べる。高さは親が決め、幅はAspectRatioFitterが決める。
        var rows = new GameObject("CardRows");
        rows.transform.SetParent(block.transform, false);
        rows.AddComponent<RectTransform>();
        var rowHl = rows.AddComponent<HorizontalLayoutGroup>();
        rowHl.childAlignment = TextAnchor.MiddleCenter;
        rowHl.spacing = 4f;
        rowHl.childControlWidth = true;
        rowHl.childControlHeight = true;
        rowHl.childForceExpandWidth = false;
        // trueにすると、カードが行の高さいっぱいに引き伸ばされて
        // 縦長になってしまう(preferredHeightが無視される)
        rowHl.childForceExpandHeight = false;
        var rowsLe = rows.AddComponent<LayoutElement>();
        rowsLe.flexibleHeight = portraitBlk ? 0.74f : 0.80f;   // ラベルとの比率
        rowsLe.preferredHeight = -1f;
        rowsLe.minHeight = -1f;
        // minHeightは設けない。下限があると、行数が多いときに
        // 合計が枠を超えて配分が崩れる。
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
    private CardLayout CalcCardLayout(int cnt, float maxScale, float availableWidth, bool singleRow = false, int forceRows = 0)
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

        if (forceRows > 0)
        {
            // 行数を指定して、その行数で均等に割る(幅を使い切るため)
            r.rows = forceRows;
            r.perRow = Mathf.CeilToInt((float)cnt / r.rows);
        }
        else
        {
            float pitch = CARD_W * r.scale + 10f;
            r.perRow = Mathf.Max(1, Mathf.FloorToInt(availableWidth / pitch));
            if (r.perRow >= cnt)
            {
                r.perRow = cnt;
            }
            else
            {
                int need = Mathf.CeilToInt((float)cnt / r.perRow);
                r.perRow = Mathf.CeilToInt((float)cnt / need);
            }
            r.rows = Mathf.CeilToInt((float)cnt / r.perRow);
        }

        // 決まった1行あたりの枚数で、幅を使い切るまでスケールを上げる。
        // (行数を減らすためにscaleを下げたままだと、余った横幅が無駄になる)
        float multiRowFitScale = (availableWidth / Mathf.Max(r.perRow, 1) - 6f) / CARD_W;
        r.scale = Mathf.Clamp(Mathf.Min(multiRowFitScale, maxScale), 0.3f, maxScale);

        r.spacing = CARD_W * r.scale + 6f;
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

    // 「今出したカード」の親。一覧と手札の間の帯にアンカーで固定し、
    // 中身の並びと配置はUnityのHorizontalLayoutGroupに任せる。
    // (手計算で位置やサイズを決めると、他要素の変化に追従できず重なりの原因になる)
    // 「今出したカード」の親。GameBoard(縦配分コンテナ)の子として作り、
    // 高さの取り分と並びはUnityのLayoutGroupに任せる。
    private void EnsurePlayedCardsParent()
    {
        if (playedCardsParent != null) return;

        var canvasTf = othersCardParent.transform.parent;      // GameBoard または Canvas
        var board = canvasTf.name == "GameBoard" ? canvasTf : canvasTf.Find("GameBoard");
        var parent = board != null ? board : canvasTf;

        var existing = parent.Find("PlayedCardsParent");
        if (existing != null)
        {
            playedCardsParent = existing.gameObject;
        }
        else
        {
            var go = new GameObject("PlayedCardsParent");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            playedCardsParent = go;
        }

        // 使用済み一覧と手札の間に来るよう、並び順を固定する
        if (board != null)
        {
            int othersIdx = othersCardParent.transform.GetSiblingIndex();
            playedCardsParent.transform.SetSiblingIndex(othersIdx + 1);
        }

        // 横一列に均等配置。間隔と中央寄せはLayoutGroupが面倒を見る。
        var hl = playedCardsParent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (hl == null) hl = playedCardsParent.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.spacing = 24f;
        // childControlをtrueにしないと、LayoutElementのpreferredHeightが
        // 無視され、子のRectTransformの元サイズ(100)のまま潰れてしまう。
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        // カードは正方形に近い比率を保ちたいので、
        // 余った横幅はスロットではなく左右の余白に回す
        hl.padding = new RectOffset(0, 0, 0, 0);

        // 高さの取り分をここで設定する。
        // ResponsiveCanvasScaler.SetBoardSlotは「既に存在する要素」にしか
        // 適用されず、このオブジェクトは後から生成されるためスキップされていた。
        // (結果、帯の高さが最小値のまま100pxに潰れていた)
        // 高さの取り分はResponsiveCanvasScaler.SetBoardSlotAbsが
        // 全要素まとめて設定するので、ここでは最低限だけ用意しておく。
        if (playedCardsParent.GetComponent<UnityEngine.UI.LayoutElement>() == null)
            playedCardsParent.AddComponent<UnityEngine.UI.LayoutElement>();
    }



    // 各プレイヤー行の右端に「今出したカード」を表示する
    // 「今出したカード」の内容を更新する。
    // サイズと配置はLayoutGroup + AspectRatioFitterに任せる。
    private void RefreshPlayedCards(int pcnt, GameManager gm)
    {
        if (playedCardsParent == null) return;
        playedCardsParent.SetActive(othersCardParent.activeSelf);

        for (int i = playedCardsParent.transform.childCount - 1; i >= 0; i--)
        {
            var ch = playedCardsParent.transform.GetChild(i);
            if (!ch.name.StartsWith("PlayedSlot")) DestroyImmediate(ch.gameObject);
        }

        bool createdSlot = false;
        while (playedCardsParent.transform.childCount < pcnt)
        {
            CreatePlayedSlot(playedCardsParent.transform);
            createdSlot = true;
        }

        int myId = NetworkClient.connection?.identity?.GetComponent<Player>()?.playerId ?? -1;

        for (int i = 0; i < playedCardsParent.transform.childCount; i++)
        {
            var slot = playedCardsParent.transform.GetChild(i);
            slot.gameObject.SetActive(i < pcnt);
            if (i >= pcnt) continue;

            var ownerText = slot.Find("OwnerLabel").GetComponent<TextMeshProUGUI>();
            bool isMe = (i == myId);
            ownerText.text = "P" + i + (isMe ? " (You)" : "");
            ownerText.color = isMe ? new Color(1f, 0.85f, 0.25f, 1f) : new Color(0.85f, 0.88f, 0.92f, 1f);

            var num = slot.Find("Card").GetComponent<NumberCardUI>();
            // gm.roomはクライアント側では未同期のことがあるため、
            // シーン上のPlayerから直接探す方が確実。
            // (roomがnullだと提出済みでも「?」にならず「-」のままになる)
            Player pl = null;
            if (gm != null && gm.room != null && i < gm.room.playerComponents.Count)
                pl = gm.room.playerComponents[i];
            if (pl == null)
            {
                foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
                    if (p.gameManager == gm && p.playerId == i) { pl = p; break; }
            }

            int revealed;
            if (_revealedPicksOverride != null && i < _revealedPicksOverride.Length)
                revealed = _revealedPicksOverride[i];
            else
                revealed = (gm != null && i < gm.lastRevealedPicks.Count) ? gm.lastRevealedPicks[i] : 0;

            if (revealed > 0) { num.SetupDisplay(revealed.ToString()); num.SetSelected(false); }
            else if (pl != null && pl.isReadytoTurn) { num.SetupDisplay("?"); num.SetSelected(true); }
            else { num.SetupDisplay("-"); num.SetSelected(false); }
        }

        // 作ったとき、またはまだサイズが決まっていないときだけ予約する
        bool playedSized = playedCardsParent.transform.childCount > 0
            && playedCardsParent.transform.GetChild(0).Find("Card") != null
            && playedCardsParent.transform.GetChild(0).Find("Card")
                .GetComponent<RectTransform>().rect.width > 1f;
        if (createdSlot || !playedSized) _pendingPlayedFit = pcnt;
    }

    // 「今出したカード」1人分(カード + 名前)のブロック
    private void CreatePlayedSlot(Transform parent)
    {
        var slot = new GameObject("PlayedSlot" + parent.childCount);
        slot.transform.SetParent(parent, false);
        slot.AddComponent<RectTransform>();
        var vl = slot.AddComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.MiddleCenter;
        vl.spacing = 2f;
        vl.childControlWidth = true;
        vl.childControlHeight = true;
        vl.childForceExpandWidth = false;
        // 高さは比率で分け合う(カード0.76 : ラベル0.24)
        vl.childForceExpandHeight = true;

        var card = Instantiate(cardUI, slot.transform);
        card.name = "Card";
        // 表示専用なのでクリック判定を持たせない(Button自体は残す)
        var b = card.GetComponent<UnityEngine.UI.Button>();
        if (b != null) b.interactable = false;
        foreach (var g in card.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            g.raycastTarget = false;
        SetupAutoCard(card);
        // カードは比率配分の対象にしない。
        // flexibleHeightを持たせると、余った高さを吸って
        // 縦に引き伸ばされ、縦横比が崩れる。
        // サイズはUpdateResultPopupでpreferredに直接入れる。
        var cle = card.GetComponent<LayoutElement>();
        if (cle != null) { cle.flexibleHeight = 0f; cle.flexibleWidth = 0f; }

        var go = new GameObject("OwnerLabel");
        go.transform.SetParent(slot.transform, false);
        go.AddComponent<RectTransform>();
        var t = go.AddComponent<TextMeshProUGUI>();
        t.enableAutoSizing = true;
        t.fontSizeMin = 1;
        t.fontSizeMax = 300;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.flexibleHeight = 0f;

        // WIN表示は名前と別の行に、全スロット共通で常設する。
        // 空文字でも行自体は存在するので、勝者かどうかで
        // 縦のレイアウトがずれることがない。
        var winGo = new GameObject("WinLabel");
        winGo.transform.SetParent(slot.transform, false);
        winGo.AddComponent<RectTransform>();
        var wt = winGo.AddComponent<TextMeshProUGUI>();
        wt.enableAutoSizing = true;
        wt.fontSizeMin = 1;
        wt.fontSizeMax = 300;
        wt.textWrappingMode = TextWrappingModes.NoWrap;
        wt.alignment = TextAlignmentOptions.Center;
        wt.raycastTarget = false;
        wt.text = "";
        var wle = winGo.AddComponent<LayoutElement>();
        wle.flexibleHeight = 0f;
    }


    public void ShowResult(string message)
    {
        Debug.Log(message);
        if (statusText != null) statusText.text = message;
    }

    // 得点カードモード: ラウンド中、今回の得点カード(と持ち越し)をステータス欄に出しておく。
    // 何を賭けて出すカードを選ぶのかが、ゲームの肝なので常に見えるようにする。
    public void ShowPointCard(int pointCard, int carryOver)
    {
        string text = "Point card: " + GameManager.FormatPoints(pointCard);
        if (carryOver != 0)
            text += "  (carried " + GameManager.FormatPoints(carryOver)
                  + ", total " + GameManager.FormatPoints(pointCard + carryOver) + ")";
        ShowResult(text);
    }

    // 履歴1行の文字列を分解する。
    // 形式: "roundNumber|card0,card1,...|winnerId|tie|resultLabel|winner0,winner1,..."
    // 6番目(勝者一覧)が無い古い行は、最大値を出した人を勝者とみなす。
    public static bool TryParseHistoryEntry(string entry, out int roundNumber,
        out List<int> cards, out List<int> winners, out string resultLabel)
    {
        roundNumber = 0;
        cards = new List<int>();
        winners = new List<int>();
        resultLabel = "";
        if (string.IsNullOrEmpty(entry)) return false;

        var parts = entry.Split('|');
        if (parts.Length < 5) return false;

        int.TryParse(parts[0], out roundNumber);
        if (!string.IsNullOrEmpty(parts[1]))
            foreach (var t in parts[1].Split(','))
                cards.Add(int.TryParse(t, out int v) ? v : 0);
        resultLabel = parts[4];

        if (parts.Length >= 6)
        {
            if (!string.IsNullOrEmpty(parts[5]))
                foreach (var t in parts[5].Split(','))
                    if (int.TryParse(t, out int w)) winners.Add(w);
        }
        else
        {
            int best = 0;
            foreach (var v in cards) if (v > best) best = v;
            for (int i = 0; i < cards.Count; i++) if (best > 0 && cards[i] == best) winners.Add(i);
        }
        return true;
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

    // GameManagerのログから履歴パネルを丸ごと作り直す。
    public void RebuildHistoryPanel() => RebuildHistoryFromLog();

    public bool IsHistoryPanelOpen()
    {
        var p = FindHistoryPanel();
        return p != null && p.gameObject.activeSelf;
    }

    // Historyのレイアウト値は画面サイズが変わらない限り不変なので、
    // 1回計算してキャッシュし、行追加のたびに計算し直さない。
    private float _historyWidthCache = 0f;
    private Vector2 _historyLayoutFor = Vector2.zero;   // 計算時の画面サイズ
    private float _histRowHeight, _histBadgeSize, _histCardScale, _histCardSpacing;
    private float _histCardsStartX, _histResultTextWidth;

    // 画面サイズが変わっていたらレイアウト値を計算し直す。
    // 変わっていなければ前回の値をそのまま使う。
    private void EnsureHistoryLayout(int cardCountInRow)
    {
        float cw = GetCanvasWidth();
        float chh = GetCanvasHeight();
        var now = new Vector2(cw, chh);
        if ((now - _historyLayoutFor).sqrMagnitude < 1f && _histRowHeight > 0f) return;
        _historyLayoutFor = now;

        _histRowHeight = chh * 0.075f;
        _histBadgeSize = _histRowHeight * 0.62f;

        float panelWidth = _historyWidthCache > 50f
            ? _historyWidthCache
            : cw * (ResponsiveCanvasScaler.IsPortraitMode ? 0.88f : 0.56f);

        _histResultTextWidth = panelWidth * 0.22f;
        _histCardsStartX = _histBadgeSize * 1.6f + 16f;
        float cardsAreaWidth = panelWidth - _histCardsStartX - _histResultTextWidth - 24f;

        int n = Mathf.Max(1, cardCountInRow);
        float byHeight = (_histRowHeight * 0.88f) / CARD_H;
        float byWidth = (cardsAreaWidth / n) / 95f;
        _histCardScale = Mathf.Min(byHeight, byWidth);
        _histCardSpacing = cardsAreaWidth / n;
    }

    private void RebuildHistoryFromLog()
    {
        var localPlayer = GetDebugOrLocalPlayer();
        var gm = localPlayer != null ? localPlayer.gameManager : null;
        if (gm == null) return;

        // 削除前に幅を測っておく(削除後はrectが縮む)
        var panelForWidth = FindHistoryPanel();
        var vp = panelForWidth?.Find("Viewport") as RectTransform;
        if (vp != null && vp.rect.width > 50f) _historyWidthCache = vp.rect.width;

        ClearHistoryPanel();
        for (int i = 0; i < gm.roundHistoryLog.Count; i++)
        {
            if (!TryParseHistoryEntry(gm.roundHistoryLog[i], out int roundNumber,
                    out List<int> cards, out List<int> winners, out string resultLabel))
                continue;
            AppendHistoryLine(roundNumber, cards, winners, resultLabel);
        }
        // 再構築が終わったらキャッシュを解除し、
        // 次回は実際のrectから測り直せるようにする
        _historyWidthCache = 0f;
    }

    public void ButtonToggleHistory()
    {
        var panel = FindHistoryPanel();
        if (panel == null) return;
        bool willShow = !panel.gameObject.activeSelf;

        if (willShow)
        {
            // 開くたびに履歴を作り直す。
            // Callbackを取りこぼしていた場合でも、ここで最新状態になる。
            RebuildHistoryFromLog();
            // モーダルなので、開くたびに画面サイズに合わせて作り直す。
            // 500x400固定だと、縦持ちの画面に対して小さすぎた。
            var prt = panel.GetComponent<RectTransform>();
            float cw = GetCanvasWidth();
            float ch = GetCanvasHeight();
            bool portrait = ResponsiveCanvasScaler.IsPortraitMode;
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(
                Mathf.Min(portrait ? cw * 0.92f : cw * 0.6f, 1100f),
                Mathf.Min(ch * 0.7f, 1200f));

            // 背景を不透明にする(角丸スプライトはalphaを上げても縁から透ける)
            var img = panel.GetComponent<UnityEngine.UI.Image>();
            if (img != null) { var c = img.color; img.color = new Color(c.r, c.g, c.b, 1f); }

            // タイトルと閉じるボタンもパネルサイズに合わせる。
            // 40pxや30x30といった固定値のままだと、画面が大きいときに
            // 極端に小さく見えてしまう。
            float panelH = prt.sizeDelta.y;
            float titleH = Mathf.Clamp(panelH * 0.09f, 36f, 90f);
            var title = panel.Find("Title") as RectTransform;
            if (title != null)
            {
                title.sizeDelta = new Vector2(title.sizeDelta.x, titleH);
                var tt = title.GetComponent<TMP_Text>();
                if (tt != null)
                {
                    tt.enableAutoSizing = true;
                    tt.fontSizeMin = 1f;
                    tt.fontSizeMax = 300f;
                }
            }
            float closeSize = Mathf.Clamp(panelH * 0.075f, 34f, 76f);
            var close = panel.Find("BtnCloseHistory") as RectTransform;
            if (close != null)
            {
                close.sizeDelta = new Vector2(closeSize, closeSize);
                close.anchoredPosition = new Vector2(-closeSize * 0.25f, -closeSize * 0.25f);
                var ct = close.GetComponentInChildren<TMP_Text>(true);
                if (ct != null)
                {
                    ct.enableAutoSizing = true;
                    ct.fontSizeMin = 1f;
                    ct.fontSizeMax = 300f;
                }
            }
            // 一覧領域(Viewport)もタイトルの高さに合わせて下げる
            var vp = panel.Find("Viewport") as RectTransform;
            if (vp != null)
            {
                vp.anchorMin = new Vector2(0f, 0f);
                vp.anchorMax = new Vector2(1f, 1f);
                vp.offsetMin = new Vector2(12f, 12f);
                vp.offsetMax = new Vector2(-12f, -(titleH + 8f));
            }

            // 暗幕を敷いてから最前面へ
            SetHistoryDimActive(true, panel);
            panel.SetAsLastSibling();
        }
        else
        {
            SetHistoryDimActive(false, panel);
        }
        panel.gameObject.SetActive(willShow);
    }

    // 履歴パネル背後の暗幕。設定画面と同じ仕組み。
    private void SetHistoryDimActive(bool active, Transform panel)
    {
        var canvasTf = panel.parent;
        var dim = canvasTf.Find("HistoryDim") as RectTransform;
        if (dim == null)
        {
            var go = new GameObject("HistoryDim");
            go.transform.SetParent(canvasTf, false);
            dim = go.AddComponent<RectTransform>();
            var di = go.AddComponent<UnityEngine.UI.Image>();
            di.color = new Color(0f, 0f, 0f, 0.75f);
        }
        dim.anchorMin = Vector2.zero;
        dim.anchorMax = Vector2.one;
        dim.offsetMin = Vector2.zero;
        dim.offsetMax = Vector2.zero;
        dim.gameObject.SetActive(active);
        if (active) dim.SetAsLastSibling();
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
            // Destroyはフレーム末まで反映されないため、
            // 直後に作り直すと行が重複してしまう。
            var go = content.GetChild(i).gameObject;
            go.transform.SetParent(null, false);
            DestroyImmediate(go);
        }
    }

    // roundHistoryLog(SyncList)の変更を受けて、スクロールビューに1行追記する。
    // 表示はテキストの羅列ではなく、カードUIを使ったリッチな1行にする。
    // winners: 得点を得たプレイヤー(サーバーが決めたもの)。そのカードを強調する。
    public void AppendHistoryLine(int roundNumber, List<int> playedCards, List<int> winners, string resultLabel)
    {
        Debug.Log(roundNumber + "R: " + string.Join(",", playedCards) + " => winners [" + string.Join(",", winners) + "] (" + resultLabel + ")");
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
        // レイアウト値は画面サイズが変わったときだけ計算する
        EnsureHistoryLayout(Mathf.Max(1, playedCards.Count));
        float rowHeight = _histRowHeight;
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
        float badgeSize = _histBadgeSize;
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
        // クランプを入れると枠に合わなくなるので、枠の大きさに任せる
        badgeTmp.fontSizeMin = 1;
        badgeTmp.fontSizeMax = 300;
        badgeTmp.fontStyle = FontStyles.Bold;
        badgeTmp.textWrappingMode = TextWrappingModes.NoWrap;
        badgeTmp.overflowMode = TextOverflowModes.Truncate;
        badgeTmp.alignment = TextAlignmentOptions.Center;
        badgeTmp.color = Color.white;

        // カードのサイズと位置はEnsureHistoryLayoutで計算済み。
        // 行を追加するたびに測り直すと、削除直後のrectなど
        // 不安定な値を拾ってカードの大きさがばらついていた。
        float resultTextWidth = _histResultTextWidth;
        float cardScale = _histCardScale;
        float cardSpacing = 90f * cardScale + 4f;
        float cardsStartX = _histCardsStartX;
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
            // 得点を得た全員を強調する(同点で複数人が得点した場合も全員)。
            // 得点カードモードでは最小値の人や2番目に大きい人が取ることもあるので、
            // 「最大値かどうか」ではなくサーバーが決めた勝者で判断する。
            numUI.SetSelected(winners != null && winners.Contains(i));
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
        resultTmp.fontSizeMin = 1;
        resultTmp.fontSizeMax = 300;
        resultTmp.textWrappingMode = TextWrappingModes.NoWrap;
        resultTmp.overflowMode = TextOverflowModes.Truncate;
        resultTmp.alignment = TextAlignmentOptions.MidlineRight;
        // 同点でも得点は入るので、常に強調色にする。
        // (以前はtieだとグレーになり、変動ptが目立たなかった)
        // マイナスの得点を取った行は赤くして、損をしたことが分かるようにする
        resultTmp.color = string.IsNullOrEmpty(resultLabel)
            ? new Color(0.8f, 0.8f, 0.8f, 1f)
            : resultLabel.StartsWith("-")
                ? new Color(1f, 0.45f, 0.45f, 1f)
                : new Color(1f, 0.85f, 0.25f, 1f);

        // 追加のたびに一番下(最新)まで自動スクロールする
        var scrollRect = panel.GetComponent<UnityEngine.UI.ScrollRect>();
        if (scrollRect != null) Canvas.ForceUpdateCanvases();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }

    // 部屋を離脱してタイトル画面に戻る
    // 退出後に部屋選択画面へ戻す。
    // inRoomの同期だけでは、ゲーム中に抜けた場合に
    // 盤面が残ったままになることがあるため明示的に切り替える。
    public void ShowRoomSelectAfterLeave()
    {
        HideRoundResultPopup();
        var canvas = GameObject.Find("Canvas");
        if (canvas == null) return;

        foreach (var name in new[] { "GameBoard", "LobbyPanel", "HistoryPanel", "SettingsPanel" })
        {
            var t = canvas.transform.Find(name);
            if (t != null) t.gameObject.SetActive(false);
        }
        foreach (var name in new[] { "RoomId", "RoomPassword", "RoomCreate", "RoomJoin" })
        {
            var t = canvas.transform.Find(name);
            if (t != null) t.gameObject.SetActive(true);
        }
        ClearDisplayOverrides();
    }

    // 「本当に抜けますか?」の確認ダイアログ。
    // 抜けている間のラウンドはランダムにカードが出されるため、誤操作を防ぐワンクッションを入れる。
    // (同じ部屋にJoinし直せば、自分の席に戻って続きから遊べる)
    private GameObject _leaveConfirm;

    private void ShowLeaveConfirm()
    {
        var canvasTf = GameObject.Find("Canvas")?.transform;
        if (canvasTf == null) return;

        if (_leaveConfirm == null)
        {
            _leaveConfirm = new GameObject("LeaveConfirm");
            _leaveConfirm.transform.SetParent(canvasTf, false);
            var rt = _leaveConfirm.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var dim = _leaveConfirm.AddComponent<UnityEngine.UI.Image>();
            dim.color = new Color(0f, 0f, 0f, 0.8f);

            // 中身(メッセージ + ボタン2つ)を縦に並べる
            var body = new GameObject("Body");
            body.transform.SetParent(_leaveConfirm.transform, false);
            var brt = body.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.15f, 0.35f);
            brt.anchorMax = new Vector2(0.85f, 0.65f);
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            var bvl = body.AddComponent<VerticalLayoutGroup>();
            bvl.childAlignment = TextAnchor.MiddleCenter;
            bvl.spacing = 16f;
            bvl.childControlWidth = true; bvl.childControlHeight = true;
            bvl.childForceExpandWidth = true; bvl.childForceExpandHeight = true;

            var msgContainer = new GameObject("MessageContainer");
            msgContainer.transform.SetParent(body.transform, false);
            var msgLE = msgContainer.AddComponent<LayoutElement>();
            msgLE.flexibleHeight = 85f;

            var msgGo = new GameObject("Message");
            msgGo.transform.SetParent(msgContainer.transform, false);
            var msgRt = msgGo.AddComponent<RectTransform>();
            msgRt.anchorMin = Vector2.zero;
            msgRt.anchorMax = Vector2.one;
            msgRt.offsetMin = Vector2.zero;
            msgRt.offsetMax = Vector2.zero;
            var t = msgGo.AddComponent<TextMeshProUGUI>();
            t.text = "Leave this game?\nWhile you're away, a random card is played for you each round.\nJoin this room again to return to your seat.";
            t.enableAutoSizing = true;
            t.fontSizeMin = 1; t.fontSizeMax = 300;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;

            // ボタン2つを横に並べる
            var row = new GameObject("Buttons");
            row.transform.SetParent(body.transform, false);
            row.AddComponent<RectTransform>();
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.spacing = 24f;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
            var rowLE = row.AddComponent<LayoutElement>();
            rowLE.flexibleHeight = 15f;
            rowLE.preferredHeight = -1;

            CreateConfirmButton(row.transform, "Leave", new Color(0.78f, 0.35f, 0.35f, 1f),
                () => { _leaveConfirm.SetActive(false); DoLeaveRoom(); });
            CreateConfirmButton(row.transform, "Stay", new Color(0.45f, 0.5f, 0.58f, 1f),
                () => { _leaveConfirm.SetActive(false); });

            LayoutRebuilder.ForceRebuildLayoutImmediate(body.GetComponent<RectTransform>());
            t.ForceMeshUpdate();
        }

        _leaveConfirm.SetActive(true);
        _leaveConfirm.transform.SetAsLastSibling();
    }

    private void CreateConfirmButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Btn" + label);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<UnityEngine.UI.Image>();
        if (roundedButtonSprite != null)
        {
            img.sprite = roundedButtonSprite;
            img.type = UnityEngine.UI.Image.Type.Sliced;
        }
        img.color = color;
        var btn = go.AddComponent<UnityEngine.UI.Button>();
        btn.onClick.AddListener(onClick);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8, 6); trt.offsetMax = new Vector2(-8, -6);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 1; tmp.fontSizeMax = 300;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
    }

    public void ButtonLeaveRoom()
    {
        var localPlayer = NetworkClient.connection?.identity?.GetComponent<Player>();
        if (localPlayer == null) return;

        // ゲーム中は誤操作で抜けないよう確認する。
        // (抜けている間はランダムに提出される。同じ部屋にJoinし直せば席に戻れる)
        var gmForCheck = localPlayer.gameManager;
        if (gmForCheck != null && gmForCheck.inProgress)
        {
            ShowLeaveConfirm();
            return;
        }

        DoLeaveRoom();
    }

    // 実際に部屋を抜ける
    private void DoLeaveRoom()
    {
        var localPlayer = NetworkClient.connection?.identity?.GetComponent<Player>();
        if (localPlayer == null) return;

        string rid = localPlayer.myRoomId;
        Debug.Log("DoLeaveRoom: " + rid);
        if(rid == null) return; // rid == ""の可能性もあるため、NullorEmptyはダメ

        HideRoundResultPopup();
        // senderは[Command]がMirror側で自動補完する。
        // connectionToClientを渡すと、クライアントではnullになり
        // サーバー側で退出処理が行われなかった。
        roomManager.CmdLeaveRoom(rid);
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
    // ラウンド結果のポップアップ。
    // 「今出したカード」と同じ内容を、画面中央に大きく別表示する。
    // 元のUIには手を加えないので、閉じたあとの見た目が変わらない。
    private GameObject _resultPopup;
    private float _resultPopupUntil = 0f;

    // winners: 得点を得たプレイヤー。winnerLabel: そのカードの下に出す文字("WIN"や"+7pt")
    public void ShowRoundResultPopup(int[] picks, int[] winners, string message, string winnerLabel, float seconds)
    {
        EnsureResultPopup();
        _resultPopupUntil = Time.time + seconds;

        var body = _resultPopup.transform.Find("Body");
        var title = body.Find("Title").GetComponent<TextMeshProUGUI>();
        title.text = message;

        var rowTf = body.Find("Cards");
        int n = picks != null ? picks.Length : 0;

        while (rowTf.childCount < n) CreateResultSlot(rowTf);
        for (int i = 0; i < rowTf.childCount; i++)
        {
            var slot = rowTf.GetChild(i);
            slot.gameObject.SetActive(i < n);
            if (i >= n) continue;

            // 勝者はサーバーが決めたものをそのまま使う(空なら勝者なし)。
            bool isWinner = winners != null && System.Array.IndexOf(winners, i) >= 0;

            var num = slot.Find("Card").GetComponent<NumberCardUI>();
            num.SetupDisplay(picks[i] > 0 ? picks[i].ToString() : "-");
            num.SetSelected(isWinner);
            var lbl = slot.Find("OwnerLabel").GetComponent<TextMeshProUGUI>();
            lbl.text = "P" + i;
            lbl.color = isWinner ? new Color(1f, 0.85f, 0.25f, 1f) : new Color(0.85f, 0.88f, 0.92f, 1f);

            var winLbl = slot.Find("WinLabel")?.GetComponent<TextMeshProUGUI>();
            if (winLbl != null)
            {
                winLbl.text = isWinner ? winnerLabel : "";
                bool negative = !string.IsNullOrEmpty(winnerLabel) && winnerLabel.StartsWith("-");
                winLbl.color = negative ? new Color(1f, 0.45f, 0.45f, 1f) : new Color(1f, 0.85f, 0.25f, 1f);
            }
        }

        _resultPopup.SetActive(true);
        // 描画順をNext/Confirmボタンの直前にする。
        // 最前面に出すとボタンを覆ってしまうため。
        // ポップアップは最前面に出すが、そのあとで
        // Next/Confirmボタンをさらに前へ持ち上げる。
        // これで「ポップアップはボタンより下のレイヤー」になり、
        // 画面を覆いつつボタンは押せる状態が保たれる。
        _resultPopup.transform.SetAsLastSibling();
        RaiseActionButtonsAboveOverlay();

        _pendingResultPopupFit = n;
    }

    private int _pendingResultPopupFit = 0;

    private void EnsureResultPopup()
    {
        if (_resultPopup != null) return;
        var canvasTf = othersCardParent.transform.parent.parent; // GameBoard の親 = Canvas

        _resultPopup = new GameObject("RoundResultPopup");
        _resultPopup.transform.SetParent(canvasTf, false);
        // 画面の上寄りに出す。下部(確定/次へボタンと手札)は覆わない。
        // 全画面にするとNextが押せなくなるため。
        var rt = _resultPopup.AddComponent<RectTransform>();
        // 画面全体を覆う。
        // ただし描画順(レイヤー)はNextボタンより下にするので、
        // ボタンはポップアップの上に表示され、押せる状態が保たれる。
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var dim = _resultPopup.AddComponent<UnityEngine.UI.Image>();
        dim.color = new Color(0.05f, 0.07f, 0.10f, 0.96f);
        dim.raycastTarget = false;   // 下のボタン操作を妨げない

        // 中身(タイトル + カード列)を縦に並べる
        var body = new GameObject("Body");
        body.transform.SetParent(_resultPopup.transform, false);
        var brt = body.AddComponent<RectTransform>();
        // 内容を上寄りに配置する。
        // 下いっぱいまで使うとNextボタンと近くなりすぎるため。
        brt.anchorMin = new Vector2(0.04f, 0.30f);
        brt.anchorMax = new Vector2(0.96f, 0.96f);
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;
        var bvl = body.AddComponent<VerticalLayoutGroup>();
        bvl.childAlignment = TextAnchor.MiddleCenter;
        bvl.spacing = 20f;
        bvl.childControlWidth = true;
        bvl.childControlHeight = true;
        bvl.childForceExpandWidth = true;
        bvl.childForceExpandHeight = false;

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(body.transform, false);
        titleGo.AddComponent<RectTransform>();
        var t = titleGo.AddComponent<TextMeshProUGUI>();
        t.enableAutoSizing = true;
        t.fontSizeMin = 1;
        t.fontSizeMax = 300;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        var tle = titleGo.AddComponent<LayoutElement>();
        tle.flexibleHeight = 0.22f;   // カード側との比率で分ける

        var cards = new GameObject("Cards");
        cards.transform.SetParent(body.transform, false);
        cards.AddComponent<RectTransform>();
        var chl = cards.AddComponent<HorizontalLayoutGroup>();
        chl.childAlignment = TextAnchor.MiddleCenter;
        chl.spacing = 28f;
        chl.childControlWidth = true;
        chl.childControlHeight = true;
        chl.childForceExpandWidth = false;
        chl.childForceExpandHeight = false;
        var cle = cards.AddComponent<LayoutElement>();
        cle.flexibleHeight = 1f;
        cle.minHeight = 80f;
    }

    // 公開されたカードの最大値(同点判定に使う)
    // ポップアップ用の1枚分(カード + 名前)
    private void CreateResultSlot(Transform parent)
    {
        var slot = new GameObject("ResultSlot" + parent.childCount);
        slot.transform.SetParent(parent, false);
        slot.AddComponent<RectTransform>();
        var vl = slot.AddComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.UpperCenter;
        vl.spacing = 2f;
        vl.childControlWidth = true;
        vl.childControlHeight = true;
        vl.childForceExpandWidth = false;
        // preferredHeightで明示指定するのでfalse。
        // trueだとカードが余った高さを吸って縦横比が崩れる。
        vl.childForceExpandHeight = false;
        slot.AddComponent<LayoutElement>();

        var card = Instantiate(cardUI, slot.transform);
        card.name = "Card";
        // 表示専用なのでクリック判定を持たせない(Button自体は残す)
        var b = card.GetComponent<UnityEngine.UI.Button>();
        if (b != null) b.interactable = false;
        foreach (var g in card.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            g.raycastTarget = false;
        SetupAutoCard(card);
        // カードは比率配分の対象にしない。
        // flexibleHeightを持たせると、余った高さを吸って
        // 縦に引き伸ばされ、縦横比が崩れる。
        // サイズはUpdateResultPopupでpreferredに直接入れる。
        var cle = card.GetComponent<LayoutElement>();
        if (cle != null) { cle.flexibleHeight = 0f; cle.flexibleWidth = 0f; }

        var go = new GameObject("OwnerLabel");
        go.transform.SetParent(slot.transform, false);
        go.AddComponent<RectTransform>();
        var t = go.AddComponent<TextMeshProUGUI>();
        t.enableAutoSizing = true;
        t.fontSizeMin = 1;
        t.fontSizeMax = 300;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.flexibleHeight = 0f;

        // WIN表示は名前と別の行に、全スロット共通で常設する。
        // 空文字でも行自体は存在するので、勝者かどうかで
        // 縦のレイアウトがずれることがない。
        var winGo = new GameObject("WinLabel");
        winGo.transform.SetParent(slot.transform, false);
        winGo.AddComponent<RectTransform>();
        var wt = winGo.AddComponent<TextMeshProUGUI>();
        wt.enableAutoSizing = true;
        wt.fontSizeMin = 1;
        wt.fontSizeMax = 300;
        wt.textWrappingMode = TextWrappingModes.NoWrap;
        wt.alignment = TextAlignmentOptions.Center;
        wt.raycastTarget = false;
        wt.text = "";
        var wle = winGo.AddComponent<LayoutElement>();
        wle.flexibleHeight = 0f;
    }

    // 確定/次へボタンを、オーバーレイより前面に表示する。
    // ボタンはGameBoardの子(レイアウトに参加)なので親は変えず、
    // Canvasコンポーネントで描画順だけを上書きする。
    private void RaiseActionButtonsAboveOverlay()
    {
        var boardTf = othersCardParent != null ? othersCardParent.transform.parent : null;
        var slot = boardTf != null ? boardTf.Find("ActionButtonSlot") : null;
        if (slot == null) return;

        var cv = slot.GetComponent<Canvas>();
        if (cv == null)
        {
            cv = slot.gameObject.AddComponent<Canvas>();
            slot.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }
        cv.overrideSorting = true;
        cv.sortingOrder = 10;   // オーバーレイ(0)より前
    }

    public void HideRoundResultPopup()
    {
        if (_resultPopup != null && _resultPopup.activeSelf)
            _resultPopup.SetActive(false);
    }

    private void UpdateResultPopup()
    {
        // 表示中のサイズ決定(レイアウト確定後に行う)
        if (_pendingResultPopupFit > 0 && _resultPopup != null && _resultPopup.activeSelf)
        {
            var cardsRt = _resultPopup.transform.Find("Body/Cards") as RectTransform;
            if (cardsRt != null && cardsRt.rect.height > 40f && cardsRt.rect.width > 40f)
            {
                int n = _pendingResultPopupFit;
                float areaH = cardsRt.rect.height;
                float areaW = cardsRt.rect.width;

                // スロットの内訳: カード / 名前 / WIN。
                // ラベル2行分を先に確保し、残りをカードに割り当てる。
                float labelH = areaH * 0.16f;   // 1行あたり
                float ch = areaH - labelH * 2f - 8f;
                float cw = ch * (CARD_W / CARD_H);
                float maxW = (areaW - 28f * (n - 1)) / n;
                if (cw > maxW) { cw = maxW; ch = cw * (CARD_H / CARD_W); }
                if (cw < 8f || ch < 8f) return;

                for (int i = 0; i < n && i < cardsRt.childCount; i++)
                {
                    var slot = cardsRt.GetChild(i);
                    // スロット全体のサイズ
                    var sle = slot.GetComponent<LayoutElement>();
                    if (sle != null)
                    {
                        sle.preferredWidth = cw;
                        sle.preferredHeight = ch + labelH * 2f + 8f;
                    }
                    // カード(縦横比を保つ)
                    var cardLe = slot.Find("Card")?.GetComponent<LayoutElement>();
                    if (cardLe != null)
                    {
                        cardLe.preferredWidth = cw;
                        cardLe.preferredHeight = ch;
                        cardLe.minWidth = cw;
                        cardLe.minHeight = ch;
                    }
                    // ラベル2行
                    foreach (var nm in new[] { "OwnerLabel", "WinLabel" })
                    {
                        var lle = slot.Find(nm)?.GetComponent<LayoutElement>();
                        if (lle != null)
                        {
                            lle.preferredWidth = cw;
                            lle.preferredHeight = labelH;
                            lle.minHeight = labelH;
                        }
                    }
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(cardsRt);
                _pendingResultPopupFit = 0;
            }
        }

        // 表示時間が過ぎたら閉じる
        if (_resultPopup != null && _resultPopup.activeSelf && Time.time >= _resultPopupUntil)
            _resultPopup.SetActive(false);
    }

    // 盤面(手札・使用済み一覧・今出したカード)をまとめて更新する。
    // 提出状態が変わったときに呼ぶことで、自分の操作が即座に反映される。
    public void RefreshBoardViews()
    {
        var localPlayer = GetDebugOrLocalPlayer();
        var gm = localPlayer != null ? localPlayer.gameManager : null;
        if (gm == null) return;

        if (localPlayer != null)
            RefreshMyCardView(localPlayer.used.ToList(), localPlayer.used.Count);
        RefreshAllCardView(gm.used_Players.ToList(), gm.CARDCOUNT);
        // キャンセル後などに確定ボタンの表示を合わせる
        RefreshConfirmButton();
    }

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

        // 空席(null)は操作対象にできないので除外する
        var list = localPlayer.room.playerComponents.Where(p => p != null).ToList();
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
