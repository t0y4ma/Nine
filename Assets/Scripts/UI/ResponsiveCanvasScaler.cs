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
        // 上部要素は画面高の割合で配置し、合計が20%に収まるようにする。
        // 固定値だと解像度によって過剰/不足が生じ、
        // 盤面(GameBoard)に回せる高さが変わってしまう。
        float topH = canvasHeight * (IsPortraitMode ? 0.20f : 0.16f);
        float btnH = topH * (IsPortraitMode ? 0.26f : 0.36f);
        // 幅は画面幅を基準にする。
        // 高さから比率(x2.6)で出すと、縦持ちでは画面幅に対して
        // 大きくなりすぎ、左右のボタンが中央で重なっていた。
        float btnW = canvasWidth * (IsPortraitMode ? 0.26f : 0.16f);
        float sideGap = canvasWidth * 0.02f;
        SetAnchoredRect(canvasTf, "BtnLeaveRoom", new Vector2(0f, 1f),
            new Vector2(sideGap, -topH * 0.06f), new Vector2(btnW, btnH));
        SetAnchoredRect(canvasTf, "BtnHistory", new Vector2(1f, 1f),
            new Vector2(-sideGap, -topH * 0.06f), new Vector2(btnW, btnH));

        // バーとStatusTextの左右余白も画面幅基準にする
        float barH = topH * 0.07f;
        float barMargin = canvasWidth * 0.04f;
        StretchHorizontal(canvasTf, "RoundTimerBarPanel", barMargin, -(topH * 0.46f), barH);
        StretchHorizontal(canvasTf, "TransitionBarPanel", barMargin, -(topH * 0.46f + barH + 4f), barH);

        StretchHorizontal(canvasTf, "StatusText", canvasWidth * 0.03f, -(topH * 0.62f), topH * 0.30f);
        var statusBg = canvasTf.Find("StatusText/Bg")?.GetComponent<RectTransform>();
        if (statusBg != null)
        {
            statusBg.anchorMin = Vector2.zero;
            statusBg.anchorMax = Vector2.one;
            statusBg.offsetMin = Vector2.zero;
            statusBg.offsetMax = Vector2.zero;
        }

        // === ゲーム本体を縦3領域に配分する ===
        // 上から: 使用済み一覧 / 今出したカード / 手札。
        // 高さの配分はLayoutElement.flexibleHeightでUnityに任せる。
        // 手計算で各領域の高さを出すと、片方を変えたときにもう片方の
        // 計算を直し忘れて重なる、という事故が繰り返し起きていた。
        var boardRt = canvasTf.Find("GameBoard") as RectTransform;
        if (boardRt == null)
        {
            var go = new GameObject("GameBoard");
            go.transform.SetParent(canvasTf, false);
            boardRt = go.AddComponent<RectTransform>();
        }
        // 上部固定要素の下から、画面下端までを占める
        boardRt.anchorMin = new Vector2(0f, 0f);
        boardRt.anchorMax = new Vector2(1f, 1f);
        boardRt.pivot = new Vector2(0.5f, 0.5f);
        // 画面端ギリギリまで使うと窮屈で見にくいため、左右に余白を取る。
        // 割合(1%)にしておけば、どの解像度でも同じ見た目の余白になる。
        float sideMargin = canvasWidth * 0.01f;
        // 上部はボタン/タイマーバー/StatusTextが占める。
        // 固定220pxにしていたため、画面が大きいと過剰に、
        // 小さいと不足していた。画面高の割合で確保する。
        // 横持ちは画面が低いので、上部の占有を抑えて盤面に回す。
        // 縦持ちは縦に余裕があるため、上部を広めに取って見やすくする。
        float topReserved = canvasHeight * (IsPortraitMode ? 0.20f : 0.16f);
        boardRt.offsetMin = new Vector2(sideMargin, canvasHeight * 0.01f);
        boardRt.offsetMax = new Vector2(-sideMargin, -topReserved);

        var vl = boardRt.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (vl == null) vl = boardRt.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.UpperCenter;
        // 領域間の余白は最小限にし、その分を一覧に回す
        vl.spacing = 4f;
        vl.childControlWidth = true;
        vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        // 各領域をこのコンテナの子にし、高さの取り分を指定する
        // 各領域の「最低これだけは欲しい高さ」と「余りの取り分」を指定する。
        // minHeightを一律60にしていたため、手札が90pxまで潰れてカードが
        // 0.5px相当になっていた。手札は操作対象なので厚めに確保する。
        // 高さは「GameBoardの何割か」で決める。
        // minHeight/preferredHeightを混ぜると、VerticalLayoutGroupが
        // preferredを優先して比率配分が崩れるため、preferredで明示する。
        // boardRt.rect.heightはCanvas座標、canvasHeightは実ピクセル。
        // 単位が違うため、フォールバック時はscaleFactorで割ってCanvas座標に揃える。
        // (WebGLのscaleFactor=2の環境では2倍の値になり、各領域が過大に確保されていた)
        var cvForBoard = GetComponent<Canvas>();
        float sfBoard = (cvForBoard != null && cvForBoard.scaleFactor > 0.01f) ? cvForBoard.scaleFactor : 1f;
        float boardH = boardRt.rect.height > 100f
            ? boardRt.rect.height
            : (canvasHeight / sfBoard) - 240f;
        float usableH2 = Mathf.Max(200f, boardH - 80f - 12f);
        bool portraitNow = IsPortraitMode;
        float rO = portraitNow ? 0.66f : 0.52f;
        float rP = portraitNow ? 0.20f : 0.28f;
        float rH = portraitNow ? 0.14f : 0.20f;
        SetBoardSlotAbs(canvasTf, boardRt, "OthersCardParent", usableH2 * rO, 0f);
        SetBoardSlotAbs(canvasTf, boardRt, "PlayedCardsParent", usableH2 * rP, 0f);
        // 確定/次へボタンの席を、手札の前にあらかじめ確保しておく。
        // ボタンを後から重ねる形にすると、表示/非表示のたびに
        // 手札の位置がずれたり重なったりするため。
        EnsureActionButtonSlot(canvasTf, boardRt);
        SetBoardSlotAbs(canvasTf, boardRt, "MyCardParent", usableH2 * rH, 0f);

        // 「今出したカード」の帯はUIEventsManager側で設定する。
        // (このメソッドはPlayedCardsParentが生成される前に走ることがあり、
        //  ここでFindしても取得できないため)

        // 確定/次へボタンの配置はEnsureActionButtonSlotで行う(席を固定確保する)。

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
        // 選択時間(スライダー)の行を追加した分だけ高さを増やしている。
        Vector2 panelSize = portrait
            ? new Vector2(Mathf.Min(900f, canvasSize.x * 0.92f), Mathf.Min(1260f, canvasSize.y * 0.72f))
            : new Vector2(Mathf.Min(1000f, canvasSize.x * 0.7f), Mathf.Min(820f, canvasSize.y * 0.92f));
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
            SetRect(panel, "TimeLimitLabel", new Vector2(0f, y), new Vector2(w * 0.86f, rowH));
            y -= rowH + 10f;
            SetRect(panel, "TimeLimitSlider", new Vector2(0f, y), new Vector2(w * 0.8f, rowH * 0.5f));
            y -= rowH + 40f;
            SetRect(panel, "ScoringModeLabel", new Vector2(0f, y), new Vector2(w * 0.86f, rowH));
            y -= rowH + 10f;
            // 得点方式は3択を1行に並べる
            SetRect(panel, "BtnScoringFixed", new Vector2(-w * 0.3f, y), new Vector2(w * 0.28f, rowH));
            SetRect(panel, "BtnScoringSum", new Vector2(0f, y), new Vector2(w * 0.28f, rowH));
            SetRect(panel, "BtnScoringPointCards", new Vector2(w * 0.3f, y), new Vector2(w * 0.28f, rowH));
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
            y -= rowH + 20f;
            SetRect(panel, "TimeLimitLabel", new Vector2(-w * 0.2f, y), new Vector2(w * 0.42f, rowH));
            SetRect(panel, "TimeLimitSlider", new Vector2(w * 0.25f, y), new Vector2(w * 0.4f, rowH * 0.5f));
            y -= rowH + 30f;
            SetRect(panel, "ScoringModeLabel", new Vector2(0f, y), new Vector2(w * 0.9f, rowH));
            y -= rowH + 15f;
            // 得点方式は3択を1行に並べる
            SetRect(panel, "BtnScoringFixed", new Vector2(-w * 0.3f, y), new Vector2(w * 0.28f, rowH));
            SetRect(panel, "BtnScoringSum", new Vector2(0f, y), new Vector2(w * 0.28f, rowH));
            SetRect(panel, "BtnScoringPointCards", new Vector2(w * 0.3f, y), new Vector2(w * 0.28f, rowH));
            y -= rowH + 30f;
            SetRect(panel, "BtnApplySettings", new Vector2(-w * 0.19f, y), new Vector2(w * 0.34f, rowH));
            SetRect(panel, "BtnCloseSettings", new Vector2(w * 0.19f, y), new Vector2(w * 0.34f, rowH));
        }

        // フォントは基準解像度に合わせて統一する。
        // シーン上に14や26といった小さい固定値が残っており、画面が大きくても拡大されなかった。
        SetFontRange(panel, "Title", 20f, 64f);
        foreach (var n in new[] { "CardCountLabel", "MaxPlayersLabel", "TimeLimitLabel", "ScoringModeLabel" })
            SetFontRange(panel, n, 16f, 44f);

        // スライダーのつまみは高さに合わせた正方形にし、つまみが端で枠からはみ出さないよう余白を取る。
        // (標準部品のままだと、つまみの幅が20px固定で大きな画面では小さすぎる)
        var sliderRt = panel.Find("TimeLimitSlider") as RectTransform;
        if (sliderRt != null)
        {
            float knob = sliderRt.sizeDelta.y;
            var slideArea = sliderRt.Find("Handle Slide Area") as RectTransform;
            if (slideArea != null)
            {
                slideArea.offsetMin = new Vector2(knob * 0.5f, slideArea.offsetMin.y);
                slideArea.offsetMax = new Vector2(-knob * 0.5f, slideArea.offsetMax.y);
                var handle = slideArea.Find("Handle") as RectTransform;
                if (handle != null) handle.sizeDelta = new Vector2(knob, handle.sizeDelta.y);
            }
            var fillArea = sliderRt.Find("Fill Area") as RectTransform;
            if (fillArea != null)
            {
                fillArea.offsetMin = new Vector2(knob * 0.5f, fillArea.offsetMin.y);
                fillArea.offsetMax = new Vector2(-knob * 0.5f, fillArea.offsetMax.y);
                var fillRt = fillArea.Find("Fill") as RectTransform;
                if (fillRt != null) fillRt.sizeDelta = new Vector2(0f, fillRt.sizeDelta.y);
            }
        }
        foreach (var n in new[] { "CardCountInput", "MaxPlayersInput" })
            SetFontRange(panel, n, 16f, 44f);
        foreach (var n in new[] { "BtnScoringFixed", "BtnScoringSum", "BtnScoringPointCards", "BtnApplySettings", "BtnCloseSettings" })
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

    // 確定/次へボタンのための「席」を作る。
    // ボタン自体は表示/非表示が切り替わるが、席は常に確保しておくことで
    // ボタンの有無で他の要素の位置が動かないようにする。
    private static void EnsureActionButtonSlot(Transform canvasTf, RectTransform board)
    {
        var slot = board.Find("ActionButtonSlot") as RectTransform;
        if (slot == null)
        {
            var go = new GameObject("ActionButtonSlot");
            go.transform.SetParent(board, false);
            slot = go.AddComponent<RectTransform>();
        }
        var le = slot.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le == null) le = slot.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        le.flexibleHeight = 0f;   // 余りは分け合わず、固定の高さだけ取る
        // ボタンの高さ(76)ぶんあれば足りる。
        // 余分に確保していた分を一覧に回す。
        le.minHeight = 80f;
        le.preferredHeight = 80f;

        // ボタンを席の中央に置く(表示状態は各機能側が制御する)
        foreach (var name in new[] { "BtnConfirmCard", "BtnNextRound" })
        {
            var btn = canvasTf.Find(name) as RectTransform;
            if (btn == null) btn = slot.Find(name) as RectTransform;
            if (btn == null) continue;
            if (btn.parent != slot) btn.SetParent(slot, false);
            btn.anchorMin = new Vector2(0.5f, 0.5f);
            btn.anchorMax = new Vector2(0.5f, 0.5f);
            btn.pivot = new Vector2(0.5f, 0.5f);
            btn.anchoredPosition = Vector2.zero;
            btn.sizeDelta = new Vector2(320f, 76f);
        }
    }

    // ゲーム盤の高さ配分を適用し直す。
    // PlayedCardsParentは実行時に生成されるため、生成後に呼ぶ必要がある。
    public void ReapplyBoardSlots()
    {
        var board = transform.Find("GameBoard") as RectTransform;
        if (board == null) return;
        var cvR = GetComponent<Canvas>();
        float sfR = (cvR != null && cvR.scaleFactor > 0.01f) ? cvR.scaleFactor : 1f;
        float boardH = board.rect.height > 100f
            ? board.rect.height
            : (GetActualCanvasSize().y / sfR) - 240f;
        // ボタンの席(100)と行間(spacing 12 x 3)を先に差し引いてから配分する。
        // 比率の合計を1.0のままボタンを足していたため、
        // 合計が枠を超えて全体が圧縮されていた。
        const float BUTTON_H = 80f;
        float usableH = Mathf.Max(200f, boardH - BUTTON_H - 12f);
        // 縦持ちと横持ちで配分を変える。
        //   縦持ち: 一覧が1列(人数分の行)になるため厚く取る
        //   横持ち: 一覧が2列に収まり行数が半分なので、帯と手札に回せる
        // minHeightは指定しない(下限が効くと比率が崩れるため)。
        bool isPortrait = IsPortraitMode;
        float rOthers = isPortrait ? 0.66f : 0.52f;
        float rPlayed = isPortrait ? 0.20f : 0.28f;
        float rHand   = isPortrait ? 0.14f : 0.20f;
        SetBoardSlotAbs(transform, board, "OthersCardParent", usableH * rOthers, 0f);
        SetBoardSlotAbs(transform, board, "PlayedCardsParent", usableH * rPlayed, 0f);
        SetBoardSlotAbs(transform, board, "MyCardParent", usableH * rHand, 0f);

        // 並び順: 一覧 → 今出したカード → ボタン → 手札
        var oc = board.Find("OthersCardParent");
        var pc = board.Find("PlayedCardsParent");
        var ab = board.Find("ActionButtonSlot");
        var mc = board.Find("MyCardParent");
        if (oc != null) oc.SetSiblingIndex(0);
        if (pc != null) pc.SetSiblingIndex(1);
        if (ab != null) ab.SetSiblingIndex(2);
        if (mc != null) mc.SetSiblingIndex(3);
    }

    // 高さを実数で指定して組み込む。
    // flexibleHeightによる比率配分は、他の要素がpreferredHeightを持つと
    // そちらが優先されて崩れるため、全要素をpreferredで揃える。
    private static void SetBoardSlotAbs(Transform canvasTf, RectTransform board, string name, float height, float minHeight)
    {
        var rt = canvasTf.Find(name) as RectTransform;
        if (rt == null) rt = board.Find(name) as RectTransform;
        if (rt == null) return;
        if (rt.parent != board) rt.SetParent(board, false);

        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;

        var le = rt.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le == null) le = rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        le.flexibleHeight = 0f;
        le.minHeight = minHeight;
        le.preferredHeight = Mathf.Max(height, minHeight);
    }

    // ゲーム本体の縦配分コンテナに、指定した要素を組み込む。
    // flexibleHeightで「余った高さをどの比率で分け合うか」を指定し、
    // 実際の高さ計算はVerticalLayoutGroupに任せる。
    private static void SetBoardSlot(Transform canvasTf, RectTransform board, string name, float weight, float minHeight)
    {
        var rt = canvasTf.Find(name) as RectTransform;
        if (rt == null)
        {
            // まだ生成されていない要素(PlayedCardsParent等)は、
            // 生成側で親をGameBoardにするので、ここでは何もしない
            return;
        }
        if (rt.parent != board) rt.SetParent(board, false);

        // LayoutGroupが子のサイズを制御できるようにリセットする。
        // シーンに保存されたアンカーやsizeDeltaが残っていると、
        // 100x100のまま潰れて表示されることがある。
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;

        var le = rt.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le == null) le = rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        le.flexibleHeight = weight;
        le.minHeight = minHeight;
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
