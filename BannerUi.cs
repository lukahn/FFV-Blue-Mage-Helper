using System;
using System.Linq;
using Il2CppInterop.Runtime.Injection;
using Last.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace FFVBlueMageHelper;

/// <summary>
/// Draws the Final Fantasy-style blue message box at the top of the screen.
/// </summary>
public sealed class BannerBehaviour : MonoBehaviour
{
    private const float FadeInSeconds = 0.2f;
    private const float FadeOutSeconds = 0.6f;
    private const int ReferenceWidth = 1920;
    private const int ReferenceHeight = 1080;

    private CanvasGroup _group;
    private Canvas _canvas;
    private Image _box;
    private Text _text;
    private RectTransform _boxRect;
    private Sprite _boxSprite;
    private Texture2D _boxTexture;

    private bool _visible;
    private float _shownAt;
    private float _holdUntil;

    private BattleController _pendingBattle;
    private float _pendingReadyAt;
    private int _pendingAttempts;
    private bool _pendingHandled;

    private bool _captureTest;
    private float _captureTestAt;

    private BattleController _lastBattle;
    private double _lastBattleStartTime;
    private float _nextBattlePoll;

    public BannerBehaviour() : base(ClassInjector.DerivedConstructorPointer<BannerBehaviour>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public BannerBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private void Awake()
    {
        try
        {
            if (BlueMageHelperPlugin.Verbose.Value)
            {
                BlueMageHelperPlugin.LogSource.LogInfo("Banner behaviour awake.");
            }

            BuildUi();

            if (BlueMageHelperPlugin.Verbose.Value)
            {
                BlueMageHelperPlugin.LogSource.LogInfo("Banner UI built.");
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogError($"Could not build banner UI: {e}");
        }
    }

    public void Schedule(BattleController controller)
    {
        _pendingBattle = controller;
        _pendingReadyAt = Time.unscaledTime + 0.35f;
        _pendingAttempts = 0;
        _pendingHandled = false;

        if (controller != null)
        {
            _lastBattle = controller;
            _lastBattleStartTime = controller.atTheStartPlayTime;
        }

        HideImmediate();
    }

    public void Hide()
    {
        if (_group == null || !_visible)
            return;

        _holdUntil = Time.unscaledTime;
    }

    public void ShowTest()
    {
        if (BlueMageHelperPlugin.Verbose.Value)
            BlueMageHelperPlugin.LogSource.LogInfo("Showing test banner.");

        Show("Goblin \u2014 Goblin Punch\nBlack Goblin \u2014 Goblin Punch", 2);
        _captureTest = true;
        _captureTestAt = Time.unscaledTime + 2.5f;
    }

    private void HideImmediate()
    {
        _visible = false;
        if (_group != null)
            _group.alpha = 0f;
    }

    private bool _loggedUpdate;
    private bool _loggedUpdateError;

    public void Update()
    {
        try
        {
            if (!_loggedUpdate)
            {
                _loggedUpdate = true;
                if (BlueMageHelperPlugin.Verbose.Value)
                    BlueMageHelperPlugin.LogSource.LogInfo("Banner update loop is running.");
            }

            UpdatePendingBattle();
            UpdateBattleFallback();
            UpdateVisibility();
            UpdateTestCapture();
        }
        catch (Exception e)
        {
            if (!_loggedUpdateError)
            {
                _loggedUpdateError = true;
                BlueMageHelperPlugin.LogSource.LogWarning($"Banner update failed: {e}");
            }

            _pendingBattle = null;
        }
    }

    /// <summary>
    /// Redundant detection path in case the StartInBattle hook is skipped: while
    /// the banner is idle, look for a battle that has reached its play states.
    /// </summary>
    private void UpdateBattleFallback()
    {
        if (_pendingBattle != null || Time.unscaledTime < _nextBattlePoll)
            return;

        _nextBattlePoll = Time.unscaledTime + 0.5f;

        BattleController controller;
        try
        {
            controller = UnityEngine.Object.FindObjectOfType<BattleController>();
        }
        catch
        {
            return;
        }

        if (controller == null)
        {
            _lastBattle = null;
            return;
        }

        if (controller == _lastBattle &&
            Math.Abs(controller.atTheStartPlayTime - _lastBattleStartTime) < 0.01)
        {
            return;
        }

        if (!controller.isBattleStart)
            return;

        Schedule(controller);
    }

    private void UpdatePendingBattle()
    {
        if (_pendingBattle == null || _pendingHandled)
            return;

        if (!_pendingBattle.isActiveAndEnabled)
        {
            _pendingBattle = null;
            return;
        }

        if (Time.unscaledTime < _pendingReadyAt)
            return;

        var lines = BlueMagicNotifier.BuildLines(_pendingBattle);
        _pendingAttempts++;

        if (lines != null)
        {
            _pendingHandled = true;
            _pendingBattle = null;

            if (lines.Count > 0)
            {
                int maxLines = Mathf.Clamp(BlueMageHelperPlugin.MaxLines.Value, 1, 20);
                var display = lines.Take(maxLines).ToList();
                if (lines.Count > maxLines)
                    display.Add($"+{lines.Count - maxLines} more");

                Show(string.Join("\n", display), display.Count);
            }
            else
            {
                HideImmediate();
            }
        }
        else if (_pendingAttempts >= 12)
        {
            // Enemies never became available (for example a scripted/no-battle scene).
            _pendingBattle = null;
        }
        else
        {
            _pendingReadyAt = Time.unscaledTime + 0.25f;
        }
    }

    private void UpdateVisibility()
    {
        if (_group == null || !_visible)
            return;

        float now = Time.unscaledTime;

        if (now < _shownAt + FadeInSeconds)
        {
            _group.alpha = Mathf.Clamp01((now - _shownAt) / FadeInSeconds);
            return;
        }

        if (now < _holdUntil)
        {
            _group.alpha = 1f;
            return;
        }

        float fade = Mathf.Clamp01((now - _holdUntil) / FadeOutSeconds);
        _group.alpha = 1f - fade;

        if (fade >= 1f)
            HideImmediate();
    }

    private void UpdateTestCapture()
    {
        if (!_captureTest || Time.unscaledTime < _captureTestAt)
            return;

        _captureTest = false;

        try
        {
            string path = System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "BlueMageBannerTest.png");
            ScreenCapture.CaptureScreenshot(path);
            BlueMageHelperPlugin.LogSource.LogInfo($"Saved test banner screenshot to {path}");
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Could not capture test banner: {e.Message}");
        }
    }

    private void Show(string content, int lineCount)
    {
        if (_group == null || _text == null || _boxRect == null)
            return;

        _text.text =
            "<size=19><color=#F3E3A2>BLUE MAGIC</color></size>\n" +
            content;

        float height = Mathf.Max(90f, _text.preferredHeight + 28f);
        float scale = Mathf.Max(0.0001f, _canvas != null ? _canvas.scaleFactor : 1f);
        float width = Mathf.Clamp(Screen.width / scale - 32f, 400f, 1100f);

        _boxRect.sizeDelta = new Vector2(width, height);

        var textRect = _text.rectTransform;
        textRect.sizeDelta = new Vector2(width - 48f, height - 24f);
        textRect.anchoredPosition = new Vector2(0f, -12f);

        if (BlueMageHelperPlugin.Verbose.Value)
        {
            BlueMageHelperPlugin.LogSource.LogInfo(
                $"Banner rect {_boxRect.rect.width}x{_boxRect.rect.height}, " +
                $"text preferred {_text.preferredHeight}, screen {Screen.width}x{Screen.height}, " +
                $"canvas scale {_canvas?.scaleFactor}");
            BlueMageHelperPlugin.LogSource.LogInfo(
                $"Box pos={_boxRect.position}, anchored={_boxRect.anchoredPosition}, " +
                $"pivot={_boxRect.pivot}, anchors={_boxRect.anchorMin}/{_boxRect.anchorMax}, " +
                $"fillCenter={_box.fillCenter}, type={_box.type}, border={_box.sprite.border}, " +
                $"textRect={_text.rectTransform.rect}");
        }

        _shownAt = Time.unscaledTime;
        _holdUntil = _shownAt + Mathf.Max(1f, BlueMageHelperPlugin.ShowDuration.Value);
        _visible = true;
        _group.alpha = 0f;
    }

    private void BuildUi()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        _canvas = canvas;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var boxObject = new GameObject("Box");
        boxObject.transform.SetParent(transform, false);

        _box = boxObject.AddComponent<Image>();
        _boxTexture = CreateBoxTexture();
        _boxSprite = Sprite.Create(
            _boxTexture,
            new Rect(0f, 0f, _boxTexture.width, _boxTexture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(10f, 10f, 10f, 10f));
        _box.sprite = _boxSprite;
        _box.type = Image.Type.Sliced;
        _box.raycastTarget = false;

        _boxRect = boxObject.GetComponent<RectTransform>();
        _boxRect.anchorMin = new Vector2(0.5f, 1f);
        _boxRect.anchorMax = new Vector2(0.5f, 1f);
        _boxRect.pivot = new Vector2(0.5f, 1f);
        _boxRect.anchoredPosition = new Vector2(0f, -24f);
        _boxRect.sizeDelta = new Vector2(1100f, 100f);

        var textObject = new GameObject("Text");
        textObject.transform.SetParent(boxObject.transform, false);

        _text = textObject.AddComponent<Text>();
        _text.font = CreateFont();
        _text.fontSize = 24;
        _text.alignment = TextAnchor.MiddleCenter;
        _text.color = Color.white;
        _text.raycastTarget = false;
        _text.supportRichText = true;
        _text.lineSpacing = 1.05f;
        _text.horizontalOverflow = HorizontalWrapMode.Wrap;
        _text.verticalOverflow = VerticalWrapMode.Overflow;

        var shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
        shadow.useGraphicAlpha = true;

        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 1f);
        textRect.anchorMax = new Vector2(0.5f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = new Vector2(0f, -12f);
        textRect.sizeDelta = new Vector2(1052f, 76f);
    }

    private static Font CreateFont()
    {
        string[] candidates =
        {
            "Georgia",
            "Palatino Linotype",
            "Times New Roman",
            "Segoe UI",
            "Arial"
        };

        foreach (string name in candidates)
        {
            try
            {
                var font = Font.CreateDynamicFontFromOSFont(name, 28);
                if (font != null)
                    return font;
            }
            catch
            {
                // Try the next installed font.
            }
        }

        try
        {
            return Font.GetDefault();
        }
        catch
        {
            return null;
        }
    }

    private static Texture2D CreateBoxTexture()
    {
        const int size = 64;

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var border = new Color(0.91f, 0.93f, 1f, 0.97f);
        var bevel = new Color(0.33f, 0.38f, 0.63f, 0.97f);
        var bottom = new Color(0.07f, 0.26f, 0.70f, 0.96f);
        var top = new Color(0.02f, 0.06f, 0.23f, 0.96f);
        var innerHighlight = new Color(0.33f, 0.49f, 0.88f, 0.96f);

        for (int y = 0; y < size; y++)
        {
            float t = y / (float)(size - 1);
            Color body = Color.Lerp(bottom, top, t);

            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));

                Color color;
                if (edge <= 1)
                    color = border;
                else if (edge <= 3)
                    color = bevel;
                else if (edge <= 6)
                    color = Color.Lerp(body, innerHighlight, 0.18f);
                else
                    color = body;

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return texture;
    }
}
