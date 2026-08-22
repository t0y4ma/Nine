using UnityEngine;
using UnityEngine.UI;

// 機種(スマホ/タブレット/デスクトップ)を推測して基準解像度を切り替える方式は、
// PCではウィンドウサイズが任意に変わるため適切ではない。
// 代わりに、CanvasScalerのreferenceResolutionを"常に実際の画面解像度そのもの"に
// 合わせることで、scaleFactorが常に1になる。これにより、機種を一切判定することなく、
// あらゆる解像度で一貫した(物理ピクセル基準の)UIサイズになる。
//
// 各UI要素のサイズ・間隔は、固定の絶対値ではなく、Canvas幅/高さ(この方式では画面の
// 実ピクセル数と一致する)に対する割合で計算することで、どんな解像度・ウィンドウサイズでも
// 連続的に正しいサイズになるようにする。
[RequireComponent(typeof(CanvasScaler))]
public class ResponsiveCanvasScaler : MonoBehaviour
{
    private CanvasScaler scaler;

    private float _lastWidth = -1f;
    private float _lastHeight = -1f;

    public static bool IsPortraitMode { get; private set; }
    // 縦方向の予算が窮屈な画面で、カード等のサイズをどれだけ圧縮したかをUIEventsManager側と共有する。
    // これが無いと、レイアウト側は圧縮した前提で余白を計算しているのに、実際のカード描画側は
    // 圧縮を知らず元のサイズのまま描画してしまい、再び重なりが発生する。
    public static float VerticalCompressionScale { get; private set; } = 1f;

    private void Awake()
    {
        scaler = GetComponent<CanvasScaler>();
        ApplyMatch();
    }

    private void Start()
    {
        StartCoroutine(ReapplyNextFrame());
    }

    private System.Collections.IEnumerator ReapplyNextFrame()
    {
        yield return null;
        ApplyMatch();
    }

    // Screen.width/heightではなく、Canvas自身のRectTransformの実サイズを使う。
    // WebGLではScreenの値がdevicePixelRatioの影響を受けたり、ブラウザのリサイズに対して
    // 更新が1フレーム以上遅れることがあり、UI全体の座標系がずれる原因になっていた。
    // RectTransform.rectはUnityのレイアウトシステムが実際に使う値なので、常に表示と一致する。
    private Vector2 GetActualCanvasSize()
    {
        // rect.width/heightはscaleFactorで割られた値なので、実際の描画ピクセル数を得るには
        // scaleFactorを掛け戻す必要がある。これをしないと、referenceResolutionを設定→
        // scaleFactorが変化→rectが変化→…という循環に陥る。
        var rt = transform as RectTransform;
        var cv = GetComponent<Canvas>();
        if (rt != null && cv != null && rt.rect.width > 1f && rt.rect.height > 1f)
        {
            float sf = cv.scaleFactor > 0.0001f ? cv.scaleFactor : 1f;
            return new Vector2(rt.rect.width * sf, rt.rect.height * sf);
        }
        return new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
    }

    private void Update()
    {
        var size = GetActualCanvasSize();
        if (!Mathf.Approximately(size.x, _lastWidth) || !Mathf.Approximately(size.y, _lastHeight))
        {
            ApplyMatch();
        }
    }

    // Canvasのサイズ変更をUnityが検知した正確なタイミングでも再計算する。
    // (ブラウザのリサイズ時、Updateでの検知より確実)
    private void OnRectTransformDimensionsChange()
    {
        if (scaler != null) ApplyMatch();
    }

    // 外部(UIEventsManager)から、カード描画後の実測値を反映させるために呼ぶ。
    // 無限ループを避けるため、再入中は何もしない。
    private bool _reapplying;
    public void ReapplyLayout()
    {
        if (_reapplying) return;
        _reapplying = true;
        try { ApplyMatch(); }
        finally { _reapplying = false; }
    }

    private void ApplyMatch()
    {
        var canvasSize = GetActualCanvasSize();
        _lastWidth = canvasSize.x;
        _lastHeight = canvasSize.y;

        float width = Mathf.Max(1f, canvasSize.x);
        float height = Mathf.Max(1f, canvasSize.y);
        bool isPortrait = height >= width;
        IsPortraitMode = isPortrait;

        // 参照解像度を実際の画面解像度そのものにする -> scaleFactorは常に1。
        // 機種を推測する必要が一切なくなり、あらゆる解像度で連続的に破綻なく動く。
        scaler.referenceResolution = new Vector2(width, height);
        scaler.matchWidthOrHeight = 0.5f;

        ApplyOrientationLayout(isPortrait, width, height);
    }

    private void ApplyOrientationLayout(bool portrait, float canvasWidth, float canvasHeight)
    {
        var canvasTf = transform;

        // ボタンサイズは画面の実サイズに対する割合で連続的に計算する(機種の分岐は行わない)
        float btnWidth = Mathf.Clamp(canvasWidth * 0.24f, 160f, 420f);
        float btnHeight = Mathf.Clamp(canvasHeight * 0.045f, 30f, 90f);
        Vector2 btnSizePortrait = new Vector2(btnWidth, btnHeight);
        // 高さは縦持ちと共通の式にすることで、PCとスマホでボタンの「形」が大きく変わらないようにする
        Vector2 btnSizeLandscape = new Vector2(Mathf.Clamp(canvasWidth * 0.083f, 140f, 220f), btnHeight);

        float vGap = btnHeight + 8f;

if (portrait)
        {
            float clusterGap = 50f;

            // RoomListPanel(既存の部屋一覧)を一番上に配置し、その下にRoomCreate等を続ける。
            // ここに組み込んでいなかったため、ConnectPanel(Host/Connectボタン)と重なって
            // クリックできなくなっていた。
            float roomListHeight = Mathf.Clamp(canvasHeight * 0.16f, 140f, 220f);

// クラスタ全体(RoomListPanel〜BtnSettings)の合計高さを見積もり、画面の上端〜DebugPanelの
            // 間で縦方向に中央寄せする。以前はRoomListPanelだけ座標系が異なっていたため中央寄せの
            // 計算がズレてしまっていたが、座標系を統一したので正しく機能する。
            float connectPanelHeightForCalc = vGap * 4f + 20f;
            float totalClusterHeight = roomListHeight + clusterGap
                + (btnHeight + vGap * 3f) + clusterGap
                + connectPanelHeightForCalc + clusterGap
                + btnHeight + (btnHeight + 12f);

            const float topSafeMargin = 40f;
            const float bottomSafeMargin = 170f; // DebugPanelとの最低クリアランス
            float availableHeight = canvasHeight - topSafeMargin - bottomSafeMargin;
            float roomListTopOffset = topSafeMargin + Mathf.Max(0f, (availableHeight - totalClusterHeight) * 0.5f);
            // RoomListPanel以外の要素(RoomCreate等)はすべて中央アンカーのため、RoomListPanelも
            // 中央アンカーに統一する。以前は上端アンカーのままだったため、2つの異なる座標系が
            // 混在してズレが生じ、意図しない大きな空白ができる原因になっていた。
            var roomListRt = canvasTf.Find("RoomListPanel")?.GetComponent<RectTransform>();
            float roomListCenterY = canvasHeight * 0.5f - roomListTopOffset - roomListHeight * 0.5f;
            if (roomListRt != null)
            {
                roomListRt.anchorMin = new Vector2(0.5f, 0.5f);
                roomListRt.anchorMax = new Vector2(0.5f, 0.5f);
                roomListRt.pivot = new Vector2(0.5f, 0.5f);
                roomListRt.anchoredPosition = new Vector2(0, roomListCenterY);
                roomListRt.sizeDelta = new Vector2(btnWidth + 60f, roomListHeight);
            }

            float topY = roomListCenterY - roomListHeight * 0.5f - clusterGap - btnHeight * 0.5f;

            SetPos(canvasTf, "RoomCreate", new Vector2(0, topY));
            SetSize(canvasTf, "RoomCreate", btnSizePortrait);
            SetPos(canvasTf, "RoomJoin", new Vector2(0, topY - vGap));
            SetSize(canvasTf, "RoomJoin", btnSizePortrait);
            SetPos(canvasTf, "RoomId", new Vector2(0, topY - vGap * 2f));
            SetSize(canvasTf, "RoomId", btnSizePortrait);
            SetPos(canvasTf, "RoomPassword", new Vector2(0, topY - vGap * 3f));
            SetSize(canvasTf, "RoomPassword", btnSizePortrait);
            float roomClusterBottom = topY - vGap * 3f - btnHeight * 0.5f;

            float connectPanelHeight = vGap * 4f + 20f;
            float connectPanelCenterY = roomClusterBottom - clusterGap - connectPanelHeight * 0.5f;
            SetPos(canvasTf, "ConnectPanel", new Vector2(0, connectPanelCenterY));
            SetSize(canvasTf, "ConnectPanel", new Vector2(btnWidth + 60f, connectPanelHeight));
            SetPos(canvasTf, "ConnectPanel/AddressInput", new Vector2(0, vGap * 1.5f));
            SetSize(canvasTf, "ConnectPanel/AddressInput", btnSizePortrait);
            SetPos(canvasTf, "ConnectPanel/BtnHost", new Vector2(0, vGap * 0.5f));
            SetSize(canvasTf, "ConnectPanel/BtnHost", btnSizePortrait);
            SetPos(canvasTf, "ConnectPanel/BtnConnect", new Vector2(0, -vGap * 0.5f));
            SetSize(canvasTf, "ConnectPanel/BtnConnect", btnSizePortrait);
            SetPos(canvasTf, "ConnectPanel/BtnServer", new Vector2(0, -vGap * 1.5f));
            SetSize(canvasTf, "ConnectPanel/BtnServer", btnSizePortrait);

            float lobbyPanelHeight = vGap * 3f + 36f;
            SetPos(canvasTf, "LobbyPanel", new Vector2(0, connectPanelCenterY));
            SetSize(canvasTf, "LobbyPanel", new Vector2(btnWidth + 60f, lobbyPanelHeight));
            SetPos(canvasTf, "LobbyPanel/BtnReady", new Vector2(0, vGap));
            SetSize(canvasTf, "LobbyPanel/BtnReady", btnSizePortrait);
            SetPos(canvasTf, "LobbyPanel/ReadyStatusText", new Vector2(0, 0));
            SetSize(canvasTf, "LobbyPanel/ReadyStatusText", btnSizePortrait);
            SetPos(canvasTf, "LobbyPanel/PlayerCountText", new Vector2(0, -vGap));
            SetSize(canvasTf, "LobbyPanel/PlayerCountText", btnSizePortrait);

            float lobbyPanelBottom = connectPanelCenterY - lobbyPanelHeight * 0.5f;
            float startGameY = lobbyPanelBottom - clusterGap - btnHeight * 0.5f;
            SetPos(canvasTf, "StartGame", new Vector2(0, startGameY));
            SetSize(canvasTf, "StartGame", btnSizePortrait);

            // BtnSettingsはStartGameのすぐ下に配置する(排他表示ではなく同時に見えるため、間隔を空ける)
            SetPos(canvasTf, "BtnSettings", new Vector2(0, startGameY - btnHeight - 12f));
            SetSize(canvasTf, "BtnSettings", btnSizePortrait);
        }
        else
        {
            // 横持ちのロビークラスタは、以前は固定ピクセル値(200や-50など)で配置しており、
            // 4K等の非常に大きい画面ではボタン自体が上限近くまで大きくなるため固定値では
            // 足りず重なりが発生していた。実際のボタンサイズ(btnSizeLandscape)から
            // 動的に間隔を計算する方式に変更する。
            float hGap = btnSizeLandscape.x + 40f; // 横並びの中心間隔
            float vGap2 = btnSizeLandscape.y + 20f; // 縦並びの中心間隔

            // Room作成/参加クラスタを2x2グリッドで配置
            SetPos(canvasTf, "RoomCreate", new Vector2(-hGap * 0.5f, vGap2 * 0.5f));
            SetSize(canvasTf, "RoomCreate", btnSizeLandscape);
            SetPos(canvasTf, "RoomJoin", new Vector2(-hGap * 0.5f, -vGap2 * 0.5f));
            SetSize(canvasTf, "RoomJoin", btnSizeLandscape);
            SetPos(canvasTf, "RoomId", new Vector2(hGap * 0.5f, vGap2 * 0.5f));
            SetSize(canvasTf, "RoomId", btnSizeLandscape);
            SetPos(canvasTf, "RoomPassword", new Vector2(hGap * 0.5f, -vGap2 * 0.5f));
            SetSize(canvasTf, "RoomPassword", btnSizeLandscape);
            float roomClusterBottomY = -vGap2 * 0.5f - btnSizeLandscape.y * 0.5f;

            float connectClusterGap = 40f;
            float connectPanelHeight = vGap2 * 2f + btnSizeLandscape.y + 20f; // 2行分
            float connectPanelWidth = hGap * 2f + btnSizeLandscape.x + 40f; // 3列分(AddressInput/Host/Connect)
            float connectPanelCenterY = roomClusterBottomY - connectClusterGap - connectPanelHeight * 0.5f;
            SetPos(canvasTf, "ConnectPanel", new Vector2(0, connectPanelCenterY));
            SetSize(canvasTf, "ConnectPanel", new Vector2(connectPanelWidth, connectPanelHeight));
            SetPos(canvasTf, "ConnectPanel/AddressInput", new Vector2(-hGap, vGap2 * 0.5f));
            SetSize(canvasTf, "ConnectPanel/AddressInput", btnSizeLandscape);
            SetPos(canvasTf, "ConnectPanel/BtnHost", new Vector2(0, vGap2 * 0.5f));
            SetSize(canvasTf, "ConnectPanel/BtnHost", btnSizeLandscape);
            SetPos(canvasTf, "ConnectPanel/BtnConnect", new Vector2(hGap, vGap2 * 0.5f));
            SetSize(canvasTf, "ConnectPanel/BtnConnect", btnSizeLandscape);
            SetPos(canvasTf, "ConnectPanel/BtnServer", new Vector2(0, -vGap2 * 0.5f));
            SetSize(canvasTf, "ConnectPanel/BtnServer", btnSizeLandscape);

            SetPos(canvasTf, "LobbyPanel", new Vector2(0, connectPanelCenterY));
            SetSize(canvasTf, "LobbyPanel", new Vector2(connectPanelWidth, connectPanelHeight));
            SetPos(canvasTf, "LobbyPanel/BtnReady", new Vector2(-hGap, 0));
            SetSize(canvasTf, "LobbyPanel/BtnReady", btnSizeLandscape);
            SetPos(canvasTf, "LobbyPanel/ReadyStatusText", new Vector2(0, 0));
            SetSize(canvasTf, "LobbyPanel/ReadyStatusText", btnSizeLandscape);
            SetPos(canvasTf, "LobbyPanel/PlayerCountText", new Vector2(hGap, 0));
            SetSize(canvasTf, "LobbyPanel/PlayerCountText", btnSizeLandscape); // 高さ30固定だと文字が小さくなりすぎるため、他テキストと同じサイズ枠にする

            float connectPanelBottomY = connectPanelCenterY - connectPanelHeight * 0.5f;
            float startGameY = connectPanelBottomY - connectClusterGap - btnHeight * 0.5f;
            SetPos(canvasTf, "StartGame", new Vector2(-hGap * 0.5f, startGameY));
            SetSize(canvasTf, "StartGame", btnSizeLandscape);

            SetPos(canvasTf, "BtnSettings", new Vector2(hGap * 0.5f, startGameY));
            SetSize(canvasTf, "BtnSettings", btnSizeLandscape);

            // RoomListPanelは、横持ちでは横幅に余裕があるため左側に配置し、
            // RoomCreate/ConnectPanelクラスタ(画面中央)と重ならないようにする。
            // ConnectPanelの実際の幅(connectPanelWidth、4K等では広がる)を基準に位置を計算する。
            float roomListWidth = Mathf.Clamp(canvasWidth * 0.2f, 260f, 400f);
            float roomListHeight = 260f;
            var roomListRt = canvasTf.Find("RoomListPanel")?.GetComponent<RectTransform>();
            if (roomListRt != null)
            {
                roomListRt.anchorMin = new Vector2(0.5f, 0.5f);
                roomListRt.anchorMax = new Vector2(0.5f, 0.5f);
                roomListRt.pivot = new Vector2(0.5f, 0.5f);
                float roomListX = -(connectPanelWidth * 0.5f + 40f + roomListWidth * 0.5f);
                roomListRt.anchoredPosition = new Vector2(roomListX, 0);
                roomListRt.sizeDelta = new Vector2(roomListWidth, roomListHeight);
            }
        }

ApplyTopBars(canvasTf, canvasWidth, canvasHeight);
        ApplyOthersLayout(canvasTf, portrait, canvasWidth, canvasHeight);
    }

    private void ApplyTopBars(Transform canvasTf, float canvasWidth, float canvasHeight)
    {
        float margin = Mathf.Max(20f, canvasWidth * 0.04f);

        // 先に上部ボタン(Leave/History)のサイズを決める。バーはこのボタンの下に配置するため。
        float topButtonWidth = Mathf.Clamp(canvasWidth * 0.11f, 140f, 260f);
        float topButtonHeight = Mathf.Clamp(canvasHeight * 0.045f, 40f, 90f);
        const float topButtonTopMargin = 15f;
        float buttonsBottomFromTop = topButtonTopMargin + topButtonHeight;

        // タイマーバー類は、上部ボタンの下端よりさらに下に配置する。
        // 以前はバーの位置を固定(画面上端から70〜86)にしていたため、大画面でボタンの高さが
        // 拡大するとボタンとバーが重なってしまっていた。
        const float barHeight = 16f;
        const float barGap = 10f;
        float roundBarTop = buttonsBottomFromTop + barGap;
        float transitionBarTop = roundBarTop + barHeight + 6f;

        var roundBarRt = canvasTf.Find("RoundTimerBarPanel")?.GetComponent<RectTransform>();
        if (roundBarRt != null)
        {
            roundBarRt.offsetMin = new Vector2(margin, -(roundBarTop + barHeight));
            roundBarRt.offsetMax = new Vector2(-margin, -roundBarTop);
        }
        var transitionBarRt = canvasTf.Find("TransitionBarPanel")?.GetComponent<RectTransform>();
        if (transitionBarRt != null)
        {
            transitionBarRt.offsetMin = new Vector2(margin, -(transitionBarTop + barHeight));
            transitionBarRt.offsetMax = new Vector2(-margin, -transitionBarTop);
        }

        // Leave Room / History ボタンは、RoundTimerBarPanel(画面上端から70〜86ユニットの帯)より
        // さらに上にある未使用の帯(0〜70ユニット)に、左右の隅として配置する。
        // この帯はロビー画面・ゲーム画面どちらでも他要素と競合しない安全な位置。
        var leaveRt = canvasTf.Find("BtnLeaveRoom")?.GetComponent<RectTransform>();
        if (leaveRt != null)
        {
            leaveRt.anchorMin = new Vector2(0f, 1f);
            leaveRt.anchorMax = new Vector2(0f, 1f);
            leaveRt.pivot = new Vector2(0f, 1f);
            leaveRt.anchoredPosition = new Vector2(margin, -topButtonTopMargin);
            leaveRt.sizeDelta = new Vector2(topButtonWidth, topButtonHeight);
        }
        var historyBtnRt = canvasTf.Find("BtnHistory")?.GetComponent<RectTransform>();
        if (historyBtnRt != null)
        {
            historyBtnRt.anchorMin = new Vector2(1f, 1f);
            historyBtnRt.anchorMax = new Vector2(1f, 1f);
            historyBtnRt.pivot = new Vector2(1f, 1f);
            historyBtnRt.anchoredPosition = new Vector2(-margin, -topButtonTopMargin);
            historyBtnRt.sizeDelta = new Vector2(topButtonWidth, topButtonHeight);
        }
    }

private void ApplyCutIn(Transform canvasTf, float canvasWidth, float statusBottomFromTop, float availableGap)
    {
        var cutInText = canvasTf.Find("CutInPanel/CutInText")?.GetComponent<TMPro.TextMeshProUGUI>();
        if (cutInText != null)
        {
            cutInText.fontSizeMax = Mathf.Clamp(canvasWidth * 0.06f, 28f, 64f);
        }
        var cutInPanelRt = canvasTf.Find("CutInPanel")?.GetComponent<RectTransform>();
        if (cutInPanelRt != null)
        {
            // 位置が一度も設定されておらず固定のまま(中央)だったため、RoundResultPanel等と
            // 重なることがあった。StatusTextのすぐ下、OthersCardParentより上の隙間に正確に収める。
            cutInPanelRt.anchorMin = new Vector2(0.5f, 1f);
            cutInPanelRt.anchorMax = new Vector2(0.5f, 1f);
            cutInPanelRt.pivot = new Vector2(0.5f, 1f);
            const float topMargin = 10f;
            const float bottomSafety = 15f;
            float cutInHeight = Mathf.Clamp(availableGap - topMargin - bottomSafety, 40f, 200f);
            cutInPanelRt.sizeDelta = new Vector2(Mathf.Min(canvasWidth * 0.85f, 900f), cutInHeight);
            cutInPanelRt.anchoredPosition = new Vector2(0, -(statusBottomFromTop + topMargin));
        }
    }

    private void ApplyOthersLayout(Transform canvasTf, bool portrait, float canvasWidth, float canvasHeight)
    {
        // ==== 縦方向の「予算制」レイアウト ====
        // 干渉しうる全要素(StatusText / OthersCardParent / RoundResultPanel / MyCardParent /
        // Confirmボタン / DebugPanelとの余白)の「理想サイズ」をまず個別に計算し、その合計を
        // 画面の高さと比較する。画面が狭くて理想サイズの合計が収まらない場合は、全要素を
        // 同じ比率で一括圧縮することで、どんな高さでも必ず重ならずに収まることを保証する。
        // (個々の要素を場当たり的に調整すると、別の解像度で新たな重なりを生み続けてしまうため)

        // 上部ボタン(Leave/History)とタイマーバー2本の下に来るよう、下限を170に揃える
        float statusTopOffset = Mathf.Max(portrait ? 115f : Mathf.Clamp(canvasHeight * 0.12f, 90f, 160f), 170f);
        float statusHeight = Mathf.Clamp(canvasHeight * 0.028f, 44f, 80f);

        // 右端に「今出したカード」枠を置くため、その分の幅も差し引いておく(UIEventsManagerと同値)
        float othersAvailWidthEst = Mathf.Max(150f, canvasWidth * AvailableGameWidthRatio - 100f - 220f);
        float othersExpectedRows = portrait ? 4f : 2f;
        float othersHeightCap = (canvasHeight * (portrait ? 0.25f : 0.18f) / othersExpectedRows) / 160f * 110f;
        // UIEventsManager.RefreshAllCardViewと同じ折り返し計算をする(値がズレると重なるため)
        // UIEventsManagerと同じ計算(横持ちは幅を活かして行数を抑える)
        float othersDesiredSpacingEst = portrait ? 60f : 45f;
        int othersDesiredPerRowEst = Mathf.Max(1, Mathf.FloorToInt(othersAvailWidthEst / othersDesiredSpacingEst));
        int othersSubRowsEst = Mathf.Clamp(Mathf.CeilToInt(9f / othersDesiredPerRowEst), 1, portrait ? 3 : 2);
        int othersPerRowEst = Mathf.CeilToInt(9f / othersSubRowsEst);
        float othersSpacingEst = Mathf.Min(Mathf.Min(130f, othersHeightCap), othersAvailWidthEst / Mathf.Max(othersPerRowEst, 1));
        // UIEventsManager側と同じ計算にする(値がズレると行が重なるため)。
        // 縦が狭い画面では高さから逆算した上限も考慮する。
        float allowedTotalHeightEst = canvasHeight * (portrait ? 0.42f : 0.46f);
        float perPlayerAllowedEst = allowedTotalHeightEst / (portrait ? 4f : 2f);
        float scaleCapByHeightEst = Mathf.Max(0.34f, (perPlayerAllowedEst - 60f) / (140f * Mathf.Max(othersSubRowsEst, 1)));
        float othersScaleEst = Mathf.Clamp(Mathf.Max(0.5f * (othersSpacingEst / 55f), Mathf.Min(0.6f, scaleCapByHeightEst)), 0.34f, 1.8f);
        // UIEventsManager側と同じ式: (カード高さ×サブ行数) + サブ行間余白 + ラベル高さ + 余白
        float othersCardRowHEst = 140f * othersScaleEst; // カードのネイティブ高さ140基準(UIEventsManagerと一致させる)
        // ラベルはカード高さの35%を上限とする(1行表示)
        float othersLabelFontMaxEst = Mathf.Clamp(othersCardRowHEst * 0.35f, 22f, 48f);
        float othersRowHeightEst = othersCardRowHEst * othersSubRowsEst + (othersSubRowsEst - 1) * 8f + othersLabelFontMaxEst * 1.4f + 24f;
        float labelTopMarginEst = othersRowHeightEst * 0.6f;
        // labelTopMarginEstを足すとラベル高さの二重計上になる(ラベル分は既にothersRowHeightEstに
        // 含まれている)。ここは純粋にStatusTextとの間隔のみとする。
        float othersGap = 40f;
        // 実測値が利用可能ならそちらを優先する(scale下限などで見積もりとズレて手札と重なるため)
        float othersReservedHeight = Mathf.Max(
            othersRowHeightEst * othersExpectedRows * 1.3f + (portrait ? 60f : 30f),
            OthersActualTotalHeight + 20f);

        float resultGap = 30f;
        float resultPanelHeight = Mathf.Clamp(canvasHeight * (portrait ? 0.16f : 0.2f), portrait ? 160f : 110f, 260f);

        float cardMaxRowWidth = canvasWidth * 0.92f * AvailableGameWidthRatio;
        float cardWidthBasedCapEst = cardMaxRowWidth / 9f;
        float cardHeightBasedCapEst = (canvasHeight * (portrait ? 0.32f : 0.27f)) / 3.45f;
        float cardSpacingCapEst = Mathf.Clamp(Mathf.Min(cardWidthBasedCapEst, cardHeightBasedCapEst), 60f, 210f);
        float cardScaleEst = Mathf.Clamp(Mathf.Min(cardSpacingCapEst, cardMaxRowWidth / 9f) / 95f, 0.4f, 1.8f); // UIEventsManager側と基準値(95)を一致させること
        // 1行分の縦占有は 345*scale 程度。複数行に折り返されている場合はその行数分を見積もる
        // (2行目以降はカード高さ分だけ加算する)。
        float cardDownwardExtent = 345f * cardScaleEst + Mathf.Max(0, MyCardRowCount - 1) * (150f * cardScaleEst);

        float resultToCardGap = portrait ? 100f : 80f; // 結果パネル下端とMyCardParentアンカーの間の最低隙間
        // DebugPanelは本番ビルドに含まれない開発用UIなので、高さ予算には含めない。
        // (含めると狭い画面でゲーム本体が過度に圧縮されてしまう)
        // ここは単に画面下端との最低マージンとする。DebugPanelが重なる場合は
        // ApplyDebugPanelLayout側で左右に逃がして対処する。
        float bottomClearance = portrait ? 40f : 30f;

        float totalIdeal = statusTopOffset + statusHeight + othersGap + othersReservedHeight
            + resultGap + resultPanelHeight + resultToCardGap + cardDownwardExtent + bottomClearance;

        float availableHeight = canvasHeight - 20f; // 上下にわずかな余白を残す
        float compressionScale = Mathf.Min(1f, availableHeight / Mathf.Max(1f, totalIdeal));
        VerticalCompressionScale = compressionScale;

        if (compressionScale < 1f)
        {
            // 画面が狭く、理想サイズのままでは収まらない -> 全要素を同じ比率で圧縮する。
            // StatusTextの高さと結果パネルの高さだけは、可読性のため下限を設けておく。
            // statusTopOffsetは、画面上部のRoundTimerBarPanel(固定で画面上端から86ユニットの位置に
            // 配置されている)と重ならないよう、圧縮してもその下限を下回らないようにする。
            // タイマーバー類は上部ボタンの下に配置されるため、その下端(概算)を下回らないようにする。
            // 上部ボタン(最大90)+マージン15+バー2本(16*2)+間隔16 ≒ 153
            // 上部ボタン(最大90)+マージン15+バー2本(16*2)+間隔16 ≒ 153 が物理的な下限。
            // 圧縮時はここまで詰めて、その分をゲーム本体に回す。
            statusTopOffset = Mathf.Max(statusTopOffset * compressionScale, 155f);
            statusHeight = Mathf.Max(statusHeight * compressionScale, 32f);
            // 上部の隙間は圧縮時に特に切り詰める。ここが広いと一覧全体が下に押し出され、
            // 手札と重なる原因になる(WXGAで170pxもの空白が残っていた)。
            othersGap = Mathf.Max(12f, othersGap * compressionScale * 0.4f);
            othersReservedHeight *= compressionScale;
            resultGap *= compressionScale;
            resultPanelHeight = Mathf.Max(resultPanelHeight * compressionScale, 90f);
            resultToCardGap *= compressionScale;
            cardDownwardExtent *= compressionScale;
            bottomClearance *= compressionScale;
        }
        else
        {
            // 逆に画面が十分広く余白がある場合は、その余白を各隙間に均等に配分し、
            // 上部に要素が偏って下部に不自然な空白ができるのを防ぐ。
            float slack = availableHeight - totalIdeal;
            // StatusTextと使用済み一覧の間(othersGap)に大きく配分すると、そこだけ
            // 不自然な空白になる(WXGAで161pxの空白が発生していた)。
            // 上部の隙間は控えめにし、下側の隙間に多めに配分する。
            // 上部(StatusTextと一覧の間)は最小限にし、余白は下側に寄せる。
            // ここに配分しすぎると一覧が下に押し出され、手札と重なる原因になる。
            othersGap += slack * 0.05f;
            resultGap += slack * 0.25f;
            resultToCardGap += slack * 0.7f;
        }

        // ==== 圧縮/再配分後の値で、上から順に連鎖配置する ====
        float statusBottomFromTop = statusTopOffset + statusHeight;

        var statusRt = canvasTf.Find("StatusText")?.GetComponent<RectTransform>();
        if (statusRt != null)
        {
            statusRt.sizeDelta = new Vector2(portrait ? Mathf.Min(canvasWidth * 0.9f, 700f) : Mathf.Min(canvasWidth * 0.42f, 800f), statusHeight);
            statusRt.anchoredPosition = new Vector2(GameAreaCenterOffsetX, -(statusTopOffset + statusHeight * 0.5f));
        }

        string[] othersPaths = { "OthersCardParent", "OthersLabelsParent" };
        float othersY = -(statusBottomFromTop + othersGap);
        ApplyCutIn(canvasTf, canvasWidth, statusBottomFromTop, othersGap);
        foreach (var p in othersPaths)
        {
            var t = canvasTf.Find(p);
            if (t == null) continue;
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f); // 上端pivotにし、アンカー点より上にはみ出さないようにする
            rt.anchoredPosition = new Vector2(GameAreaCenterOffsetX, othersY);
        }
        float othersBottomFromTop = -othersY + othersReservedHeight;

        var resultRt = canvasTf.Find("RoundResultPanel")?.GetComponent<RectTransform>();
        float resultBottomFromBottom = 0f;
        if (resultRt != null)
        {
            resultRt.anchorMin = new Vector2(0.5f, 1f);
            resultRt.anchorMax = new Vector2(0.5f, 1f);
            resultRt.pivot = new Vector2(0.5f, 1f); // 上端pivotにし、はみ出しを防ぐ
            float resultTopOffset = othersBottomFromTop + resultGap;
            resultRt.anchoredPosition = new Vector2(GameAreaCenterOffsetX, -resultTopOffset);
            resultRt.sizeDelta = new Vector2(resultRt.sizeDelta.x, resultPanelHeight);

            resultBottomFromBottom = canvasHeight - (resultTopOffset + resultPanelHeight); // pivotが上端のため、高さ全体を引く(以前は中心pivot前提で半分しか引いておらずズレていた)
        }

        // MyCardParentのアンカーは、結果パネル下端からresultToCardGap分下。予算が正しく組まれていれば、
        // これは自動的にcardDownwardExtent+bottomClearanceの条件も満たす(個別の分岐が不要になった)。
        // 手札は下方向(cardDownwardExtent)に広がるため、アンカーがそれを下回ると画面外に
        // はみ出してしまう。画面下端マージンを確保できる最低位置を下限とする。
        // 実測値が利用可能ならそちらを優先する(見積もり式とのズレで画面外に出るのを防ぐ)
        float actualExtent = Mathf.Max(cardDownwardExtent, MyCardActualDownwardExtent);
        float myCardsY = Mathf.Max(resultBottomFromBottom - resultToCardGap, actualExtent + bottomClearance);
        var myCardRt = canvasTf.Find("MyCardParent")?.GetComponent<RectTransform>();
        if (myCardRt != null)
        {
            // pivotを一度も設定していなかったため、デフォルトの中心pivotのまま、実際のカード
            // (アンカーより下方向にのみ描画される)より上に大きくはみ出したプレースホルダー矩形が
            // 残っており、Confirmボタン等との見かけ上の重なりの原因になっていた。
            myCardRt.anchorMin = new Vector2(0.5f, 0f);
            myCardRt.anchorMax = new Vector2(0.5f, 0f);
            myCardRt.pivot = new Vector2(0.5f, 1f);
            myCardRt.anchoredPosition = new Vector2(GameAreaCenterOffsetX, myCardsY);
        }

        // Confirmボタン(結果パネルの下端とMyCardParentの間の安全な隙間)。サイズも画面に応じて拡大する。
        var confirmRt = canvasTf.Find("BtnConfirmCard")?.GetComponent<RectTransform>();
        if (confirmRt != null)
        {
            confirmRt.anchorMin = new Vector2(0.5f, 0f);
            confirmRt.anchorMax = new Vector2(0.5f, 0f);
            float confirmWidth = Mathf.Clamp(canvasWidth * 0.16f, 160f, 320f);
            float confirmHeight = Mathf.Clamp(canvasHeight * 0.045f, 40f, 90f);
            float confirmOffset = Mathf.Min(190f, resultToCardGap - 20f) * Mathf.Min(1f, compressionScale + 0.3f);
            float confirmY = Mathf.Clamp(myCardsY + confirmOffset, myCardsY + 40f, resultBottomFromBottom - confirmHeight * 0.5f - 15f); // ボタン自身の半分の高さ分も安全マージンに含める
            confirmRt.anchoredPosition = new Vector2(GameAreaCenterOffsetX, confirmY);
            confirmRt.sizeDelta = new Vector2(confirmWidth, confirmHeight);

            // BtnNextRoundはConfirmボタンと排他表示(ラウンド中/結果表示中)のため、同じ位置・サイズを使い回す
            var nextRt = canvasTf.Find("BtnNextRound")?.GetComponent<RectTransform>();
            if (nextRt != null)
            {
                nextRt.anchorMin = new Vector2(0.5f, 0f);
                nextRt.anchorMax = new Vector2(0.5f, 0f);
                nextRt.anchoredPosition = confirmRt.anchoredPosition;
                nextRt.sizeDelta = new Vector2(confirmWidth, confirmHeight);
            }
        }

        // 手札の実際の下端(アンカー位置 - 下方向への広がり)を渡し、重なる場合だけ逃がす
        ApplyDebugPanelLayout(canvasTf, portrait, canvasWidth, canvasHeight, myCardsY - cardDownwardExtent);
        ApplySettingsPanelLayout(canvasTf, canvasWidth, canvasHeight);
        ApplyHistoryPanelLayout(canvasTf, canvasWidth, canvasHeight);
        ApplyDynamicFontSizes(canvasTf, canvasWidth, canvasHeight);
    }

    // 主要テキストのフォントサイズ上限を画面サイズに連動させる。
    // 固定の上限値(例: StatusTextの36)のままだと、4K等の大画面で画面に対して
    // 極端に小さく表示され読みづらくなるため。
    private void ApplyDynamicFontSizes(Transform canvasTf, float canvasWidth, float canvasHeight)
    {
        float basis = Mathf.Min(canvasWidth, canvasHeight * 1.6f); // 極端な縦長で大きくなりすぎないよう抑制

        var statusTmp = canvasTf.Find("StatusText")?.GetComponent<TMPro.TMP_Text>();
        if (statusTmp != null)
        {
            statusTmp.fontSizeMax = Mathf.Clamp(basis * 0.028f, 36f, 90f);
            statusTmp.fontSizeMin = 18f;
        }

        // 他プレイヤーのラベル(名前・得点)
        var labelsParent = canvasTf.Find("OthersLabelsParent");
        if (labelsParent != null)
        {
            float labelMax = Mathf.Clamp(basis * 0.02f, 40f, 70f);
            for (int i = 0; i < labelsParent.childCount; i++)
            {
                var tmp = labelsParent.GetChild(i).GetComponent<TMPro.TMP_Text>();
                if (tmp != null) { tmp.fontSizeMax = labelMax; tmp.fontSizeMin = 14f; }
            }
        }

        // ラウンド結果パネル内のラベル
        var resultPanel = canvasTf.Find("RoundResultPanel");
        if (resultPanel != null)
        {
            float resultMax = Mathf.Clamp(basis * 0.018f, 32f, 60f);
            var tmps = resultPanel.GetComponentsInChildren<TMPro.TMP_Text>(true);
            foreach (var tmp in tmps)
            {
                tmp.fontSizeMax = resultMax;
                tmp.fontSizeMin = 14f;
            }
        }
    }

    // 横持ちで横幅に十分な余裕がある場合、履歴パネルを画面右側に常駐表示できる。
    // (縦持ちや幅の狭い画面では、ゲーム本体と干渉するためポップアップ形式のまま)
    public static bool IsHistoryDockedMode { get; private set; }
    // 履歴パネルが常駐している場合、ゲーム本体(結果パネル等)が使える幅は
    // その分狭くなる。UIEventsManager側がこの値を見て幅を決める。
    public static float AvailableGameWidthRatio { get; private set; } = 1f;
    // 履歴パネルが右側に常駐している場合、ゲーム領域の中心は画面中央より左にずれる。
    // カード列などを中央揃えする際にこのオフセットを加算する(常駐していない時は0)。
    public static float GameAreaCenterOffsetX { get; private set; } = 0f;
    // 自分の手札が実際に何行に折り返されているか。行数が増えるとカードの縦占有も増えるため、
    // レイアウトの高さ予算計算でこの値を使う。UIEventsManagerが設定する。
    public static int MyCardRowCount { get; set; } = 1;
    // 手札が実際にアンカーからどれだけ下方向に広がったか(実測値)。
    // 見積もり式とのズレで画面外にはみ出すのを防ぐため、UIEventsManagerが実際の値を書き込む。
    public static float MyCardActualDownwardExtent { get; set; } = 0f;
    // 使用済み一覧(全プレイヤー分)が実際に占有する高さ。見積もり式とのズレで
    // 手札と重なるのを防ぐため、UIEventsManagerが実測値を書き込む。
    public static float OthersActualTotalHeight { get; set; } = 0f;

    private void ApplyHistoryPanelLayout(Transform canvasTf, float canvasWidth, float canvasHeight)
    {
        var historyRt = canvasTf.Find("HistoryPanel")?.GetComponent<RectTransform>();

        // 常駐表示の条件: 横持ちであり、かつゲーム本体(中央の縦積み)に必要な幅を差し引いても
        // 履歴パネル用の幅が十分に残ること。
        bool portrait = canvasHeight >= canvasWidth;
        float dockedWidth = Mathf.Clamp(canvasWidth * 0.30f, 420f, 760f); // カードUIを並べるため以前より広く取る
        float gameContentWidth = canvasWidth * 0.92f; // カード列が使う想定幅
        bool canDock = !portrait && (canvasWidth - gameContentWidth) < 0f
            ? false
            : (!portrait && canvasWidth >= 1500f); // ある程度広い横持ちのみ
        IsHistoryDockedMode = canDock;
        // 常駐時は、履歴パネルの幅+余白の分だけゲーム本体が使える幅を減らす
        // 履歴パネル幅 + 左右余白 + 追加の安全マージンを差し引く。
        // 安全マージンが無いとQHD等の中間解像度でわずかに重なることがあった。
        AvailableGameWidthRatio = canDock
            ? Mathf.Clamp01((canvasWidth - dockedWidth - Mathf.Max(20f, canvasWidth * 0.02f) * 2f - canvasWidth * 0.08f) / canvasWidth)
            : 1f;
        // ゲーム領域は画面左端〜履歴パネル左端までなので、その中心は
        // 画面中央から見て「履歴パネル幅+余白」の半分だけ左になる。
        GameAreaCenterOffsetX = canDock
            ? -(dockedWidth + Mathf.Max(20f, canvasWidth * 0.02f)) * 0.5f
            : 0f;

        if (historyRt == null) return;

        if (canDock)
        {
            // 右端に縦長で常駐させる
            historyRt.anchorMin = new Vector2(1f, 0.5f);
            historyRt.anchorMax = new Vector2(1f, 0.5f);
            historyRt.pivot = new Vector2(1f, 0.5f);
            float h = Mathf.Clamp(canvasHeight * 0.6f, 300f, 900f);
            historyRt.sizeDelta = new Vector2(dockedWidth, h);
            historyRt.anchoredPosition = new Vector2(-Mathf.Max(20f, canvasWidth * 0.02f), 0f);
        }
        else
        {
            // 中央のポップアップ形式
            historyRt.anchorMin = new Vector2(0.5f, 0.5f);
            historyRt.anchorMax = new Vector2(0.5f, 0.5f);
            historyRt.pivot = new Vector2(0.5f, 0.5f);
            float w = Mathf.Clamp(canvasWidth * 0.5f, 400f, 900f);
            float h = Mathf.Clamp(canvasHeight * 0.5f, 300f, 800f);
            historyRt.sizeDelta = new Vector2(w, h);
            historyRt.anchoredPosition = Vector2.zero;
        }

        // ゲームオーバー時のテキストも、画面に対して十分大きく表示されるようにする
        var gameOverTextRt = canvasTf.Find("GameOverPanel/GameOverText")?.GetComponent<RectTransform>();
        if (gameOverTextRt != null)
        {
            gameOverTextRt.sizeDelta = new Vector2(Mathf.Min(canvasWidth * 0.85f, 1200f), Mathf.Clamp(canvasHeight * 0.25f, 200f, 500f));
        }
        var gameOverBtnRt = canvasTf.Find("GameOverPanel/BtnCloseGameOver")?.GetComponent<RectTransform>();
        if (gameOverBtnRt != null)
        {
            float btnW = Mathf.Clamp(canvasWidth * 0.16f, 180f, 360f);
            float btnH = Mathf.Clamp(canvasHeight * 0.05f, 50f, 100f);
            gameOverBtnRt.sizeDelta = new Vector2(btnW, btnH);
            gameOverBtnRt.anchoredPosition = new Vector2(0, -(Mathf.Clamp(canvasHeight * 0.13f, 120f, 320f)));
        }
    }

    // パネル内の子要素のサイズ/位置を、存在する場合だけ設定するヘルパー
    private static void SetSizeIfExists(Transform parent, string path, Vector2 size)
    {
        var rt = parent.Find(path)?.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = size;
    }

    private static void SetPosIfExists(Transform parent, string path, Vector2 pos)
    {
        var rt = parent.Find(path)?.GetComponent<RectTransform>();
        if (rt != null) rt.anchoredPosition = pos;
    }

    private void ApplySettingsPanelLayout(Transform canvasTf, float canvasWidth, float canvasHeight)
    {
        var panelRt = canvasTf.Find("SettingsPanel")?.GetComponent<RectTransform>();
        if (panelRt == null) return;

        float panelWidth = Mathf.Min(600f, canvasWidth * 0.9f);
        float panelHeight = Mathf.Min(540f, canvasHeight * 0.85f);
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);

        // 設定画面内のフォントサイズも、パネルの実サイズに連動させる。
        // 固定値(14〜28)のままだと、縦持ちの高解像度画面でパネルは大きいのに
        // 文字だけ極端に小さく、特に入力欄の文字(14)が読めない状態になっていた。
        float settingsFontBasis = Mathf.Min(panelWidth, panelHeight);
        float labelMax = Mathf.Clamp(settingsFontBasis * 0.075f, 26f, 52f);
        float titleMax = Mathf.Clamp(settingsFontBasis * 0.11f, 40f, 76f);
        float inputMax = Mathf.Clamp(settingsFontBasis * 0.07f, 24f, 46f);

        var titleTmp = panelRt.Find("Title")?.GetComponent<TMPro.TMP_Text>();
        if (titleTmp != null) { titleTmp.fontSizeMax = titleMax; titleTmp.fontSizeMin = 16f; }

        string[] labelPaths = { "CardCountLabel", "MaxPlayersLabel", "ScoringModeLabel",
            "BtnScoringFixed/Text", "BtnScoringSum/Text", "BtnApplySettings/Text", "BtnCloseSettings/Text" };
        foreach (var p in labelPaths)
        {
            var t = panelRt.Find(p)?.GetComponent<TMPro.TMP_Text>();
            if (t != null) { t.fontSizeMax = labelMax; t.fontSizeMin = 12f; }
        }

        // 各要素の枠サイズもパネルに連動させる。枠が小さいままだとautoSizeにより
        // 文字が縮小されてしまい、fontSizeMaxを上げても実際の表示は大きくならないため。
        float rowH = Mathf.Clamp(panelHeight * 0.085f, 40f, 80f);
        float labelW = panelWidth * 0.44f;
        float inputW = panelWidth * 0.26f;
        float sideX = panelWidth * 0.24f;

        SetSizeIfExists(panelRt, "Title", new Vector2(panelWidth * 0.9f, rowH * 1.2f));
        SetSizeIfExists(panelRt, "CardCountLabel", new Vector2(labelW, rowH));
        SetSizeIfExists(panelRt, "MaxPlayersLabel", new Vector2(labelW, rowH));
        SetSizeIfExists(panelRt, "ScoringModeLabel", new Vector2(panelWidth * 0.9f, rowH));
        SetSizeIfExists(panelRt, "CardCountInput", new Vector2(inputW, rowH));
        SetSizeIfExists(panelRt, "MaxPlayersInput", new Vector2(inputW, rowH));
        SetSizeIfExists(panelRt, "BtnScoringFixed", new Vector2(panelWidth * 0.44f, rowH * 1.3f));
        SetSizeIfExists(panelRt, "BtnScoringSum", new Vector2(panelWidth * 0.44f, rowH * 1.3f));
        SetSizeIfExists(panelRt, "BtnApplySettings", new Vector2(panelWidth * 0.34f, rowH * 1.1f));
        SetSizeIfExists(panelRt, "BtnCloseSettings", new Vector2(panelWidth * 0.34f, rowH * 1.1f));

        SetPosIfExists(panelRt, "CardCountLabel", new Vector2(-sideX, panelHeight * 0.23f));
        SetPosIfExists(panelRt, "CardCountInput", new Vector2(sideX, panelHeight * 0.23f));
        SetPosIfExists(panelRt, "MaxPlayersLabel", new Vector2(-sideX, panelHeight * 0.12f));
        SetPosIfExists(panelRt, "MaxPlayersInput", new Vector2(sideX, panelHeight * 0.12f));
        SetPosIfExists(panelRt, "Title", new Vector2(0, panelHeight * 0.36f));
        SetPosIfExists(panelRt, "ScoringModeLabel", new Vector2(0, panelHeight * 0.01f));
        SetPosIfExists(panelRt, "BtnScoringFixed", new Vector2(-sideX, -panelHeight * 0.11f));
        SetPosIfExists(panelRt, "BtnScoringSum", new Vector2(sideX, -panelHeight * 0.11f));
        SetPosIfExists(panelRt, "BtnApplySettings", new Vector2(-panelWidth * 0.19f, -panelHeight * 0.33f));
        SetPosIfExists(panelRt, "BtnCloseSettings", new Vector2(panelWidth * 0.19f, -panelHeight * 0.33f));

        // 入力欄(TMP_InputField)内のテキスト・プレースホルダー
        string[] inputPaths = { "CardCountInput", "MaxPlayersInput" };
        foreach (var p in inputPaths)
        {
            var inputTf = panelRt.Find(p);
            if (inputTf == null) continue;
            var texts = inputTf.GetComponentsInChildren<TMPro.TMP_Text>(true);
            foreach (var t in texts)
            {
                t.enableAutoSizing = true;
                t.fontSizeMax = inputMax;
                t.fontSizeMin = 12f;
            }
        }
    }

    private void ApplyDebugPanelLayout(Transform canvasTf, bool portrait, float canvasWidth, float canvasHeight, float myCardsBottomY)
    {
        var debugRt = canvasTf.Find("DebugPanel")?.GetComponent<RectTransform>();
        if (debugRt == null) return;

        // DebugPanelは開発用なので高さ予算に含めない。その代わり、ゲーム本体(手札)と
        // 重なってしまう場合は画面左下に寄せて小さくし、ボタンが押せる状態だけ保つ。
        const float debugPanelHeight = 120f;
        bool wouldOverlap = myCardsBottomY < (30f + debugPanelHeight);

        float panelWidth = wouldOverlap
            ? Mathf.Min(360f, canvasWidth * 0.42f)
            : Mathf.Min(660f, canvasWidth * 0.9f);
        float addBotWidth = Mathf.Clamp(panelWidth * 0.35f, 100f, 180f);
        float prevNextWidth = 80f;
        const float gap = 12f;

        float prevNextCenterX = addBotWidth * 0.5f + gap + prevNextWidth * 0.5f;
        float maxCenterX = panelWidth * 0.5f - prevNextWidth * 0.5f;
        prevNextCenterX = Mathf.Min(prevNextCenterX, maxCenterX);

        if (wouldOverlap)
        {
            // 画面左下の隅に逃がす(手札は中央寄せなので、左端なら干渉しにくい)
            debugRt.anchorMin = new Vector2(0f, 0f);
            debugRt.anchorMax = new Vector2(0f, 0f);
            debugRt.pivot = new Vector2(0f, 0f);
            debugRt.anchoredPosition = new Vector2(10f, 10f);
        }
        else
        {
            debugRt.anchorMin = new Vector2(0.5f, 0f);
            debugRt.anchorMax = new Vector2(0.5f, 0f);
            debugRt.pivot = new Vector2(0.5f, 0.5f);
            debugRt.anchoredPosition = portrait ? new Vector2(0, 80) : new Vector2(0, 90);
        }
        debugRt.sizeDelta = new Vector2(panelWidth, debugPanelHeight);

        SetPos(canvasTf, "DebugPanel/BtnDebugPrev", new Vector2(-prevNextCenterX, 0));
        SetPos(canvasTf, "DebugPanel/BtnDebugNext", new Vector2(prevNextCenterX, 0));
        SetSize(canvasTf, "DebugPanel/BtnDebugAddBot", new Vector2(addBotWidth, 40));
        SetSize(canvasTf, "DebugPanel/DebugPlayerLabel", new Vector2(panelWidth - 20f, 40));
    }

    private void SetPos(Transform root, string path, Vector2 pos)
    {
        var t = root.Find(path);
        if (t == null) return;
        var rt = t.GetComponent<RectTransform>();
        if (rt != null) rt.anchoredPosition = pos;
    }

    private void SetSize(Transform root, string path, Vector2 size)
    {
        var t = root.Find(path);
        if (t == null) return;
        var rt = t.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = size;
    }
}
