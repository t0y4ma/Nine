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

    // 設計上の基準解像度。この解像度でレイアウトが成立するように組み、
    // 他の解像度へはCanvasScalerが自動で拡大縮小する。
    // 手札が実際に何行に折り返されているか。UIEventsManagerが描画時に設定する。
    // 確定ボタンの位置を、手札の実際の高さに合わせるために使う。
    public static int MyCardRowCountForLayout { get; set; } = 1;

    private static readonly Vector2 REFERENCE_RESOLUTION_LANDSCAPE = new Vector2(1920f, 1080f);
    // 縦持ちは基準解像度自体を縦長にする。
    // 横持ち用の1920x1080のまま幅基準にすると、Canvas高さが4000超になり
    // 1080基準で組んだ要素が相対的に極端に小さくなってしまう。
    private static readonly Vector2 REFERENCE_RESOLUTION_PORTRAIT = new Vector2(1080f, 1920f);

    private void ApplyMatch()
    {
        var canvasSize = GetActualCanvasSize();
        _lastWidth = canvasSize.x;
        _lastHeight = canvasSize.y;

        float width = Mathf.Max(1f, canvasSize.x);
        float height = Mathf.Max(1f, canvasSize.y);
        bool isPortrait = height >= width;
        IsPortraitMode = isPortrait;

        // === CanvasScalerを本来の用途で使う ===
        // 以前はreferenceResolutionを実画面サイズに毎フレーム書き換えていたため
        // scaleFactorが常に1になり、CanvasScalerのスケーリングが無効化されていた。
        // その結果、本来CanvasScalerが自動でやる拡大縮小を全要素ぶん手計算する
        // 羽目になり、二重計算・循環依存・解像度ごとの個別調整の温床になっていた。
        //
        // 参照解像度は固定にして、スケーリングはCanvasScalerに任せる。
        scaler.referenceResolution = isPortrait ? REFERENCE_RESOLUTION_PORTRAIT : REFERENCE_RESOLUTION_LANDSCAPE;
        // 縦横比に応じて基準軸を切り替える。
        // 横持ち(縦が狭い)は高さ基準に寄せ、縦持ち(横が狭い)は幅基準に寄せることで、
        // どちらの向きでも「狭い方の軸」からはみ出さないようにする。
        // どちらの向きでも「基準解像度に対して縦横比がずれた分」を両軸で吸収する。
        // 0.5にすることで、極端に細長い画面でも要素が小さくなりすぎない。
        scaler.matchWidthOrHeight = 0.5f;

        // ロビー画面とゲーム画面のレイアウトを両方適用する。
        // (表示/非表示は各パネル側が制御するため、常に両方の座標を更新しておく)
        ApplyOrientationLayout(isPortrait, width, height);
        ApplyOthersLayout(isPortrait, width, height);
        ApplyDebugPanelLayout(transform, isPortrait, width, height, 0f);
        ApplySettingsPanelLayout(transform, isPortrait);
    }

    // === ロビー画面のレイアウト ===
    // 基準解像度(1920x1080)上で組む。他解像度へはCanvasScalerが自動でスケールする。
    // 以前は縦持ち/横持ちで全要素の座標を二重に手計算していた(約300行)が、
    // アンカー+縦積みの共通ルールに統一した。
    private void ApplyOrientationLayout(bool portrait, float canvasWidth, float canvasHeight)
    {
        var canvasTf = transform;

        // 基準解像度上での寸法。縦持ちは横幅が狭いので、やや小さめのボタンにする。
        Vector2 btnSize = portrait ? new Vector2(420f, 90f) : new Vector2(300f, 80f);
        Vector2 inputSize = portrait ? new Vector2(420f, 90f) : new Vector2(300f, 80f);
        float rowGap = portrait ? 105f : 95f;

        // --- 中央の縦積みクラスタ ---
        // 上から: 部屋一覧 / [作成・参加ボタン + 入力欄] / 接続パネル(またはロビーパネル)
        float y = portrait ? 330f : 250f;

        // 部屋一覧
        var roomListRt = canvasTf.Find("RoomListPanel")?.GetComponent<RectTransform>();
        if (roomListRt != null)
        {
            CenterAnchor(roomListRt);
            roomListRt.sizeDelta = new Vector2(portrait ? 700f : 560f, portrait ? 300f : 240f);
            // クラスタ上端(y + ボタン半分)から余白を空けた位置に、パネル下端が来るようにする
            float listH = portrait ? 300f : 240f;
            float clusterTop = y + btnSize.y * 0.5f;
            roomListRt.anchoredPosition = new Vector2(0f, clusterTop + 30f + listH * 0.5f);
        }

        // 作成/参加ボタンと入力欄を左右に並べる
        float colGap = (portrait ? 420f : 300f) * 0.5f + 20f;
        SetRect(canvasTf, "RoomCreate", new Vector2(-colGap, y), btnSize);
        SetRect(canvasTf, "RoomId", new Vector2(colGap, y), inputSize);
        SetRect(canvasTf, "RoomJoin", new Vector2(-colGap, y - rowGap), btnSize);
        SetRect(canvasTf, "RoomPassword", new Vector2(colGap, y - rowGap), inputSize);

        float clusterBottom = y - rowGap - btnSize.y * 0.5f;

        // --- 接続パネル(Host/Connect/Server) ---
        float panelGap = 40f;
        float connectPanelH = portrait ? 280f : 200f;
        float connectPanelW = portrait ? 760f : 1000f;
        float connectCenterY = clusterBottom - panelGap - connectPanelH * 0.5f;
        SetRect(canvasTf, "ConnectPanel", new Vector2(0f, connectCenterY), new Vector2(connectPanelW, connectPanelH));

        if (portrait)
        {
            // 縦持ち: 3段に積む
            SetRect(canvasTf, "ConnectPanel/AddressInput", new Vector2(0f, 85f), inputSize);
            SetRect(canvasTf, "ConnectPanel/BtnHost", new Vector2(-115f, -20f), new Vector2(220f, 80f));
            SetRect(canvasTf, "ConnectPanel/BtnConnect", new Vector2(115f, -20f), new Vector2(220f, 80f));
            SetRect(canvasTf, "ConnectPanel/BtnServer", new Vector2(0f, -110f), new Vector2(220f, 80f));
        }
        else
        {
            // 横持ち: 横一列に並べる
            SetRect(canvasTf, "ConnectPanel/AddressInput", new Vector2(-330f, 25f), new Vector2(280f, 80f));
            SetRect(canvasTf, "ConnectPanel/BtnHost", new Vector2(0f, 25f), new Vector2(280f, 80f));
            SetRect(canvasTf, "ConnectPanel/BtnConnect", new Vector2(330f, 25f), new Vector2(280f, 80f));
            SetRect(canvasTf, "ConnectPanel/BtnServer", new Vector2(0f, -70f), new Vector2(280f, 80f));
        }

        // --- ロビーパネル(入室後: Ready/人数表示) ---
        // ConnectPanelと同じ位置に出す(排他表示のため)
        SetRect(canvasTf, "LobbyPanel", new Vector2(0f, connectCenterY), new Vector2(connectPanelW, connectPanelH));
        if (portrait)
        {
            SetRect(canvasTf, "LobbyPanel/BtnReady", new Vector2(0f, 80f), btnSize);
            SetRect(canvasTf, "LobbyPanel/ReadyStatusText", new Vector2(0f, -10f), new Vector2(600f, 70f));
            SetRect(canvasTf, "LobbyPanel/PlayerCountText", new Vector2(0f, -90f), new Vector2(600f, 70f));
        }
        else
        {
            SetRect(canvasTf, "LobbyPanel/BtnReady", new Vector2(-330f, 25f), new Vector2(280f, 80f));
            SetRect(canvasTf, "LobbyPanel/ReadyStatusText", new Vector2(30f, 25f), new Vector2(300f, 80f));
            SetRect(canvasTf, "LobbyPanel/PlayerCountText", new Vector2(330f, 25f), new Vector2(300f, 80f));
        }

        // --- 開始/設定ボタン ---
        float belowPanelY = connectCenterY - connectPanelH * 0.5f - panelGap - 45f;
        SetRect(canvasTf, "StartGame", new Vector2(-160f, belowPanelY), new Vector2(300f, 90f));
        SetRect(canvasTf, "BtnSettings", new Vector2(160f, belowPanelY), new Vector2(300f, 90f));
    }

    // 中央アンカーに揃えるヘルパー(基準解像度上の座標で扱えるようにする)
    private static void CenterAnchor(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    // 中央基準で位置とサイズをまとめて設定する
    private static void SetRect(Transform parent, string path, Vector2 pos, Vector2 size)
    {
        var rt = parent.Find(path)?.GetComponent<RectTransform>();
        if (rt == null) return;
        CenterAnchor(rt);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private void ApplyOthersLayout(bool portrait, float canvasWidth, float canvasHeight)
    {
        var canvasTf = transform;

        // --- 上部: ボタン ---
        Vector2 topBtnSize = new Vector2(220f, 70f);
        SetAnchoredRect(canvasTf, "BtnLeaveRoom", new Vector2(0f, 1f), new Vector2(40f, -20f), topBtnSize);
        SetAnchoredRect(canvasTf, "BtnHistory", new Vector2(1f, 1f), new Vector2(-40f, -20f), topBtnSize);

        // --- 上部: タイマーバー(左右に伸ばす) ---
        StretchHorizontal(canvasTf, "RoundTimerBarPanel", 60f, -100f, 18f);
        StretchHorizontal(canvasTf, "TransitionBarPanel", 60f, -124f, 18f);

        // --- 上部: 状況テキスト ---
        // 固定幅(1100)にすると、縦持ちなどCanvas幅がそれより狭い場合に画面外へはみ出す。
        // 左右マージン指定のストレッチにして、常に画面内に収める。
        StretchHorizontal(canvasTf, "StatusText", 40f, -145f, 70f);
        // 背景(Bg)が親より外側に広がっていると、親を画面内に収めても背景がはみ出す。
        // 親いっぱいにぴったり合わせる。
        var statusBg = canvasTf.Find("StatusText/Bg")?.GetComponent<RectTransform>();
        if (statusBg != null)
        {
            statusBg.anchorMin = Vector2.zero;
            statusBg.anchorMax = Vector2.one;
            statusBg.offsetMin = Vector2.zero;
            statusBg.offsetMax = Vector2.zero;
        }

        // --- 中央: 使用済みカード一覧 ---
        // 中身(カード)の配置はUIEventsManager側が行う。ここでは基準点だけ決める。
        // 一覧の開始位置。縦持ちは縦に余裕があるので、上部の固定要素の下から少し空ける。
        float othersTop = portrait ? -260f : -230f;
        var othersRt = canvasTf.Find("OthersCardParent")?.GetComponent<RectTransform>();
        if (othersRt != null)
        {
            othersRt.anchorMin = new Vector2(0.5f, 1f);
            othersRt.anchorMax = new Vector2(0.5f, 1f);
            othersRt.pivot = new Vector2(0.5f, 1f);
            othersRt.anchoredPosition = new Vector2(0f, othersTop);
        }
        // ラベルはカード一覧と同じ基準点に重ねる(内部で行ごとにずらす)
        var labelsRt = canvasTf.Find("OthersLabelsParent")?.GetComponent<RectTransform>();
        if (labelsRt != null && othersRt != null)
        {
            labelsRt.anchorMin = othersRt.anchorMin;
            labelsRt.anchorMax = othersRt.anchorMax;
            labelsRt.pivot = othersRt.pivot;
            labelsRt.anchoredPosition = othersRt.anchoredPosition;
        }
        // 「今出したカード」枠はOthersCardParentの子なので、ここでの配置は不要

        // --- 下部: 自分の手札 ---
        // 下端アンカーにすることで、画面下からの距離が常に一定になる。
        var myRt = canvasTf.Find("MyCardParent")?.GetComponent<RectTransform>();
        if (myRt != null)
        {
            myRt.anchorMin = new Vector2(0.5f, 0f);
            myRt.anchorMax = new Vector2(0.5f, 0f);
            myRt.pivot = new Vector2(0.5f, 0f);
            // 縦持ちはDebugPanel(左下)と重ならないよう少し上げる
            myRt.anchoredPosition = new Vector2(0f, portrait ? 150f : 40f);
        }

        // --- 下部: 確定/次へボタン ---
        // 手札の上に十分な間隔を空けて配置する(手札は最大2行 = 約300 になりうる)。
        Vector2 actionBtnSize = new Vector2(340f, 90f);
        float myCardsBottom = portrait ? 150f : 40f;
        // 手札の上端から少し上に置く。手札は最大2行なので、その高さを見込む。
        // (固定値で高く置きすぎると、上の使用済み一覧と重なる)
        float myCardsTop = myCardsBottom + 150f * MyCardRowCountForLayout;
        float actionBtnY = myCardsTop + 70f;
        SetAnchoredRect(canvasTf, "BtnConfirmCard", new Vector2(0.5f, 0f), new Vector2(0f, actionBtnY), actionBtnSize);
        SetAnchoredRect(canvasTf, "BtnNextRound", new Vector2(0.5f, 0f), new Vector2(0f, actionBtnY), actionBtnSize);

        // --- フォントサイズを基準解像度に合わせる ---
        // シーン上に14や32など小さい固定値のまま残っているものがあるため、ここで統一する。
        SetFontRange(canvasTf, "BtnConfirmCard", 16f, 44f);
        SetFontRange(canvasTf, "BtnNextRound", 16f, 44f);
        SetFontRange(canvasTf, "RoomId", 16f, 40f);
        SetFontRange(canvasTf, "RoomPassword", 16f, 40f);
    }

    // 設定画面を開くタイミングでレイアウトを適用し直すための入口。
    // (Awake時だけだと、シーン上の固定値が残ることがあるため)
    public void ReapplySettingsPanel()
    {
        var size = GetActualCanvasSize();
        ApplySettingsPanelLayout(transform, size.y >= size.x);
    }

    // === 設定画面のレイアウト ===
    // 縦持ちは横幅が狭いため、ラベルと入力欄を横並びではなく縦積みにする。
    // 横並びのままだと1要素あたりの幅が足りず、文字が極端に縮小されて読めなくなる。
    private void ApplySettingsPanelLayout(Transform canvasTf, bool portrait)
    {
        var panel = canvasTf.Find("SettingsPanel");
        if (panel == null) return;
        var panelRt = panel.GetComponent<RectTransform>();

        // モーダルの背後を覆う暗幕。
        // パネル本体のalphaを上げても角丸スプライトの縁から背景が透けるため全画面の幕を敷く。
        // パネルの子にすると「パネル背景より前面」に描画されてしまうので、
        // Canvas直下に置き、パネルの直前(=背面)に配置する。
        var dimTf = canvasTf.Find("ModalDim") as RectTransform;
        if (dimTf == null)
        {
            var dimGo = new GameObject("ModalDim");
            dimGo.transform.SetParent(canvasTf, false);
            dimTf = dimGo.AddComponent<RectTransform>();
            var dimImg = dimGo.AddComponent<UnityEngine.UI.Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.8f);
            dimGo.SetActive(false); // 初期状態は非表示(開いたときにUIEventsManagerが有効化する)
        }
        // 画面全体を覆う
        dimTf.anchorMin = Vector2.zero;
        dimTf.anchorMax = Vector2.one;
        dimTf.offsetMin = Vector2.zero;
        dimTf.offsetMax = Vector2.zero;
        // 描画順を整える(表示/非表示はUIEventsManagerが開閉時に制御する)。
        // 暗幕を最前面に出した直後にパネルを最前面へ動かすことで、
        // 「暗幕 → パネル」の順(パネルが手前)になる。
        // Canvasの有効/無効切り替え中はSetSiblingIndexが呼べない(警告になる)ため、
        // パネルが表示されているときだけ描画順を整える。
        if (panel.gameObject.activeInHierarchy)
        {
            dimTf.SetAsLastSibling();
            panel.SetAsLastSibling();
        }

        // パネル背景は不透明にする。
        // 角丸スプライトを使っているとalpha 0.97程度でも背後が透けて見えるため。
        var panelImg = panel.GetComponent<UnityEngine.UI.Image>();
        if (panelImg != null)
        {
            var c = panelImg.color;
            panelImg.color = new Color(c.r, c.g, c.b, 1f);
        }

        // パネル自体のサイズ。縦持ちは縦に伸ばして1項目ずつ積めるようにする。
        // Canvasの実サイズを基準に決める。固定値だと、縦持ちでCanvas幅が
        // それより狭い場合に画面外へはみ出すため。
        var canvasSize = GetActualCanvasSize();
        // 横持ちは項目を2列に並べるが、それでも縦に5段(Title/CardCount/MaxPlayers/
        // ScoringMode/ボタン)必要なため、620では下部ボタンが30pxはみ出していた。
        Vector2 panelSize = portrait
            ? new Vector2(Mathf.Min(900f, canvasSize.x * 0.92f), Mathf.Min(1150f, canvasSize.y * 0.62f))
            : new Vector2(Mathf.Min(1000f, canvasSize.x * 0.7f), Mathf.Min(720f, canvasSize.y * 0.85f));
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = panelSize;

        float w = panelSize.x;
        float rowH = portrait ? 90f : 80f;

        if (portrait)
        {
            // 縦積み: ラベルの下に入力欄を置く
            float y = panelSize.y * 0.5f - 90f;
            SetRect(panel, "Title", new Vector2(0f, y), new Vector2(w * 0.9f, 90f));
            y -= 130f;
            SetRect(panel, "CardCountLabel", new Vector2(0f, y), new Vector2(w * 0.86f, rowH));
            y -= rowH + 10f;
            SetRect(panel, "CardCountInput", new Vector2(0f, y), new Vector2(w * 0.5f, rowH));
            y -= rowH + 40f;
            SetRect(panel, "MaxPlayersLabel", new Vector2(0f, y), new Vector2(w * 0.86f, rowH));
            y -= rowH + 10f;
            SetRect(panel, "MaxPlayersInput", new Vector2(0f, y), new Vector2(w * 0.5f, rowH));
            y -= rowH + 40f;
            SetRect(panel, "ScoringModeLabel", new Vector2(0f, y), new Vector2(w * 0.86f, rowH));
            y -= rowH + 10f;
            SetRect(panel, "BtnScoringFixed", new Vector2(-w * 0.22f, y), new Vector2(w * 0.4f, rowH));
            SetRect(panel, "BtnScoringSum", new Vector2(w * 0.22f, y), new Vector2(w * 0.4f, rowH));
            y -= rowH + 50f;
            SetRect(panel, "BtnApplySettings", new Vector2(-w * 0.22f, y), new Vector2(w * 0.4f, rowH));
            SetRect(panel, "BtnCloseSettings", new Vector2(w * 0.22f, y), new Vector2(w * 0.4f, rowH));
        }
        else
        {
            // 横持ち: ラベルと入力欄を左右に並べる
            float y = panelSize.y * 0.5f - 70f;
            SetRect(panel, "Title", new Vector2(0f, y), new Vector2(w * 0.9f, 80f));
            y -= 110f;
            SetRect(panel, "CardCountLabel", new Vector2(-w * 0.2f, y), new Vector2(w * 0.42f, rowH));
            SetRect(panel, "CardCountInput", new Vector2(w * 0.25f, y), new Vector2(w * 0.24f, rowH));
            y -= rowH + 20f;
            SetRect(panel, "MaxPlayersLabel", new Vector2(-w * 0.2f, y), new Vector2(w * 0.42f, rowH));
            SetRect(panel, "MaxPlayersInput", new Vector2(w * 0.25f, y), new Vector2(w * 0.24f, rowH));
            y -= rowH + 30f;
            SetRect(panel, "ScoringModeLabel", new Vector2(0f, y), new Vector2(w * 0.9f, rowH));
            y -= rowH + 15f;
            SetRect(panel, "BtnScoringFixed", new Vector2(-w * 0.23f, y), new Vector2(w * 0.42f, rowH));
            SetRect(panel, "BtnScoringSum", new Vector2(w * 0.23f, y), new Vector2(w * 0.42f, rowH));
            y -= rowH + 30f;
            SetRect(panel, "BtnApplySettings", new Vector2(-w * 0.19f, y), new Vector2(w * 0.34f, rowH));
            SetRect(panel, "BtnCloseSettings", new Vector2(w * 0.19f, y), new Vector2(w * 0.34f, rowH));
        }

        // フォントは基準解像度に合わせて統一する。
        // シーン上に14や26といった小さい固定値が残っており、画面が大きくても拡大されなかった。
        SetFontRange(panel, "Title", 20f, 64f);
        foreach (var n in new[] { "CardCountLabel", "MaxPlayersLabel", "ScoringModeLabel" })
            SetFontRange(panel, n, 16f, 44f);
        foreach (var n in new[] { "CardCountInput", "MaxPlayersInput" })
            SetFontRange(panel, n, 16f, 44f);
        foreach (var n in new[] { "BtnScoringFixed", "BtnScoringSum", "BtnApplySettings", "BtnCloseSettings" })
            SetFontRange(panel, n, 14f, 40f);
    }

    // 配下の全テキストの自動サイズ範囲を揃える。
    // シーン上に小さい固定値(14等)が残っていると、画面が大きくても文字が
    // 拡大されず読めなくなるため、コード側で統一する。
    private static void SetFontRange(Transform parent, string path, float min, float max)
    {
        var t = parent.Find(path);
        if (t == null) return;

        // TMP_InputFieldは内部テキストのサイズをpointSizeで別管理しており、
        // 子テキストのfontSizeMaxだけ上げても実際の表示サイズは変わらない。
        var input = t.GetComponent<TMPro.TMP_InputField>();
        if (input != null)
        {
            input.pointSize = max * 0.8f;
            if (input.textComponent != null)
            {
                input.textComponent.enableAutoSizing = false;
                input.textComponent.fontSize = max * 0.8f;
                // 入力欄の枠が縦に大きいと、上寄せのままでは文字が小さく見えるため中央に揃える
                input.textComponent.alignment = TMPro.TextAlignmentOptions.Center;
            }
            if (input.placeholder is TMPro.TMP_Text ph)
            {
                ph.enableAutoSizing = false;
                ph.fontSize = max * 0.8f;
                ph.alignment = TMPro.TextAlignmentOptions.Center;
            }
            // TextAreaが枠いっぱいに広がるようにする(余白が大きいと文字が小さく見える)
            var textArea = t.Find("Text Area")?.GetComponent<RectTransform>();
            if (textArea != null)
            {
                textArea.anchorMin = Vector2.zero;
                textArea.anchorMax = Vector2.one;
                textArea.offsetMin = new Vector2(12f, 4f);
                textArea.offsetMax = new Vector2(-12f, -4f);
            }
            return;
        }

        foreach (var tmp in t.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = min;
            tmp.fontSizeMax = max;
        }
    }

    // アンカーを指定して位置・サイズを設定する。
    // anchorはpivotと一致させるので、anchoredPositionは「そのアンカーからの距離」になる。
    private static void SetAnchoredRect(Transform parent, string path, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = parent.Find(path)?.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // 左右いっぱいに伸ばし、上端からの距離と高さを指定する
    private static void StretchHorizontal(Transform parent, string path, float sideMargin, float topOffset, float height)
    {
        var rt = parent.Find(path)?.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(sideMargin, topOffset - height);
        rt.offsetMax = new Vector2(-sideMargin, topOffset);
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
