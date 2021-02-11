﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clowd
{
    public interface IPage : IDisposable
    {
        event EventHandler Closed;
        void Close();
    }

    public interface IVideoCapturePage : IPage
    {
        void Open(Rectangle captureArea);
    }

    public interface IScreenCapturePage : IPage
    {
        void Open(Rectangle captureArea);
    }
}