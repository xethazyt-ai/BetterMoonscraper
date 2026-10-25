// See LICENSE in project root for license information.

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A color wheel (hue around it, strength from the middle out) with a brightness bar next to it, for picking any
/// color. Built in code; tells onPicked each time the color changes.
/// </summary>
public class ColorWheelPicker : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    const int WheelSize = 128;

    public Action<Color> onPicked;

    RawImage m_wheel;
    RawImage m_brightnessBar;
    RawImage m_marker;
    Image m_preview;
    Texture2D m_barTexture;

    float m_hue = 0.08f, m_saturation = 1, m_value = 1;
    bool m_draggingBar;

    public Color color { get { return Color.HSVToRGB(m_hue, m_saturation, m_value); } }

    // Builds the wheel at (x, y) from the parent's top left: wheel, then the brightness bar, then a preview swatch
    public static ColorWheelPicker Build(RectTransform parent, float x, float y, float size)
    {
        GameObject root = new GameObject("Color Wheel Picker", typeof(RectTransform));
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.SetParent(parent, false);
        SongPropertiesFormatBar.Place(rootRect, x, y, size + 70, size);

        ColorWheelPicker picker = root.AddComponent<ColorWheelPicker>();
        picker.m_wheel = MakeRawImage(rootRect, "Wheel", 0, 0, size, size, MakeWheelTexture());
        picker.m_brightnessBar = MakeRawImage(rootRect, "Brightness", size + 10, 0, 18, size, null);
        picker.m_barTexture = new Texture2D(1, 64, TextureFormat.RGBA32, false);
        picker.m_barTexture.wrapMode = TextureWrapMode.Clamp;
        picker.m_brightnessBar.texture = picker.m_barTexture;
        picker.m_marker = MakeRawImage(picker.m_wheel.rectTransform, "Marker", 0, 0, 8, 8, Texture2D.whiteTexture);
        picker.m_marker.raycastTarget = false;

        GameObject previewObject = new GameObject("Preview", typeof(RectTransform));
        previewObject.transform.SetParent(rootRect, false);
        picker.m_preview = previewObject.AddComponent<Image>();
        picker.m_preview.raycastTarget = false;
        SongPropertiesFormatBar.Place(picker.m_preview.rectTransform, size + 38, 0, 32, 32);

        // The whole picker takes the clicks, and works out which part was clicked
        Image clickArea = root.AddComponent<Image>();
        clickArea.color = new Color(0, 0, 0, 0);

        picker.Refresh(false);
        return picker;
    }

    static RawImage MakeRawImage(RectTransform parent, string name, float x, float y, float width, float height, Texture texture)
    {
        GameObject made = new GameObject(name, typeof(RectTransform));
        made.transform.SetParent(parent, false);
        RawImage image = made.AddComponent<RawImage>();
        image.texture = texture;
        SongPropertiesFormatBar.Place(image.rectTransform, x, y, width, height);
        return image;
    }

    static Texture2D MakeWheelTexture()
    {
        Texture2D texture = new Texture2D(WheelSize, WheelSize, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[WheelSize * WheelSize];
        float radius = WheelSize / 2f;
        for (int py = 0; py < WheelSize; ++py)
        {
            for (int px = 0; px < WheelSize; ++px)
            {
                float dx = (px + 0.5f - radius) / radius;
                float dy = (py + 0.5f - radius) / radius;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                Color pixel = Color.HSVToRGB(HueAt(dx, dy), Mathf.Clamp01(distance), 1);
                pixel.a = Mathf.Clamp01((1 - distance) * radius);   // Smooth edge
                pixels[py * WheelSize + px] = pixel;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    static float HueAt(float dx, float dy)
    {
        float hue = Mathf.Atan2(dy, dx) / (2 * Mathf.PI);
        return hue < 0 ? hue + 1 : hue;
    }

    public void SetColor(Color color)
    {
        Color.RGBToHSV(color, out m_hue, out m_saturation, out m_value);
        Refresh(false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Vector2 inBar;
        m_draggingBar = Local(m_brightnessBar.rectTransform, eventData, out inBar) && m_brightnessBar.rectTransform.rect.Contains(inBar);
        Pick(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        Pick(eventData);
    }

    void Pick(PointerEventData eventData)
    {
        if (m_draggingBar)
        {
            Vector2 local;
            if (!Local(m_brightnessBar.rectTransform, eventData, out local))
                return;

            Rect rect = m_brightnessBar.rectTransform.rect;
            m_value = Mathf.Clamp01((local.y - rect.yMin) / rect.height);
        }
        else
        {
            Vector2 local;
            if (!Local(m_wheel.rectTransform, eventData, out local))
                return;

            Rect rect = m_wheel.rectTransform.rect;
            float dx = (local.x - rect.center.x) / (rect.width / 2);
            float dy = (local.y - rect.center.y) / (rect.height / 2);
            if (dx * dx + dy * dy > 1.44f && eventData.dragging == false)
                return;     // A click well outside the wheel

            m_hue = HueAt(dx, dy);
            m_saturation = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
            if (m_value < 0.05f)
                m_value = 1;    // Picking a hue on black would show nothing
        }

        Refresh(true);
    }

    static bool Local(RectTransform rect, PointerEventData eventData, out Vector2 local)
    {
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out local);
    }

    void Refresh(bool tell)
    {
        Color full = Color.HSVToRGB(m_hue, m_saturation, 1);
        for (int i = 0; i < m_barTexture.height; ++i)
        {
            Color shade = full * ((float)i / (m_barTexture.height - 1));
            shade.a = 1;
            m_barTexture.SetPixel(0, i, shade);
        }
        m_barTexture.Apply();

        m_wheel.color = new Color(m_value, m_value, m_value, 1);    // Darkens the wheel with the brightness
        m_preview.color = color;

        // The marker sits where the color is on the wheel
        Rect rect = m_wheel.rectTransform.rect;
        float angle = m_hue * 2 * Mathf.PI;
        float x = rect.width / 2 + Mathf.Cos(angle) * m_saturation * rect.width / 2 - 4;
        float y = -(rect.height / 2 - Mathf.Sin(angle) * m_saturation * rect.height / 2) + 4;
        m_marker.rectTransform.anchoredPosition = new Vector2(x, y);
        m_marker.color = m_value > 0.5f ? Color.black : Color.white;

        if (tell && onPicked != null)
            onPicked(color);
    }

    void OnDestroy()
    {
        if (m_barTexture)
            Destroy(m_barTexture);
        if (m_wheel && m_wheel.texture)
            Destroy(m_wheel.texture);
    }
}
