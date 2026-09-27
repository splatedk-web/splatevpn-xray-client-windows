using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ZXing;

namespace SplateClientDesktop.Services;

public class QrCodeScannerService
{
    /// <summary>
    /// Captures the full virtual screen and scans for any QR code containing VPN links.
    /// </summary>
    public async Task<string?> ScanFromScreenAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                int left = (int)SystemParameters.VirtualScreenLeft;
                int top = (int)SystemParameters.VirtualScreenTop;
                int width = (int)SystemParameters.VirtualScreenWidth;
                int height = (int)SystemParameters.VirtualScreenHeight;

                if (width <= 0 || height <= 0)
                {
                    width = 1920;
                    height = 1080;
                }

                IntPtr hDesk = GetDesktopWindow();
                IntPtr hdcSrc = GetDC(hDesk);
                IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
                IntPtr hBmp = CreateCompatibleBitmap(hdcSrc, width, height);
                IntPtr hOld = SelectObject(hdcDest, hBmp);

                BitBlt(hdcDest, 0, 0, width, height, hdcSrc, left, top, 0x00CC0020);

                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = width;
                bmi.bmiHeader.biHeight = -height; // top-down bitmap
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0; // BI_RGB

                byte[] rgbValues = new byte[width * height * 4];
                GetDIBits(hdcDest, hBmp, 0, (uint)height, rgbValues, ref bmi, 0);

                SelectObject(hdcDest, hOld);
                DeleteObject(hBmp);
                DeleteDC(hdcDest);
                ReleaseDC(hDesk, hdcSrc);

                var reader = new BarcodeReaderGeneric
                {
                    AutoRotate = true,
                    Options = new ZXing.Common.DecodingOptions
                    {
                        TryHarder = true,
                        PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                    }
                };

                var luminanceSource = new RGBLuminanceSource(rgbValues, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
                var result = reader.Decode(luminanceSource);
                return result?.Text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Screen QR scan error: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// Scans a QR code from an image file path (PNG, JPG, BMP, WEBP).
    /// </summary>
    public async Task<string?> ScanFromFileAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                var uri = new Uri(filePath, UriKind.Absolute);
                var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];

                var formatConverted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int width = formatConverted.PixelWidth;
                int height = formatConverted.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                formatConverted.CopyPixels(pixels, stride, 0);

                var reader = new BarcodeReaderGeneric
                {
                    AutoRotate = true,
                    Options = new ZXing.Common.DecodingOptions
                    {
                        TryHarder = true,
                        PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                    }
                };

                var luminanceSource = new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
                var result = reader.Decode(luminanceSource);
                return result?.Text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"File QR scan error: {ex.Message}");
                return null;
            }
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }
}
