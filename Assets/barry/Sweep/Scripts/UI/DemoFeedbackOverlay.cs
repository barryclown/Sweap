using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 展演模式的純執行期 HUD。它不要求手動修改 Scene/Pefab，並且只負責短暫回饋。
/// </summary>
public sealed class DemoFeedbackOverlay : MonoBehaviour
{
    private Canvas _canvas;
    private RectTransform _canvasRect;
    private TMP_FontAsset _font;

    private TMP_Text _comboText;
    private TMP_Text _scoreBurstText;
    private TMP_Text _hitChainText;
    private TMP_Text _hintText;
    private TMP_Text _phaseText;
    private Image _flash;

    private Coroutine _scoreRoutine;
    private Coroutine _hitRoutine;
    private Coroutine _hintRoutine;
    private Coroutine _phaseRoutine;
    private Coroutine _flashRoutine;

    public static DemoFeedbackOverlay Attach(Component owner, TMP_Text fontTemplate)
    {
        if (owner == null) return null;

        Canvas canvas = owner.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[DemoFeedbackOverlay] 找不到 Canvas，展演提示將略過。", owner);
            return null;
        }

        DemoFeedbackOverlay overlay = canvas.GetComponent<DemoFeedbackOverlay>();
        if (overlay == null) overlay = canvas.gameObject.AddComponent<DemoFeedbackOverlay>();
        overlay.Initialize(canvas, fontTemplate);
        return overlay;
    }

    private void Initialize(Canvas canvas, TMP_Text fontTemplate)
    {
        if (_canvas != null) return;

        _canvas = canvas;
        _canvasRect = canvas.transform as RectTransform;
        _font = fontTemplate != null ? fontTemplate.font : TMP_Settings.defaultFontAsset;

        _flash = CreateFlash();
        _comboText = CreateText("[Demo] Combo", new Vector2(0.5f, 0.82f), new Vector2(720f, 100f), 42f);
        _scoreBurstText = CreateText("[Demo] Score Burst", new Vector2(0.5f, 0.62f), new Vector2(520f, 100f), 38f);
        _hitChainText = CreateText("[Demo] Hit Chain", new Vector2(0.5f, 0.5f), new Vector2(420f, 90f), 34f);
        _hintText = CreateText("[Demo] Hint", new Vector2(0.5f, 0.12f), new Vector2(1100f, 100f), 34f);
        _phaseText = CreateText("[Demo] Phase", new Vector2(0.5f, 0.5f), new Vector2(1200f, 180f), 64f);

        transform.SetAsLastSibling();
    }

    public void ShowCombo(int count, float multiplier)
    {
        if (_comboText == null) return;

        if (count <= 1)
        {
            _comboText.gameObject.SetActive(false);
            return;
        }

        _comboText.text = $"COMBO  x{count}   ×{multiplier:0.0}";
        _comboText.color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.25f, 0.08f), Mathf.Clamp01(count / 15f));
        _comboText.transform.localScale = Vector3.one * 1.12f;
        _comboText.gameObject.SetActive(true);
    }

    public void HideCombo()
    {
        if (_comboText != null) _comboText.gameObject.SetActive(false);
    }

    public void ShowScoreBurst(int score, bool playerAssisted, int combo, Vector2 worldPosition)
    {
        if (_scoreBurstText == null) return;
        if (_scoreRoutine != null) StopCoroutine(_scoreRoutine);

        PositionAtWorldPoint(_scoreBurstText.rectTransform, worldPosition);
        _scoreBurstText.text = playerAssisted
            ? $"+{score}  {(combo > 1 ? $"CHAIN ×{combo}" : "NICE!")}"
            : $"AUTO +{score}";
        _scoreBurstText.color = playerAssisted ? new Color(1f, 0.88f, 0.18f) : new Color(0.65f, 0.72f, 0.78f);
        _scoreRoutine = StartCoroutine(PopAndFade(_scoreBurstText, 0.72f, 1.35f));
    }

    public void ShowHitChain(int count, Vector2 worldPosition)
    {
        if (_hitChainText == null) return;
        if (_hitRoutine != null) StopCoroutine(_hitRoutine);

        PositionAtWorldPoint(_hitChainText.rectTransform, worldPosition + Vector2.up * 0.8f);
        _hitChainText.text = count > 1 ? $"{count} HIT!" : "HIT!";
        _hitChainText.color = Color.Lerp(Color.white, new Color(1f, 0.35f, 0.08f), Mathf.Clamp01((count - 1) / 8f));
        _hitRoutine = StartCoroutine(PopAndFade(_hitChainText, 0.42f, 1.55f));
    }

    public void ShowHint(string message, float duration = 2.6f)
    {
        if (_hintText == null) return;
        if (_hintRoutine != null) StopCoroutine(_hintRoutine);

        _hintText.text = message;
        _hintText.color = Color.white;
        _hintRoutine = StartCoroutine(FadeMessage(_hintText, duration, 1.08f));
    }

    public void ShowPhase(string message, Color color, bool flash = false)
    {
        if (_phaseText == null) return;
        if (_phaseRoutine != null) StopCoroutine(_phaseRoutine);

        _phaseText.text = message;
        _phaseText.color = color;
        _phaseRoutine = StartCoroutine(PopAndFade(_phaseText, 1.1f, 1.35f));

        if (flash) Flash(color, 0.25f, 0.22f);
    }

    public void ShowFullCharge()
    {
        ShowPhase("MAX POWER!", new Color(0.2f, 0.9f, 1f), true);
    }

    public void Flash(Color color, float alpha, float duration)
    {
        if (_flash == null) return;
        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine(color, alpha, duration));
    }

    private TMP_Text CreateText(string name, Vector2 anchor, Vector2 size, float fontSize)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.font = _font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.text = string.Empty;
        text.gameObject.SetActive(false);
        return text;
    }

    private Image CreateFlash()
    {
        GameObject go = new GameObject("[Demo] Screen Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.clear;
        image.gameObject.SetActive(false);
        return image;
    }

    private void PositionAtWorldPoint(RectTransform target, Vector2 worldPosition)
    {
        if (_canvasRect == null || target == null) return;

        Camera worldCamera = Camera.main;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(worldCamera, worldPosition);
        Camera uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPoint, uiCamera, out Vector2 localPoint))
        {
            target.anchorMin = target.anchorMax = new Vector2(0.5f, 0.5f);
            target.anchoredPosition = localPoint;
        }
    }

    private IEnumerator PopAndFade(TMP_Text text, float duration, float peakScale)
    {
        text.gameObject.SetActive(true);
        text.alpha = 1f;
        text.transform.localScale = Vector3.one * 0.65f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float popT = Mathf.Clamp01(t * 4f);
            float scale = Mathf.Lerp(0.65f, peakScale, 1f - Mathf.Pow(1f - popT, 3f));
            if (t > 0.35f) scale = Mathf.Lerp(peakScale, 1f, (t - 0.35f) / 0.65f);
            text.transform.localScale = Vector3.one * scale;
            text.alpha = 1f - Mathf.Clamp01((t - 0.5f) * 2f);
            yield return null;
        }

        text.transform.localScale = Vector3.one;
        text.alpha = 1f;
        text.gameObject.SetActive(false);
    }

    private IEnumerator FadeMessage(TMP_Text text, float duration, float peakScale)
    {
        text.gameObject.SetActive(true);
        text.alpha = 0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            text.alpha = Mathf.Clamp01(t * 5f) * Mathf.Clamp01((1f - t) * 5f);
            text.transform.localScale = Vector3.one * Mathf.Lerp(1f, peakScale, Mathf.Sin(t * Mathf.PI));
            yield return null;
        }

        text.transform.localScale = Vector3.one;
        text.alpha = 1f;
        text.gameObject.SetActive(false);
    }

    private IEnumerator FlashRoutine(Color color, float alpha, float duration)
    {
        _flash.gameObject.SetActive(true);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Color frameColor = color;
            frameColor.a = alpha * (1f - t);
            _flash.color = frameColor;
            yield return null;
        }

        _flash.color = Color.clear;
        _flash.gameObject.SetActive(false);
    }
}
