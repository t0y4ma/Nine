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

    private int _lastWidth = -1;
    private int _lastHeight = -1;

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

    private void Update()
    {
        if (Screen.width != _lastWidth || Screen.height != _lastHeight)
        {
            ApplyMatch();
        }
    }

    private void ApplyMatch()
    {
        _lastWidth = Screen.width;
        _lastHeight = Screen.height;

        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
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
            SetSize(canvasTf, "LobbyPanel/PlayerCountText", new Vector2(btnWidth, 30f));

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
            SetSize(canvasTf, "LobbyPanel/PlayerCountText", new Vector2(btnSizeLandscape.x, 30));

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

ApplyTopBars(canvasTf, canvasWidth);
        ApplyOthersLayout(canvasTf, portrait, canvasWidth, canvasHeight);
    }

    private void ApplyTopBars(Transform canvasTf, float canvasWidth)
    {
        float margin = Mathf.Max(20f, canvasWidth * 0.04f);
        string[] barPaths = { "RoundTimerBarPanel", "TransitionBarPanel" };
        foreach (var path in barPaths)
        {
            var t = canvasTf.Find(path);
            if (t == null) continue;
            var rt = t.GetComponent<RectTransform>();
            float top = rt.offsetMax.y;
            float bottom = rt.offsetMin.y;
            rt.offsetMin = new Vector2(margin, bottom);
            rt.offsetMax = new Vector2(-margin, top);
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

        float statusTopOffset = portrait ? 115f : Mathf.Clamp(canvasHeight * 0.12f, 90f, 160f);
        float statusHeight = Mathf.Clamp(canvasHeight * 0.028f, 44f, 80f);

        float othersAvailWidthEst = Mathf.Max(150f, canvasWidth - 100f);
        float othersExpectedRows = portrait ? 4f : 2f;
        float othersHeightCap = (canvasHeight * (portrait ? 0.25f : 0.18f) / othersExpectedRows) / 160f * 110f;
        float othersSpacingEst = Mathf.Min(Mathf.Min(130f, othersHeightCap), othersAvailWidthEst / 9f);
        float othersScaleEst = 0.5f * (othersSpacingEst / 55f);
        float othersRowHeightEst = Mathf.Max(45f, 80f * othersScaleEst / 0.5f);
        float labelTopMarginEst = othersRowHeightEst * 0.6f;
        float othersGap = 40f + labelTopMarginEst;
        float othersReservedHeight = othersRowHeightEst * othersExpectedRows * 1.3f + (portrait ? 60f : 30f); // 実測との誤差を吸収する安全係数

        float resultGap = 30f;
        float resultPanelHeight = Mathf.Clamp(canvasHeight * (portrait ? 0.16f : 0.2f), portrait ? 160f : 110f, 260f);

        float cardMaxRowWidth = canvasWidth * 0.92f;
        float cardWidthBasedCapEst = cardMaxRowWidth / 9f;
        float cardHeightBasedCapEst = (canvasHeight * (portrait ? 0.32f : 0.27f)) / 3.45f;
        float cardSpacingCapEst = Mathf.Clamp(Mathf.Min(cardWidthBasedCapEst, cardHeightBasedCapEst), 60f, 210f);
        float cardScaleEst = Mathf.Clamp(Mathf.Min(cardSpacingCapEst, cardMaxRowWidth / 9f) / 120f, 0.4f, 1.8f);
        float cardDownwardExtent = 345f * cardScaleEst; // アンカーから最下段カード下端までの見積もり距離

        float resultToCardGap = portrait ? 100f : 80f; // 結果パネル下端とMyCardParentアンカーの間の最低隙間
        float bottomClearance = portrait ? 170f : 130f; // カード下端からDebugPanel等までの最低隙間

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
            statusTopOffset = Mathf.Max(statusTopOffset * compressionScale, 105f); // RoundTimerBarPanel(86) + StatusText/Bgの上方向はみ出し分(15)+余裕
            statusHeight = Mathf.Max(statusHeight * compressionScale, 32f);
            othersGap *= compressionScale;
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
            float extraPerGap = slack / 3f;
            othersGap += extraPerGap;
            resultGap += extraPerGap;
            resultToCardGap += extraPerGap;
        }

        // ==== 圧縮/再配分後の値で、上から順に連鎖配置する ====
        float statusBottomFromTop = statusTopOffset + statusHeight;

        var statusRt = canvasTf.Find("StatusText")?.GetComponent<RectTransform>();
        if (statusRt != null)
        {
            statusRt.sizeDelta = new Vector2(portrait ? Mathf.Min(canvasWidth * 0.9f, 700f) : Mathf.Min(canvasWidth * 0.42f, 800f), statusHeight);
            statusRt.anchoredPosition = new Vector2(0, -(statusTopOffset + statusHeight * 0.5f));
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
            rt.anchoredPosition = new Vector2(0, othersY);
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
            resultRt.anchoredPosition = new Vector2(0, -resultTopOffset);
            resultRt.sizeDelta = new Vector2(resultRt.sizeDelta.x, resultPanelHeight);

            resultBottomFromBottom = canvasHeight - (resultTopOffset + resultPanelHeight); // pivotが上端のため、高さ全体を引く(以前は中心pivot前提で半分しか引いておらずズレていた)
        }

        // MyCardParentのアンカーは、結果パネル下端からresultToCardGap分下。予算が正しく組まれていれば、
        // これは自動的にcardDownwardExtent+bottomClearanceの条件も満たす(個別の分岐が不要になった)。
        float myCardsY = resultBottomFromBottom - resultToCardGap;
        var myCardRt = canvasTf.Find("MyCardParent")?.GetComponent<RectTransform>();
        if (myCardRt != null)
        {
            // pivotを一度も設定していなかったため、デフォルトの中心pivotのまま、実際のカード
            // (アンカーより下方向にのみ描画される)より上に大きくはみ出したプレースホルダー矩形が
            // 残っており、Confirmボタン等との見かけ上の重なりの原因になっていた。
            myCardRt.anchorMin = new Vector2(0.5f, 0f);
            myCardRt.anchorMax = new Vector2(0.5f, 0f);
            myCardRt.pivot = new Vector2(0.5f, 1f);
            myCardRt.anchoredPosition = new Vector2(0, myCardsY);
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
            confirmRt.anchoredPosition = new Vector2(0, confirmY);
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

        ApplyDebugPanelLayout(canvasTf, portrait, canvasWidth);
        ApplySettingsPanelLayout(canvasTf, canvasWidth, canvasHeight);
    }

    private void ApplySettingsPanelLayout(Transform canvasTf, float canvasWidth, float canvasHeight)
    {
        var panelRt = canvasTf.Find("SettingsPanel")?.GetComponent<RectTransform>();
        if (panelRt == null) return;

        float panelWidth = Mathf.Min(600f, canvasWidth * 0.9f);
        float panelHeight = Mathf.Min(540f, canvasHeight * 0.85f);
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);
    }

    private void ApplyDebugPanelLayout(Transform canvasTf, bool portrait, float canvasWidth)
    {
        var debugRt = canvasTf.Find("DebugPanel")?.GetComponent<RectTransform>();
        if (debugRt == null) return;

        float panelWidth = Mathf.Min(660f, canvasWidth * 0.9f);
        float addBotWidth = Mathf.Clamp(panelWidth * 0.35f, 100f, 180f);
        float prevNextWidth = 80f;
        const float gap = 12f;

        float prevNextCenterX = addBotWidth * 0.5f + gap + prevNextWidth * 0.5f;
        float maxCenterX = panelWidth * 0.5f - prevNextWidth * 0.5f;
        prevNextCenterX = Mathf.Min(prevNextCenterX, maxCenterX);

        debugRt.anchoredPosition = portrait ? new Vector2(0, 80) : new Vector2(0, 90);
        debugRt.sizeDelta = new Vector2(panelWidth, 120);

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
