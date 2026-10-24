// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;

/// <summary>
/// Reads GIF files into full frames, since Unity can only load PNG and JPG by itself. Clone Hero accepts animated
/// GIFs as highways and backgrounds, so this lets the same files animate here.
/// </summary>
public static class GifDecoder
{
    public class Frame
    {
        public byte[] rgba;     // width * height * 4, rows bottom to top like Unity textures
        public int delayMs;
    }

    public class Image
    {
        public int width, height;
        public List<Frame> frames = new List<Frame>();
    }

    public const int MinDelayMs = 20;
    const int DefaultDelayMs = 100;     // Browsers use this for a delay of 0 or 1 hundredths, so GIFs are made for it

    /// <summary>
    /// Decodes every frame, already composited the way a browser shows it. Stops adding frames once they'd take more
    /// than maxBytes, so a huge GIF still shows (just a shorter loop) instead of eating all the memory.
    /// </summary>
    public static Image Decode(byte[] data, long maxBytes = 256L * 1024 * 1024)
    {
        if (data == null || data.Length < 13 || data[0] != 'G' || data[1] != 'I' || data[2] != 'F')
            throw new FormatException("Not a GIF file");

        int pos = 6;
        Image image = new Image();
        image.width = data[pos] | (data[pos + 1] << 8);
        image.height = data[pos + 2] | (data[pos + 3] << 8);
        int packed = data[pos + 4];
        pos += 7;

        if (image.width <= 0 || image.height <= 0 || (long)image.width * image.height > 8192L * 8192)
            throw new FormatException("GIF size is invalid");

        byte[] globalTable = null;
        if ((packed & 0x80) != 0)
        {
            int size = 3 << ((packed & 7) + 1);
            globalTable = Slice(data, pos, size);
            pos += size;
        }

        long frameBytes = (long)image.width * image.height * 4;
        byte[] canvas = new byte[frameBytes];      // Top to bottom while decoding
        byte[] previous = null;

        int delayMs = DefaultDelayMs, disposal = 0, transparentIndex = -1;

        while (pos < data.Length)
        {
            int block = data[pos++];

            if (block == 0x3B)          // Trailer
                break;

            if (block == 0x21)          // Extension
            {
                if (pos >= data.Length)
                    break;
                int label = data[pos++];
                if (label == 0xF9 && pos + 5 < data.Length && data[pos] >= 4)       // Graphic control
                {
                    int flags = data[pos + 1];
                    int delay = (data[pos + 2] | (data[pos + 3] << 8)) * 10;
                    delayMs = delay <= 10 ? DefaultDelayMs : Math.Max(delay, MinDelayMs);
                    disposal = (flags >> 2) & 7;
                    transparentIndex = (flags & 1) != 0 ? data[pos + 4] : -1;
                }
                pos = SkipSubBlocks(data, pos);
                continue;
            }

            if (block != 0x2C)          // Anything else means the file is damaged, so keep what we have
                break;

            if (pos + 9 > data.Length)
                break;

            int left = data[pos] | (data[pos + 1] << 8);
            int top = data[pos + 2] | (data[pos + 3] << 8);
            int w = data[pos + 4] | (data[pos + 5] << 8);
            int h = data[pos + 6] | (data[pos + 7] << 8);
            int imagePacked = data[pos + 8];
            pos += 9;

            byte[] table = globalTable;
            if ((imagePacked & 0x80) != 0)
            {
                int size = 3 << ((imagePacked & 7) + 1);
                table = Slice(data, pos, size);
                pos += size;
            }
            bool interlaced = (imagePacked & 0x40) != 0;

            if (pos >= data.Length)
                break;
            int minCodeSize = data[pos++];
            byte[] indices = DecodeLzw(data, ref pos, minCodeSize, w * h);

            if (disposal == 3)
                previous = (byte[])canvas.Clone();

            if (table != null)
                DrawFrame(canvas, image.width, image.height, indices, table, left, top, w, h, interlaced, transparentIndex);

            if ((image.frames.Count + 1) * frameBytes > maxBytes && image.frames.Count > 0)
                break;

            image.frames.Add(new Frame { rgba = FlipRows(canvas, image.width, image.height), delayMs = delayMs });

            // Get the canvas ready for the next frame
            if (disposal == 2)
                ClearRect(canvas, image.width, image.height, left, top, w, h);
            else if (disposal == 3 && previous != null)
                Buffer.BlockCopy(previous, 0, canvas, 0, canvas.Length);

            delayMs = DefaultDelayMs;
            disposal = 0;
            transparentIndex = -1;
        }

        if (image.frames.Count == 0)
            throw new FormatException("GIF has no frames");

        return image;
    }

    static byte[] Slice(byte[] data, int start, int count)
    {
        byte[] result = new byte[count];
        Buffer.BlockCopy(data, start, result, 0, Math.Max(0, Math.Min(count, data.Length - start)));
        return result;
    }

    static int SkipSubBlocks(byte[] data, int pos)
    {
        while (pos < data.Length)
        {
            int size = data[pos++];
            if (size == 0)
                break;
            pos += size;
        }
        return pos;
    }

    static byte[] DecodeLzw(byte[] data, ref int pos, int minCodeSize, int pixelCount)
    {
        byte[] output = new byte[pixelCount];
        if (minCodeSize < 2 || minCodeSize > 11)
        {
            pos = SkipSubBlocks(data, pos);
            return output;
        }

        const int MaxCodes = 4096;
        short[] prefix = new short[MaxCodes];
        byte[] suffix = new byte[MaxCodes];
        byte[] firstByte = new byte[MaxCodes];
        byte[] stack = new byte[MaxCodes + 1];

        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        for (int i = 0; i < clearCode; ++i)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
            firstByte[i] = (byte)i;
        }

        int codeSize = minCodeSize + 1;
        int nextCode = clearCode + 2;
        int oldCode = -1;
        int written = 0;

        int bitBuffer = 0, bitCount = 0;
        int blockRemaining = 0;
        bool done = false;

        while (!done)
        {
            // Pull in bytes from the data sub-blocks until there are enough bits for a code
            while (bitCount < codeSize)
            {
                if (blockRemaining == 0)
                {
                    if (pos >= data.Length || data[pos] == 0)
                    {
                        done = true;
                        break;
                    }
                    blockRemaining = data[pos++];
                }
                if (pos >= data.Length)
                {
                    done = true;
                    break;
                }
                bitBuffer |= data[pos++] << bitCount;
                bitCount += 8;
                --blockRemaining;
            }
            if (done)
                break;

            int code = bitBuffer & ((1 << codeSize) - 1);
            bitBuffer >>= codeSize;
            bitCount -= codeSize;

            if (code == clearCode)
            {
                codeSize = minCodeSize + 1;
                nextCode = clearCode + 2;
                oldCode = -1;
                continue;
            }
            if (code == endCode)
                break;

            int inCode = code;
            int top = 0;

            if (oldCode == -1)
            {
                if (code >= clearCode)
                    break;      // Damaged
                if (written < pixelCount)
                    output[written++] = (byte)code;
                oldCode = code;
                continue;
            }

            if (code >= nextCode)
            {
                if (code > nextCode)
                    break;      // Damaged
                stack[top++] = firstByte[oldCode];
                code = oldCode;
            }

            while (code >= clearCode)
            {
                stack[top++] = suffix[code];
                code = prefix[code];
            }
            stack[top++] = (byte)code;

            if (nextCode < MaxCodes)
            {
                prefix[nextCode] = (short)oldCode;
                suffix[nextCode] = (byte)code;
                firstByte[nextCode] = firstByte[oldCode];
                ++nextCode;
                if (nextCode == (1 << codeSize) && codeSize < 12)
                    ++codeSize;
            }

            while (top > 0 && written < pixelCount)
                output[written++] = stack[--top];

            oldCode = inCode;
        }

        // Skip whatever is left of the image data, including the terminator
        if (blockRemaining > 0)
            pos += blockRemaining;
        pos = SkipSubBlocks(data, pos);

        return output;
    }

    static void DrawFrame(byte[] canvas, int canvasWidth, int canvasHeight, byte[] indices, byte[] table, int left, int top, int w, int h, bool interlaced, int transparentIndex)
    {
        int colorCount = table.Length / 3;
        int row = 0;
        int pass = 0, step = interlaced ? 8 : 1;

        for (int i = 0; i < h; ++i)
        {
            int y = top + row;
            if (y < canvasHeight)
            {
                for (int x = 0; x < w; ++x)
                {
                    int cx = left + x;
                    if (cx >= canvasWidth)
                        break;

                    int index = indices[i * w + x];
                    if (index == transparentIndex || index >= colorCount)
                        continue;

                    int o = (y * canvasWidth + cx) * 4;
                    canvas[o] = table[index * 3];
                    canvas[o + 1] = table[index * 3 + 1];
                    canvas[o + 2] = table[index * 3 + 2];
                    canvas[o + 3] = 255;
                }
            }

            row += step;
            while (interlaced && row >= h && pass < 3)
            {
                ++pass;
                row = pass == 1 ? 4 : pass == 2 ? 2 : 1;
                step = pass == 1 ? 8 : pass == 2 ? 4 : 2;
            }
        }
    }

    static void ClearRect(byte[] canvas, int canvasWidth, int canvasHeight, int left, int top, int w, int h)
    {
        for (int y = top; y < Math.Min(top + h, canvasHeight); ++y)
        {
            int start = (y * canvasWidth + left) * 4;
            int count = (Math.Min(left + w, canvasWidth) - left) * 4;
            if (count > 0)
                Array.Clear(canvas, start, count);
        }
    }

    static byte[] FlipRows(byte[] canvas, int width, int height)
    {
        byte[] flipped = new byte[canvas.Length];
        int stride = width * 4;
        for (int y = 0; y < height; ++y)
            Buffer.BlockCopy(canvas, y * stride, flipped, (height - 1 - y) * stride, stride);
        return flipped;
    }
}
