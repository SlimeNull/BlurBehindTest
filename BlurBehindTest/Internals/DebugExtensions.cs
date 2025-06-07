using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace BlurBehindTest.Internals
{
    internal static class DebugExtensions
    {
        [Conditional("DEBUG")]
        public static void SavePng(this BitmapSource bitmap, string path)
        {
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var dir = Path.GetDirectoryName(path);
            if (dir is { })
            {
                Directory.CreateDirectory(dir);
            }

            using var file = File.Create(path);
            encoder.Save(file);
        }
    }
}
