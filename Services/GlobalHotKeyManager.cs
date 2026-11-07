using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TarkovTracker.Services
{
    public sealed class GlobalHotKeyManager : IDisposable
    {
        private const int WmHotKey = 0x0312;
        private const uint ModNone = 0x0000;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly IntPtr _hWnd;
        private readonly HwndSource? _hwndSource;
        private readonly Dictionary<int, Action> _hotkeyActions = new();
        private int _currentId = 9000;
        private bool _isDisposed;

        public GlobalHotKeyManager(Window window)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            _hWnd = new WindowInteropHelper(window).Handle;
            if (_hWnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Cannot initialize GlobalHotKeyManager before HWND is created. Call this in or after Window.SourceInitialized.");
            }

            _hwndSource = HwndSource.FromHwnd(_hWnd);
            _hwndSource?.AddHook(HwndHook);
        }

        public int Register(ModifierKeys modifiers, Key key, Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            _currentId++;
            int id = _currentId;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            uint modFlags = ModNone;

            if (modifiers.HasFlag(ModifierKeys.Shift)) modFlags |= ModShift;
            if (modifiers.HasFlag(ModifierKeys.Control)) modFlags |= ModControl;
            if (modifiers.HasFlag(ModifierKeys.Alt)) modFlags |= ModAlt;
            if (modifiers.HasFlag(ModifierKeys.Windows)) modFlags |= ModWin;

            bool success = RegisterHotKey(_hWnd, id, modFlags, vk);
            if (!success)
                return -1;

            _hotkeyActions[id] = action;
            return id;
        }

        public bool Unregister(int id)
        {
            if (!_hotkeyActions.ContainsKey(id))
                return false;

            bool success = UnregisterHotKey(_hWnd, id);
            if (success)
                _hotkeyActions.Remove(id);

            return success;
        }

        public void UnregisterAll()
        {
            foreach (int id in new List<int>(_hotkeyActions.Keys))
            {
                UnregisterHotKey(_hWnd, id);
            }

            _hotkeyActions.Clear();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotKey)
            {
                int hotkeyId = wParam.ToInt32();
                if (_hotkeyActions.TryGetValue(hotkeyId, out Action? action))
                {
                    action.Invoke();
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            UnregisterAll();
            _hwndSource?.RemoveHook(HwndHook);
            _isDisposed = true;
        }
    }
}
