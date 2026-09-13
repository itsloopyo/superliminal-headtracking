using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SuperliminalHeadTracking.Core
{
    /// <summary>
    /// Centres the game's window on the work area of the monitor it is already on
    /// whenever Superliminal is running windowed.
    ///
    /// The game never places its own window: PlayerSettingsManager.SetResolution is
    /// a bare Screen.SetResolution and SliderOptionManager_TEMP.OnFullScreenChanged
    /// hands it Screen.width/height, so where the window lands when it leaves
    /// fullscreen or changes resolution is whatever Unity leaves behind.
    ///
    /// Keyed to a change of (width, height, fullscreen) rather than to the window
    /// rect, so a window the player has dragged somewhere deliberate is left where
    /// they put it. Fullscreen and borderless fall out of the same path rather than
    /// needing a case of their own: a window that fills the work area is left alone.
    /// </summary>
    public class WindowPlacement
    {
        private const float PollInterval = 0.25f;

        // The window is up well before Unity has finished sizing and placing it, and
        // when it stops moving has not been measured here, so the wait is on a rect
        // that has held still rather than on a fixed delay that would be a guess.
        private const int SettlePolls = 2;
        private const int MaxPolls = 40;

        // Slack, so a window that is centred to the eye but off by a pixel of integer
        // rounding is left alone rather than moved and logged as a fix.
        private const int Tolerance = 2;

        private readonly Action<string> _log;

        private float _nextPollTime;
        private int _modeWidth = -1;
        private int _modeHeight = -1;
        private bool _modeFullScreen;
        private bool _modeHandled;
        private int _polls;
        private int _stablePolls;
        private bool _hasLastRect;
        private bool _sawWindow;
        private NativeRect _lastRect;

        public WindowPlacement(Action<string> log)
        {
            _log = log;
        }

        public void Update()
        {
            if (Time.realtimeSinceStartup < _nextPollTime) return;
            _nextPollTime = Time.realtimeSinceStartup + PollInterval;

            int width = Screen.width;
            int height = Screen.height;
            bool fullScreen = Screen.fullScreen;

            if (width != _modeWidth || height != _modeHeight || fullScreen != _modeFullScreen)
            {
                _modeWidth = width;
                _modeHeight = height;
                _modeFullScreen = fullScreen;
                _modeHandled = false;
                _polls = 0;
                _stablePolls = 0;
                _hasLastRect = false;
                _sawWindow = false;
            }

            if (_modeHandled) return;

            if (++_polls > MaxPolls)
            {
                _modeHandled = true;
                // The two ways this times out need different fixes, so they say
                // different things. Never matching a window points at the size floor or
                // the owner/PID filter (a BepInEx console is a second top-level window
                // of this process); a window that never settles points at the timing.
                _log(_sawWindow
                    ? "window: rect never held still within "
                      + (MaxPolls * PollInterval).ToString("F0") + "s, leaving placement alone"
                    : "window: no game window matched within "
                      + (MaxPolls * PollInterval).ToString("F0") + "s, leaving placement alone");
                return;
            }

            IntPtr window = FindGameWindow();
            NativeRect rect;
            if (window == IntPtr.Zero || !GetWindowRect(window, out rect))
            {
                _hasLastRect = false;
                _stablePolls = 0;
                return;
            }

            _sawWindow = true;

            if (_hasLastRect && SameRect(rect, _lastRect))
            {
                if (++_stablePolls < SettlePolls) return;
                _modeHandled = true;
                CenterUnlessAlready(window, rect);
                return;
            }

            _lastRect = rect;
            _hasLastRect = true;
            _stablePolls = 0;
        }

        private void CenterUnlessAlready(IntPtr window, NativeRect rect)
        {
            MonitorInfo info = MonitorInfo.Create();
            if (!GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref info))
            {
                _log("window: GetMonitorInfo failed: " + Marshal.GetLastWin32Error());
                return;
            }

            int windowWidth = rect.right - rect.left;
            int windowHeight = rect.bottom - rect.top;
            int workWidth = info.work.right - info.work.left;
            int workHeight = info.work.bottom - info.work.top;

            if (windowWidth >= workWidth || windowHeight >= workHeight)
            {
                _log("window: " + windowWidth + "x" + windowHeight + " fills work area "
                     + workWidth + "x" + workHeight + ", leaving in place");
                return;
            }

            // Either reading counts as centred. Unity centres on the monitor where this
            // centres on the work area, and the two differ by half the taskbar; moving a
            // window that is already centred trades a visible jump for nothing.
            if (IsCenteredOn(rect, info.work) || IsCenteredOn(rect, info.monitor))
            {
                _log("window: " + windowWidth + "x" + windowHeight + " at (" + rect.left + ", "
                     + rect.top + ") is already centred, leaving it alone");
                return;
            }

            int x = CenteredOrigin(info.work.left, workWidth, windowWidth);
            int y = CenteredOrigin(info.work.top, workHeight, windowHeight);

            // The work area, not the monitor bounds: centring against the full monitor
            // puts the title bar behind a top-docked taskbar, and the window cannot then
            // be dragged back out.
            if (!SetWindowPos(window, IntPtr.Zero, x, y, 0, 0,
                    SwpNoSize | SwpNoZOrder | SwpNoActivate))
            {
                _log("window: SetWindowPos failed: " + Marshal.GetLastWin32Error());
                return;
            }

            _log("window: centred " + windowWidth + "x" + windowHeight + " window at ("
                 + x + ", " + y + ") on work area " + workWidth + "x" + workHeight);
        }

        private static bool SameRect(NativeRect a, NativeRect b)
        {
            return a.left == b.left && a.top == b.top
                   && a.right == b.right && a.bottom == b.bottom;
        }

        private static bool IsCenteredOn(NativeRect window, NativeRect area)
        {
            int dx = window.left - CenteredOrigin(area.left, area.right - area.left,
                         window.right - window.left);
            int dy = window.top - CenteredOrigin(area.top, area.bottom - area.top,
                         window.bottom - window.top);
            return Math.Abs(dx) <= Tolerance && Math.Abs(dy) <= Tolerance;
        }

        private static int CenteredOrigin(int areaStart, int areaExtent, int windowExtent)
        {
            return areaStart + (areaExtent - windowExtent) / 2;
        }

        /// <summary>
        /// This process's main top-level window: visible, not an owned pop-up, at least
        /// 200x200, and not the console. The size floor is what separates the game
        /// window from the splash, tooltip and message-only windows Unity creates
        /// alongside it, all of which are visible and unowned too.
        ///
        /// The console has to be excluded by handle because it passes every other test.
        /// The shipped launcher-manifest.json seeds BepInEx's [Logging.Console] on, so
        /// an ordinary install runs with a second top-level window that is visible,
        /// unowned and far larger than the floor - and picking it would centre the
        /// console while leaving the game where Unity dropped it, with the log line
        /// claiming the game window had been moved.
        /// </summary>
        private static IntPtr FindGameWindow()
        {
            s_processId = GetCurrentProcessId();
            s_consoleWindow = GetConsoleWindow();
            s_found = IntPtr.Zero;
            EnumWindows(PickGameWindowDelegate, IntPtr.Zero);
            return s_found;
        }

        private static bool PickGameWindow(IntPtr window, IntPtr param)
        {
            // Zero when no console is attached, which never equals an enumerated window.
            if (window == s_consoleWindow) return true;

            uint pid;
            GetWindowThreadProcessId(window, out pid);
            if (pid != s_processId) return true;
            if (!IsWindowVisible(window)) return true;
            if (GetWindow(window, GwOwner) != IntPtr.Zero) return true;

            NativeRect rect;
            if (!GetWindowRect(window, out rect)) return true;
            if (rect.right - rect.left < 200 || rect.bottom - rect.top < 200) return true;

            s_found = window;
            return false;
        }

        // EnumWindows runs synchronously, so the enumeration state is a pair of statics
        // rather than a marshalled lParam. The delegate is held in a static of its own
        // because a delegate created at the call site is collectable while native code
        // still holds the thunk.
        private static readonly EnumWindowsProc PickGameWindowDelegate = PickGameWindow;
        private static uint s_processId;
        private static IntPtr s_consoleWindow;
        private static IntPtr s_found;

        private const uint GwOwner = 4;
        private const uint MonitorDefaultToNearest = 2;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int size;
            public NativeRect monitor;
            public NativeRect work;
            public uint flags;

            public static MonitorInfo Create()
            {
                MonitorInfo info = new MonitorInfo();
                info.size = Marshal.SizeOf(typeof(MonitorInfo));
                return info;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW",
            SetLastError = true)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
    }
}
