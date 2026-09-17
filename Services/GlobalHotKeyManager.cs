using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TarkovTracker.Helpers
{
    public class GlobalHotKeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_NONE = 0x0000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        private readonly IntPtr _hWnd;
        private readonly HwndSource? _hwndSource;
        
        // Maps Hotkey ID -> Action callback
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
                throw new InvalidOperationException("Cannot initialize GlobalHotKeyManager before HWND is created. Call this in or after Window.Loaded.");
            }

            _hwndSource = HwndSource.FromHwnd(_hWnd);
            _hwndSource?.AddHook(HwndHook);
        }

        /// <summary>
        /// Registers a global hotkey and binds it directly to an Action.
        /// </summary>
        /// <param name="modifiers">Key modifiers (Control, Alt, Shift, etc.)</param>
        /// <param name="key">The primary key</param>
        /// <param name="action">The Action delegate to execute when triggered</param>
        /// <returns>Unique integer ID, or -1 if registration failed.</returns>
        public int Register(ModifierKeys modifiers, Key key, Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            _currentId++;
            int id = _currentId;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            uint modFlags = MOD_NONE;

            if (modifiers.HasFlag(ModifierKeys.Shift)) modFlags |= MOD_SHIFT;
            if (modifiers.HasFlag(ModifierKeys.Control)) modFlags |= MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Alt)) modFlags |= MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Windows)) modFlags |= MOD_WIN;

            bool success = RegisterHotKey(_hWnd, id, modFlags, vk);
            if (success)
            {
                _hotkeyActions[id] = action;
                return id;
            }

            return -1;
        }

        /// <summary>
        /// Unregisters a single hotkey by its ID.
        /// </summary>
        public bool Unregister(int id)
        {
            if (_hotkeyActions.ContainsKey(id))
            {
                bool success = UnregisterHotKey(_hWnd, id);
                if (success)
                {
                    _hotkeyActions.Remove(id);
                }
                return success;
            }
            return false;
        }

        /// <summary>
        /// Unregisters all registered hotkeys.
        /// </summary>
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
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (_hotkeyActions.TryGetValue(hotkeyId, out var action))
                {
                    action.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            UnregisterAll();
            _hwndSource?.RemoveHook(HwndHook);
            _isDisposed = true;
        }
    }

    public class Hotkeys
    {
        public static readonly Key[] NumpadKeys =
        [
            Key.NumPad0, Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4,
            Key.NumPad5, Key.NumPad6, Key.NumPad7, Key.NumPad8, Key.NumPad9
        ];
    }
}

