﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clowd.Utilities
{
    public class ClipboardEx
    {
        private static string[] _knownImageExt = new[] {
            ".png", ".jpg", ".jpeg",".jpe", ".bmp",
            ".gif", ".tif", ".tiff", ".ico" };

        public static BitmapSource GetImage()
        {
            return Clipboard.GetImage() ?? GetImageFromFile();
        }

        public static void SetImage(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                System.Windows.Forms.IDataObject dataObject = new System.Windows.Forms.DataObject();
                dataObject.SetData("PNG", false, ms);
                System.Windows.Forms.Clipboard.SetDataObject(dataObject, false);
            }
        }
        public static void SetImage(BitmapSource bmp)
        {
            Clipboard.Clear();
            AddImage(bmp);
        }

        public static void AddImage(BitmapSource bmp)
        {
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Position = 0;
                System.Windows.Forms.IDataObject dataObject =
                    System.Windows.Forms.Clipboard.GetDataObject() ?? new System.Windows.Forms.DataObject();
                dataObject.SetData(DataFormats.Bitmap, true, (object)bmp);
                dataObject.SetData("PNG", true, ms);
                System.Windows.Forms.Clipboard.SetDataObject(dataObject, false);
            }
        }

        private static BitmapSource GetImageFromFile()
        {
            if (Clipboard.ContainsFileDropList())
            {
                var collection = Clipboard.GetFileDropList();
                if (collection.Count == 1)
                {
                    var file = collection[0];
                    if (_knownImageExt.Any(k => file.EndsWith(k)))
                        return new BitmapImage(new Uri(file));
                }
            }
            return null;
        }
    }
}