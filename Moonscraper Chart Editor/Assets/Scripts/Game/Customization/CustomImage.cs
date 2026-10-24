// See LICENSE in project root for license information.

using System;
using System.IO;
using UnityEngine;

/// <summary>
/// A highway or background image picked in Settings. PNG and JPG load as a normal texture, and GIFs play their
/// animation by swapping the texture's pixels each frame.
/// </summary>
public class CustomImage : IDisposable
{
    public Texture2D texture { get; private set; }

    GifDecoder.Image m_gif;
    int m_frame = 0;
    float m_frameTime = 0;

    public bool animated { get { return m_gif != null && m_gif.frames.Count > 1; } }

    public static CustomImage Load(string path, TextureWrapMode wrapMode)
    {
        byte[] bytes = File.ReadAllBytes(path);
        CustomImage image = new CustomImage();

        if (bytes.Length >= 3 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
        {
            image.m_gif = GifDecoder.Decode(bytes);
            image.texture = new Texture2D(image.m_gif.width, image.m_gif.height, TextureFormat.RGBA32, false);
            image.ShowFrame(0);
        }
        else
        {
            image.texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!image.texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(image.texture);
                throw new FormatException("Not a PNG, JPG or GIF image");
            }
        }

        image.texture.name = Path.GetFileName(path);
        image.texture.wrapMode = wrapMode;
        image.texture.filterMode = FilterMode.Bilinear;
        image.texture.anisoLevel = 4;

        return image;
    }

    /// <summary>
    /// Moves a GIF along to whichever frame should be showing now
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!animated || !texture)
            return;

        m_frameTime += deltaTime * 1000;
        int frame = m_frame;
        int guard = m_gif.frames.Count;     // After a long hitch, skip ahead without looping forever

        while (m_frameTime >= m_gif.frames[frame].delayMs && guard-- > 0)
        {
            m_frameTime -= m_gif.frames[frame].delayMs;
            frame = (frame + 1) % m_gif.frames.Count;
        }

        if (guard <= 0)
            m_frameTime = 0;

        if (frame != m_frame)
            ShowFrame(frame);
    }

    void ShowFrame(int frame)
    {
        m_frame = frame;
        texture.LoadRawTextureData(m_gif.frames[frame].rgba);
        texture.Apply(false);
    }

    public void Dispose()
    {
        if (texture)
            UnityEngine.Object.Destroy(texture);
        texture = null;
        m_gif = null;
    }
}
