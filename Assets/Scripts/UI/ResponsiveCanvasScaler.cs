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
            float topY = 130f;

            SetPos(canvasTf, "RoomCreate", new Vector2(0, topY));
            SetSize(canvasTf, "RoomCreate", btnSizePortrait);
            SetPos(canvasTf, "RoomJoin", new Vector2(0, topY - vGap));
            SetSize(canvasTf, "RoomJoin", btnSizePortrait);
            SetPos(canvasTf, "RoomId", new Vector2(0, topY - vGap * 2f));
            SetSize(canvasTf, "RoomId", btnSizePortrait);
            float roomClusterBottom = topY - vGap * 2f - btnHeight * 0.5f;

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

            float lobbyPanelHeight = vGap * 3f;
            SetPos(canvasTf, "LobbyPanel", new Vector2(0, connectPanelCenterY));
            SetSize(canvasTf, "LobbyPanel", new Vector2(btnWidth + 60f, lobbyPanelHeight));
            SetPos(canvasTf, "LobbyPanel/BtnReady", new Vector2(0, vGap * 0.5f));
            SetSize(canvasTf, "LobbyPanel/BtnReady", btnSizePortrait);
            SetPos(canvasTf, "LobbyPanel/ReadyStatusText", new Vector2(0, -vGap * 0.5f));
            SetSize(canvasTf, "LobbyPanel/ReadyStatusText", btnSizePortrait);

            float lobbyPanelBottom = connectPanelCenterY - lobbyPanelHeight * 0.5f;
            SetPos(canvasTf, "StartGame", new Vector2(0, lobbyPanelBottom - clusterGap - btnHeight * 0.5f));
            SetSize(canvasTf, "StartGame", btnSizePortrait);
        }
        else
        {
            SetPos(canvasTf, "ConnectPanel/AddressInput", new Vector2(-260, 0));
            SetSize(canvasTf, "ConnectPanel/AddressInput", btnSizeLandscape);
            SetPos(canvasTf, "ConnectPanel/BtnHost", new Vector2(0, 0));
            SetSize(canvasTf, "ConnectPanel/BtnHost", btnSizeLandscape);
            SetPos(canvasTf, "ConnectPanel/BtnConnect", new Vector2(260, 0));
            SetSize(canvasTf, "ConnectPanel/BtnConnect", btnSizeLandscape);
            SetPos(canvasTf, "ConnectPanel/BtnServer", new Vector2(0, -60));
            SetSize(canvasTf, "ConnectPanel/BtnServer", btnSizeLandscape);
            SetSize(canvasTf, "ConnectPanel", new Vector2(760, 120));
            SetPos(canvasTf, "ConnectPanel", new Vector2(0, -140));

            SetPos(canvasTf, "RoomCreate", new Vector2(0, 0));
            SetSize(canvasTf, "RoomCreate", btnSizeLandscape);
            SetPos(canvasTf, "RoomJoin", new Vector2(0, 100));
            SetSize(canvasTf, "RoomJoin", btnSizeLandscape);
            SetPos(canvasTf, "RoomId", new Vector2(200, 0));
            SetSize(canvasTf, "RoomId", btnSizeLandscape);

            SetPos(canvasTf, "LobbyPanel", new Vector2(0, -140));
            SetSize(canvasTf, "LobbyPanel", new Vector2(560, 120));
            SetPos(canvasTf, "LobbyPanel/BtnReady", new Vector2(-150, 0));
            SetSize(canvasTf, "LobbyPanel/BtnReady", btnSizeLandscape);
            SetPos(canvasTf, "LobbyPanel/ReadyStatusText", new Vector2(150, 0));
            SetSize(canvasTf, "LobbyPanel/ReadyStatusText", btnSizeLandscape);

            SetPos(canvasTf, "StartGame", new Vector2(-200, 0));
            SetSize(canvasTf, "StartGame", btnSizeLandscape);
        }

ApplyTopBars(canvasTf, canvasWidth);
        ApplyCutIn(canvasTf, canvasWidth);
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

    private void ApplyCutIn(Transform canvasTf, float canvasWidth)
    {
        var cutInText = canvasTf.Find("CutInPanel/CutInText")?.GetComponent<TMPro.TextMeshProUGUI>();
        if (cutInText != null)
        {
            cutInText.fontSizeMax = Mathf.Clamp(canvasWidth * 0.06f, 28f, 64f);
        }
        var cutInPanelRt = canvasTf.Find("CutInPanel")?.GetComponent<RectTransform>();
        if (cutInPanelRt != null)
        {
            cutInPanelRt.sizeDelta = new Vector2(Mathf.Min(canvasWidth * 0.85f, 900f), cutInPanelRt.sizeDelta.y);
        }
    }

    private void ApplyOthersLayout(Transform canvasTf, bool portrait, float canvasWidth, float canvasHeight)
    {
        // 各要素の位置は、上から順に「前の要素の下端」を基準に連鎖的に計算する。
        // 独立した固定割合の式同士だと、画面サイズによっては互いに重なってしまうため、
        // 常に前の要素との間隔を保証できるこの方式にする。

// 1. StatusText(タイマーバーのすぐ下)
        float statusHeight = Mathf.Clamp(canvasHeight * 0.028f, 44f, 80f);
        float statusTopOffset = portrait ? 115f : Mathf.Clamp(canvasHeight * 0.12f, 90f, 160f);
        float statusBottomFromTop = statusTopOffset + statusHeight;

        var statusRt = canvasTf.Find("StatusText")?.GetComponent<RectTransform>();
        if (statusRt != null)
        {
            statusRt.sizeDelta = new Vector2(portrait ? Mathf.Min(canvasWidth * 0.9f, 700f) : Mathf.Min(canvasWidth * 0.42f, 800f), statusHeight);
            statusRt.anchoredPosition = new Vector2(0, -(statusTopOffset + statusHeight * 0.5f));
        }

        // 2. OthersCardParent / OthersLabelsParent(StatusTextのすぐ下)
        string[] othersPaths = { "OthersCardParent", "OthersLabelsParent" };
        // OthersLabelsParentのラベルは、カードとの重なりを避けるためアンカー点よりやや上にせり出す
        // (labelTopMargin分)ため、その分もStatusTextとの間隔に含めておく。
        float othersAvailWidthEstForGap = Mathf.Max(150f, canvasWidth - 100f);
        float othersSpacingEstForGap = Mathf.Min(130f, othersAvailWidthEstForGap / 9f);
        float othersRowHeightEstForGap = Mathf.Max(45f, 80f * (othersSpacingEstForGap / 55f) / 0.5f * 0.5f);
        float labelTopMarginEst = othersRowHeightEstForGap * 0.6f;
        float othersGap = 40f + labelTopMarginEst;
        float othersY = -(statusBottomFromTop + othersGap);
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
// OthersCardParent自体のカードスケールはUIEventsManager側で画面幅から動的に計算されるため、
        // ここでも同じ考え方でスケールを見積もり、実際のカードサイズに見合った余白を確保する
        // (固定値だと、カードが大きくなった時に干渉してしまうため)。
        float othersAvailWidthEst = Mathf.Max(150f, canvasWidth - 100f);
        float othersSpacingEst = Mathf.Min(130f, othersAvailWidthEst / 9f);
        float othersScaleEst = 0.5f * (othersSpacingEst / 55f);
        float othersRowHeightEst = Mathf.Max(45f, 80f * othersScaleEst / 0.5f);
        // 横持ちは幅に余裕があり行数が少なくなる傾向があるため、想定行数を縦持ちより減らす
        // (縦持ちと同じ想定のままだと、横持ちの限られた高さの中で計算が破綻してしまう)
        float othersReservedHeight = othersRowHeightEst * (portrait ? 4f : 2f) + 60f;
        float othersBottomFromTop = -othersY + othersReservedHeight;

        // 3. RoundResultPanel(OthersCardParentのさらに下)
        var resultRt = canvasTf.Find("RoundResultPanel")?.GetComponent<RectTransform>();
        float resultBottomFromBottom = 0f;
        if (resultRt != null)
        {
            resultRt.anchorMin = new Vector2(0.5f, 1f);
            resultRt.anchorMax = new Vector2(0.5f, 1f);
            float resultPanelHeight = Mathf.Clamp(canvasHeight * (portrait ? 0.16f : 0.2f), 160f, 260f);
            float resultGap = 30f;
            float resultTopOffset = othersBottomFromTop + resultGap;
            resultRt.anchoredPosition = new Vector2(0, -resultTopOffset);
            resultRt.sizeDelta = new Vector2(resultRt.sizeDelta.x, resultPanelHeight);

            resultBottomFromBottom = canvasHeight - (resultTopOffset + resultPanelHeight * 0.5f);
        }

// 4. MyCardParent(結果パネルとDebugPanelの間、余白を均等に使う)
        // カードの実際のスケール(UIEventsManager側と同じ考え方で見積もる)によって、
        // カード列がアンカー位置からどれだけ下に伸びるかが変わるため、それを踏まえた最低限の
        // クリアランスを確保する(でないと画面下端やDebugPanelの手前で見切れてしまう)。
        float cardMaxRowWidth = canvasWidth * 0.92f;
        float cardSpacingCapEst = Mathf.Clamp(cardMaxRowWidth / 9f, 60f, 210f);
        float cardScaleEst = Mathf.Clamp(Mathf.Min(cardSpacingCapEst, cardMaxRowWidth / 9f) / 120f, 0.4f, 1.8f);
        float cardDownwardExtent = 345f * cardScaleEst; // アンカーから最下段カード下端までの見積もり距離

var myCardRt = canvasTf.Find("MyCardParent")?.GetComponent<RectTransform>();
        float myCardsY;
        if (portrait)
        {
            // 縦持ちは幅が狭く行数が増えやすいため、カードスケールに応じたクリアランスを厳密に計算する連鎖式を使う
            float myCardsMinY = cardDownwardExtent + 170f; // 画面下端およびDebugPanelとの最低クリアランス
            float resultSafetyGap = 100f;
            float myCardsUpperBound = resultBottomFromBottom - resultSafetyGap;

            if (myCardsMinY <= myCardsUpperBound)
            {
                myCardsY = Mathf.Clamp(resultBottomFromBottom * 0.42f, myCardsMinY, myCardsUpperBound);
            }
            else
            {
                // 両方の制約を同時に満たせない場合は、結果パネルとの重なり回避を優先する
                myCardsY = myCardsUpperBound;
            }
        }
        else
        {
// 横持ちは幅に余裕がありカードスケール推定が過大になりやすいため、Canvas高さに基づく
            // シンプルな式を基本にしつつ、以下の2点を保証する:
            // (1) 画面下端を超えて見切れない(カード自体の下方向の広がり分のクリアランス)
            // (2) RoundResultPanelと重ならない
            float simpleLandscapeY = Mathf.Clamp(canvasHeight * 0.5f, 300f, 700f);
            float landscapeMinY = cardDownwardExtent + 40f;
            float landscapeUpperBound = resultBottomFromBottom - 150f;

            if (landscapeMinY <= landscapeUpperBound)
            {
                myCardsY = Mathf.Clamp(simpleLandscapeY, landscapeMinY, landscapeUpperBound);
            }
            else
            {
                // 両立できない場合は、画面内に収まることを優先する(結果パネルとの重なりより見切れの方が深刻なため)
                myCardsY = landscapeMinY;
            }
        }
        if (myCardRt != null) myCardRt.anchoredPosition = new Vector2(0, myCardsY);

// 5. Confirmボタン(結果パネルの下端とMyCardParentの間の安全な隙間)。サイズも画面に応じて拡大する。
        var confirmRt = canvasTf.Find("BtnConfirmCard")?.GetComponent<RectTransform>();
        if (confirmRt != null)
        {
            confirmRt.anchorMin = new Vector2(0.5f, 0f);
            confirmRt.anchorMax = new Vector2(0.5f, 0f);
            float confirmY = Mathf.Clamp(myCardsY + 190f, myCardsY + 60f, resultBottomFromBottom - 40f);
            confirmRt.anchoredPosition = new Vector2(0, confirmY);
float confirmWidth = Mathf.Clamp(canvasWidth * 0.16f, 160f, 320f);
            float confirmHeight = Mathf.Clamp(canvasHeight * 0.045f, 40f, 90f);
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
