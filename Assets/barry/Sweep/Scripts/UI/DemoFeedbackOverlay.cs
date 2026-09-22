using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 展演模式的純執行期 HUD：建立清楚的資訊層級，並集中 Combo、滿蓄力、
/// 升級、階段與狂熱高潮的全畫面回饋，不要求手動維護 Scene UI。
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
    private Image _ambientTint;
    private Image _phaseBacking;
    private Image _flash;
    private readonly Image[] _feverEdges = new Image[4];

    private Coroutine _scoreRoutine;
    private Coroutine _hitRoutine;
    private Coroutine _hintRoutine;
    private Coroutine _phaseRoutine;
    private Coroutine _flashRoutine;
    private int _phase;
    private bool _frenzy;
    private float _pulseClock;

    public static DemoFeedbackOverlay Attach(Component owner, TMP_Text fontTemplate)
    {
        if (owner == null) return null;

        Canvas canvas = owner.GetComponentInParent<Canvas>();
        if (canvas == null) canvas = owner.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Canvas[] sceneCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sceneCanvases.Length; i++)
            {
                if (sceneCanvases[i] != null && sceneCanvases[i].gameObject.scene == owner.gameObject.scene)
                {
                    canvas = sceneCanvases[i];
                    break;
                }
            }
        }
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

        _ambientTint = CreateFullScreenImage("[Demo] Pacing Tint");
        CreateFeverEdges();
        _flash = CreateFullScreenImage("[Demo] Screen Flash");
        _phaseBacking = CreatePanel("[Demo] Phase Backing", new Vector2(0.5f, 0.5f), new Vector2(1180f, 155f));

        _comboText = CreateText("[Demo] Combo", new Vector2(0.5f, 0.82f), new Vector2(820f, 110f), 46f);
        _scoreBurstText = CreateText("[Demo] Score Burst", new Vector2(0.5f, 0.62f), new Vector2(620f, 110f), 42f);
        _hitChainText = CreateText("[Demo] Hit Chain", new Vector2(0.5f, 0.5f), new Vector2(460f, 95f), 36f);
        _hintText = CreateText("[Demo] Hint", new Vector2(0.5f, 0.12f), new Vector2(1180f, 110f), 35f);
        _phaseText = CreateText("[Demo] Phase", new Vector2(0.5f, 0.5f), new Vector2(1200f, 180f), 70f);

        SetPacing(0, false);
        transform.SetAsLastSibling();
    }

    private void Update()
    {
        if (_ambientTint == null) return;
        _pulseClock += Time.unscaledDeltaTime;
        float pulse = 0.5f + (0.5f * Mathf.Sin(_pulseClock * (_frenzy ? 7.5f : 2.2f)));

        Color tint = _frenzy
            ? new Color(1f, 0.055f, 0.01f, Mathf.Lerp(0.025f, 0.065f, pulse))
            : new Color(1f, 0.42f, 0.03f, Mathf.Lerp(0.006f, 0.024f, (_phase / 3f) * pulse));
        _ambientTint.color = tint;

        Color edge = _frenzy
            ? new Color(1f, 0.12f, 0.025f, Mathf.Lerp(0.2f, 0.58f, pulse))
            : new Color(1f, 0.58f, 0.08f, Mathf.Lerp(0f, 0.13f, (_phase / 3f) * pulse));
        for (int i = 0; i < _feverEdges.Length; i++)
        {
            if (_feverEdges[i] != null) _feverEdges[i].color = edge;
        }
    }

    public void SetPacing(int phase, bool frenzy)
    {
        _phase = Mathf.Clamp(phase, 0, 3);
        _frenzy = frenzy;
        _pulseClock = 0f;
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
        _comboText.fontSize = Mathf.Lerp(44f, 58f, Mathf.Clamp01((count - 2f) / 13f));
        _comboText.color = Color.Lerp(
            new Color(1f, 0.87f, 0.16f),
            _frenzy ? new Color(1f, 0.12f, 0.025f) : new Color(1f, 0.27f, 0.055f),
            Mathf.Clamp01(count / 15f));
        _comboText.transform.localScale = Vector3.one * 1.14f;
        _comboText.gameObject.SetActive(true);

        if (count == 5 || count == 10 || count == 15)
            Flash(_comboText.color, count == 15 ? 0.22f : 0.13f, 0.2f);
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
        _scoreBurstText.fontSize = playerAssisted ? Mathf.Lerp(40f, 52f, Mathf.Clamp01(combo / 15f)) : 31f;
        _scoreBurstText.color = playerAssisted
            ? Color.Lerp(new Color(1f, 0.9f, 0.16f), new Color(1f, 0.2f, 0.035f), Mathf.Clamp01(combo / 15f))
            : new Color(0.58f, 0.66f, 0.72f, 0.82f);
        _scoreRoutine = StartCoroutine(PopAndFade(_scoreBurstText, 0.74f, playerAssisted ? 1.42f : 1.15f));
    }

    public void ShowHitChain(int count, Vector2 worldPosition)
    {
        if (_hitChainText == null) return;
        if (_hitRoutine != null) StopCoroutine(_hitRoutine);

        PositionAtWorldPoint(_hitChainText.rectTransform, worldPosition + Vector2.up * 0.8f);
        _hitChainText.text = count > 1 ? $"{count} HIT!" : "HIT!";
        _hitChainText.fontSize = Mathf.Lerp(34f, 45f, Mathf.Clamp01((count - 1f) / 8f));
        _hitChainText.color = Color.Lerp(Color.white, new Color(1f, 0.32f, 0.055f), Mathf.Clamp01((count - 1) / 8f));
        _hitRoutine = StartCoroutine(PopAndFade(_hitChainText, 0.44f, 1.58f));
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
        _phaseRoutine = StartCoroutine(PhaseMessage(color, 1.15f));
        if (flash) Flash(color, 0.25f, 0.24f);
    }

    public void ShowFullCharge()
    {
        ShowPhase("MAX POWER!", new Color(0.12f, 0.92f, 1f), true);
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
        text.outlineColor = new Color32(8, 10, 16, 235);
        text.outlineWidth = 0.2f;
        text.text = string.Empty;
        text.gameObject.SetActive(false);
        return text;
    }

    private Image CreateFullScreenImage(string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.clear;
        return image;
    }

    private Image CreatePanel(string name, Vector2 anchor, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.clear;
        image.gameObject.SetActive(false);
        return image;
    }

    private void CreateFeverEdges()
    {
        _feverEdges[0] = CreateEdge("[Demo] Fever Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -18f), Vector2.zero);
        _feverEdges[1] = CreateEdge("[Demo] Fever Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 18f));
        _feverEdges[2] = CreateEdge("[Demo] Fever Left", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(18f, 0f));
        _feverEdges[3] = CreateEdge("[Demo] Fever Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-18f, 0f), Vector2.zero);
    }

    private Image CreateEdge(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        Image edge = CreateFullScreenImage(name);
        RectTransform rect = edge.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return edge;
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

    private IEnumerator PhaseMessage(Color color, float duration)
    {
        _phaseBacking.gameObject.SetActive(true);
        _phaseBacking.color = new Color(0.025f, 0.035f, 0.06f, 0f);
        _phaseText.gameObject.SetActive(true);
        _phaseText.alpha = 1f;
        _phaseText.transform.localScale = Vector3.one * 0.62f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float popT = Mathf.Clamp01(t * 4.5f);
            float fade = 1f - Mathf.Clamp01((t - 0.58f) / 0.42f);
            _phaseText.transform.localScale = Vector3.one * Mathf.Lerp(0.62f, 1.28f, 1f - Mathf.Pow(1f - popT, 3f));
            _phaseText.alpha = fade;
            _phaseBacking.color = new Color(0.025f, 0.035f, 0.06f, 0.72f * fade);
            yield return null;
        }

        _phaseText.transform.localScale = Vector3.one;
        _phaseText.alpha = 1f;
        _phaseText.gameObject.SetActive(false);
        _phaseBacking.color = Color.clear;
        _phaseBacking.gameObject.SetActive(false);
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
