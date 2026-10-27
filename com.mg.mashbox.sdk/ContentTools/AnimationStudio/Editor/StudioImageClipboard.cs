using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioImageClipboard
    {
        internal static uint Sequence
        {
            get
            {
#if UNITY_EDITOR_WIN
                return GetClipboardSequenceNumber();
#else
                return 0;
#endif
            }
        }
        internal static bool TryWrite(byte[] png,out string error)
        {
            error=null;
#if !UNITY_EDITOR_WIN
            error="System clipboard export is available on Windows.";return false;
#else
            IntPtr memory=IntPtr.Zero;
            if(!OpenClipboard(GetActiveWindow())){error="The system clipboard is busy.";return false;}
            try
            {
                uint format=RegisterClipboardFormat("PNG");
                memory=GlobalAlloc(2,new UIntPtr((uint)png.Length));if(memory==IntPtr.Zero)throw new InvalidOperationException("Clipboard allocation failed.");
                IntPtr data=GlobalLock(memory);if(data==IntPtr.Zero)throw new InvalidOperationException("Clipboard lock failed.");
                try{Marshal.Copy(png,0,data,png.Length);}finally{GlobalUnlock(memory);}
                if(format==0||!EmptyClipboard()||SetClipboardData(format,memory)==IntPtr.Zero)throw new InvalidOperationException("Could not write clipboard image.");
                memory=IntPtr.Zero;return true;
            }
            catch(Exception e){error=e.Message;return false;}
            finally{if(memory!=IntPtr.Zero)GlobalFree(memory);CloseClipboard();}
#endif
        }
#if UNITY_EDITOR_WIN
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format,IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags,UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
        private const uint CfDib = 8;
        private const uint CfDibV5 = 17;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr newOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string format);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GlobalSize(IntPtr memory);
#endif

        internal static bool TryRead(out Texture2D texture, out string error)
        {
            texture = null;
            error = null;
#if !UNITY_EDITOR_WIN
            error = "Clipboard-image paste is currently supported on Windows. Drag an external PNG into the canvas on this platform.";
            return false;
#else
            if (!OpenClipboard(IntPtr.Zero))
            {
                error = "The Windows clipboard is busy. Try Paste again.";
                return false;
            }

            try
            {
                uint pngFormat = RegisterClipboardFormat("PNG");
                if (pngFormat != 0 && IsClipboardFormatAvailable(pngFormat) &&
                    TryReadBytes(pngFormat, out byte[] pngBytes))
                {
                    var png = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                    {
                        name = "Clipboard Image",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    if (png.LoadImage(pngBytes, false))
                    {
                        texture = png;
                        return true;
                    }
                    UnityEngine.Object.DestroyImmediate(png);
                }

                uint dibFormat = IsClipboardFormatAvailable(CfDibV5) ? CfDibV5 : CfDib;
                if (!IsClipboardFormatAvailable(dibFormat))
                {
                    error = "The clipboard does not currently contain a supported image. Copy a bitmap/PNG, or drag an image file into the canvas.";
                    return false;
                }

                IntPtr handle = GetClipboardData(dibFormat);
                if (handle == IntPtr.Zero)
                {
                    error = "Windows returned an empty clipboard image handle.";
                    return false;
                }
                IntPtr data = GlobalLock(handle);
                if (data == IntPtr.Zero)
                {
                    error = "Windows could not lock the clipboard image.";
                    return false;
                }

                try
                {
                    texture = DecodeDib(data, checked((long)GlobalSize(handle).ToUInt64()), out error);
                    return texture != null;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            catch (Exception exception)
            {
                error = "Could not paste the clipboard image: " + exception.Message;
                return false;
            }
            finally
            {
                CloseClipboard();
            }
#endif
        }

#if UNITY_EDITOR_WIN
        private static bool TryReadBytes(uint format, out byte[] bytes)
        {
            bytes = null;
            IntPtr handle = GetClipboardData(format);
            if (handle == IntPtr.Zero)
                return false;
            IntPtr data = GlobalLock(handle);
            if (data == IntPtr.Zero)
                return false;
            try
            {
                long length = checked((long)GlobalSize(handle).ToUInt64());
                if (length <= 0 || length > int.MaxValue)
                    return false;
                bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                return true;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }

        private static Texture2D DecodeDib(IntPtr data, long dataLength, out string error)
        {
            error = null;
            if (dataLength < 40)
            {
                error = "The clipboard DIB header is incomplete.";
                return null;
            }

            int headerSize = Marshal.ReadInt32(data, 0);
            int width = Marshal.ReadInt32(data, 4);
            int signedHeight = Marshal.ReadInt32(data, 8);
            int bitsPerPixel = Marshal.ReadInt16(data, 14);
            int compression = Marshal.ReadInt32(data, 16);
            int colorCount = Marshal.ReadInt32(data, 32);
            if (width <= 0 || signedHeight == 0 || width > 16384 || Math.Abs((long)signedHeight) > 16384)
            {
                error = "The clipboard bitmap dimensions are invalid or too large.";
                return null;
            }
            if ((bitsPerPixel != 24 && bitsPerPixel != 32) || (compression != 0 && compression != 3))
            {
                error = $"Clipboard bitmap format {bitsPerPixel}-bit/compression {compression} is unsupported. Copy as PNG instead.";
                return null;
            }

            int height = Math.Abs(signedHeight);
            bool bottomUp = signedHeight > 0;
            int paletteEntries = bitsPerPixel <= 8 ? (colorCount > 0 ? colorCount : 1 << bitsPerPixel) : 0;
            int bitMasks = headerSize == 40 && compression == 3 ? 12 : 0;
            int pixelOffset = checked(headerSize + paletteEntries * 4 + bitMasks);
            int rowStride = checked(((width * bitsPerPixel + 31) / 32) * 4);
            long required = (long)pixelOffset + (long)rowStride * height;
            if (pixelOffset < 0 || required > dataLength)
            {
                error = "The clipboard bitmap pixel data is incomplete.";
                return null;
            }

            var pixels = new Color32[width * height];
            bool anyAlpha = false;
            for (int y = 0; y < height; y++)
            {
                int sourceY = bottomUp ? height - 1 - y : y;
                int row = pixelOffset + sourceY * rowStride;
                for (int x = 0; x < width; x++)
                {
                    int offset = row + x * (bitsPerPixel / 8);
                    byte blue = Marshal.ReadByte(data, offset);
                    byte green = Marshal.ReadByte(data, offset + 1);
                    byte red = Marshal.ReadByte(data, offset + 2);
                    byte alpha = bitsPerPixel == 32 ? Marshal.ReadByte(data, offset + 3) : (byte)255;
                    anyAlpha |= alpha != 0;
                    pixels[(height - 1 - y) * width + x] = new Color32(red, green, blue, alpha);
                }
            }
            if (bitsPerPixel == 32 && !anyAlpha)
            {
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i].a = 255;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "Clipboard Image",
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
#endif
    }
}
