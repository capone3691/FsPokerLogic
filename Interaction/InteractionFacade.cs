﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using WindowScrape.Types;

namespace Interaction
{
    public class WindowInfo
    {
        public string Title { get; set; }

        public string TableName { get; set; }

        public Size Size { get; set; }

        public Bitmap Bitmap { get; set; }
    }

    public static class InteractionFacade
    {
        public static Point Focus(string windowTitle)
        {
            HwndObject windowHwnd = HwndObject.GetWindowByTitle(windowTitle);
            if (windowHwnd == null)
                throw new ApplicationException(string.Format("Window '{0}' not found", windowTitle));

            // Bring window to front
            IntPtr realHwnd;
            int loop = 3;
            do
            {
                Win32.SetForegroundWindow(windowHwnd.Hwnd);
                realHwnd = Win32.GetForegroundWindow();
                loop--;
            }
            while (realHwnd != windowHwnd.Hwnd && loop > 0);


            return windowHwnd.Location;
        }    

        public static IEnumerable<WindowInfo> GetWindowList(Size screenSize, Size targetSize, params string[] searchStrings)
        {
            foreach (string searchString in searchStrings)
            {
                foreach (HwndObject window in HwndObject.GetWindows().Where(w => w.Title.StartsWith(searchString)))
                {
                    if (targetSize != window.Size)
                    {
                        HwndObject.GetWindowByTitle(window.Title).Size = targetSize;
                        continue;
                    }

                    var parts = window.Title.Split('-');
                    if (parts.Length < 3)
                    {
                        continue;
                    }

                    Bitmap bitmap = new Bitmap(screenSize.Width, screenSize.Height);
                    Graphics memoryGraphics = Graphics.FromImage(bitmap);
                    IntPtr dc = memoryGraphics.GetHdc();
                    bool success = Win32.PrintWindow(window.Hwnd, dc, 0);
                    memoryGraphics.ReleaseHdc(dc);
                    bitmap.Save("out.bmp");

                    yield return new WindowInfo
                    {
                        Title = window.Title,
                        TableName = parts[2].Trim(),
                        Size = window.Size,
                        Bitmap = bitmap
                    };
                }
            }
        }
    }
}

