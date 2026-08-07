using UnityEngine;
using UnityEngine.UI;

// 画面のアスペクト比に応じてCanvasScalerのmatchWidthOrHeightを動的に切り替える。
// 基準(1920x1080, 16:9)より横長の画面では高さ基準、縦長の画面では幅基準にすることで、
// 要素が画面外にはみ出したり極端に小さくなったりするのを防ぐ。
[RequireComponent(typeof(CanvasScaler))]
public class ResponsiveCanvasScaler : MonoBehaviour
{
    private CanvasScaler scaler;
    private const float ReferenceAspect = 1920f / 1080f;

    private int _lastWidth = -1;
    private int _lastHeight = -1;

    private void Awake()
    {
        scaler = GetComponent<CanvasScaler>();
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

        float currentAspect = (float)Screen.width / Screen.height;

        // 基準より横長(ワイド)な画面は高さ基準(1)、基準より縦長な画面は幅基準(0)にすることで、
        // どちらの方向にもコンテンツがはみ出さないようにする。
        scaler.matchWidthOrHeight = currentAspect >= ReferenceAspect ? 1f : 0f;
    }
}
