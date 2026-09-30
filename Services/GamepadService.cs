using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using HidSharp;

namespace UnifiedGameLauncher.Services;

[Flags]
public enum GamepadButtons : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftTriggerDigital = 0x0400,
    RightTriggerDigital = 0x0800,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000
}

public sealed record GamepadState(bool Connected, GamepadButtons Buttons, short LeftX, short LeftY, byte LeftTrigger, byte RightTrigger)
{
    public bool IsConnected => Connected;
}

public enum GamepadDeviceKind
{
    Unknown,
    Xbox,
    PlayStation
}

public sealed class GamepadService : IDisposable
{
    #region Constants and state

    private const int MaxRawInputPacketBytes = 64 * 1024;
    private const int MaxHidReportBytes = 4096;
    private const uint ERROR_SUCCESS = 0;
    private const uint ERROR_DEVICE_NOT_CONNECTED = 1167;
    private const byte DigitalTriggerThreshold = 48;
    private readonly DispatcherTimer _timer;
    private readonly EventHandler _tickHandler;
    private GamepadState _previous = new(false, GamepadButtons.None, 0, 0, 0, 0);
    private IntPtr _sdlController = IntPtr.Zero;
    private IntPtr _sdlJoystick = IntPtr.Zero;
    private bool _sdlInitialized;
    private int _repeatCooldown;
    private readonly Window? _window;
    private HwndSource? _hwndSource;
    private GamepadState _rawDualSenseState = new(false, GamepadButtons.None, 0, 0, 0, 0);
    private bool _rawInputRegistered;
    private readonly object _hidSync = new();
    private GamepadState _hidDualSenseState = new(false, GamepadButtons.None, 0, 0, 0, 0);
    private bool _hidDualSenseConnected;
    private CancellationTokenSource? _hidCts;
    private Task? _hidTask;

    public event EventHandler<GamepadState>? StateChanged;

    /// <summary>
    /// Família do dispositivo que está fornecendo o estado publicado atualmente.
    /// A janela principal usa esta informação apenas para escolher os ícones de ajuda.
    /// </summary>
    public GamepadDeviceKind ActiveDeviceKind { get; private set; } = GamepadDeviceKind.Unknown;

    #endregion

    #region Initialization and Raw Input

    public GamepadService(Window? window = null)
    {
        _window = window;
        if (_window is not null)
        {
            _window.SourceInitialized += Window_SourceInitialized;
            if (_window.IsInitialized) RegisterRawInput();
        }
        TryActivateDualSenseBluetooth();
        StartDirectHidReader();
        TryInitializeSdl();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _tickHandler = (_, _) => Poll();
        _timer.Tick += _tickHandler;
        _timer.Start();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        RegisterRawInput();
    }

    private void RegisterRawInput()
    {
        if (_rawInputRegistered || _window is null) return;
        try
        {
            var handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero) return;
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);

            var devices = new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = HID_USAGE_PAGE_GENERIC,
                    usUsage = HID_USAGE_GENERIC_GAMEPAD,
                    dwFlags = RIDEV_INPUTSINK,
                    hwndTarget = handle
                },
                new RAWINPUTDEVICE
                {
                    usUsagePage = HID_USAGE_PAGE_GENERIC,
                    usUsage = HID_USAGE_GENERIC_JOYSTICK,
                    dwFlags = RIDEV_INPUTSINK,
                    hwndTarget = handle
                }
            };

            _rawInputRegistered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }
        catch
        {
            _rawInputRegistered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT)
        {
            try
            {
                if (TryReadRawInputDualSense(lParam, out var state))
                {
                    _rawDualSenseState = state;
                    ActiveDeviceKind = GamepadDeviceKind.PlayStation;
                    StateChanged?.Invoke(this, state);
                    // Do not update _previous before StateChanged. WasPressed()
                    // must compare the new state with the state from the
                    // previous polling cycle.
                    handled = false;
                }
            }
            catch { }
        }
        return IntPtr.Zero;
    }

    private bool TryReadRawInputDualSense(IntPtr lParam, out GamepadState state)
    {
        state = new(false, GamepadButtons.None, 0, 0, 0, 0);
        uint size = 0;
        if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == uint.MaxValue ||
            size == 0 || size > MaxRawInputPacketBytes)
            return false;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) != size)
                return false;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.dwType != RIM_TYPEHID) return false;
            if (!IsDualSenseRawDevice(header.hDevice)) return false;

            var hidOffset = Marshal.SizeOf<RAWINPUTHEADER>();
            if (size < hidOffset + 8) return false;
            var sizeHid = Marshal.ReadInt32(IntPtr.Add(buffer, hidOffset));
            var count = Marshal.ReadInt32(IntPtr.Add(buffer, hidOffset + 4));
            if (sizeHid <= 0 || sizeHid > MaxHidReportBytes || count <= 0 || count > 128) return false;
            var dataOffset = hidOffset + 8;
            var available = (int)size - dataOffset;
            if (available < sizeHid) return false;

            var report = new byte[sizeHid];
            Marshal.Copy(IntPtr.Add(buffer, dataOffset), report, 0, sizeHid);
            return TryParseDualSenseReport(report, out state);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private bool IsDualSenseRawDevice(IntPtr device)
    {
        if (device == IntPtr.Zero) return false;
        uint chars = 0;
        var result = GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (result == uint.MaxValue || chars == 0) return false;
        var nameBuffer = Marshal.AllocHGlobal((int)((chars + 1) * 2));
        try
        {
            result = GetRawInputDeviceInfo(device, RIDI_DEVICENAME, nameBuffer, ref chars);
            if (result == uint.MaxValue) return false;
            var name = Marshal.PtrToStringUni(nameBuffer) ?? string.Empty;
            return name.Contains("VID_054C", StringComparison.OrdinalIgnoreCase)
                && (name.Contains("PID_0CE6", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("PID_0DF2", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    #endregion

    #region DualSense report parsing

    private static bool TryParseDualSenseReport(byte[] report, out GamepadState state)
    {
        state = new GamepadState(false, GamepadButtons.None, 0, 0, 0, 0);
        if (report.Length == 0) return false;

        // DualSense Bluetooth extended report 0x31.
        // Layout (including report ID):
        // 0=ID, 1=BT tag, 2..5=sticks, 6..7=triggers,
        // 8=counter, 9=hat+face buttons, 10=shoulders/options/L3/R3,
        // 11=PS/touchpad/mute.
        if (report[0] == 0x31 && report.Length >= 12)
        {
            var lx = report[2];
            var ly = report[3];
            var l2 = report[6];
            var r2 = report[7];
            var hat = report[9];
            var b0 = report[9];
            var b1 = report[10];
            var b2 = report[11];

            var buttons = DecodeDualSenseButtons(hat, b0, b1, b2);
            return BuildDualSenseState(buttons, lx, ly, l2, r2, out state);
        }

        // Some Windows/HID paths expose the DualSense's simplified Bluetooth
        // report 0x01. Depending on the HID layer, the report ID is either
        // present (10 bytes) or stripped (9 bytes).
        // Payload layout: LX, LY, RX, RY, hat+face, buttons1, buttons2, L2, R2.
        int baseIndex;
        if (report.Length >= 10 && report[0] == 0x01)
            baseIndex = 1;
        else if (report.Length == 9)
            baseIndex = 0;
        else
            return false;

        if (baseIndex + 8 >= report.Length) return false;

        var compactLx = report[baseIndex + 0];
        var compactLy = report[baseIndex + 1];
        // In the simplified DualSense report the D-pad and face buttons share
        // ONE byte: low nibble = hat, high nibble = Square/Cross/Circle/Triangle.
        // The following byte is L1/R1/L2/R2/Create/Options/L3/R3.
        var compactButtons0 = report[baseIndex + 4];
        var compactButtons1 = report[baseIndex + 5];
        var compactButtons2 = report[baseIndex + 6];
        var compactL2 = report[baseIndex + 7];
        var compactR2 = report[baseIndex + 8];

        var compactButtons = DecodeDualSenseButtons(
            compactButtons0,
            compactButtons0,
            compactButtons1,
            compactButtons2);
        return BuildDualSenseState(compactButtons, compactLx, compactLy, compactL2, compactR2, out state);
    }

    private static GamepadButtons DecodeDualSenseButtons(byte hat, byte b0, byte b1, byte _b2)
    {
        GamepadButtons buttons = GamepadButtons.None;
        switch (hat & 0x0F)
        {
            case 0: buttons |= GamepadButtons.DPadUp; break;
            case 1: buttons |= GamepadButtons.DPadUp | GamepadButtons.DPadRight; break;
            case 2: buttons |= GamepadButtons.DPadRight; break;
            case 3: buttons |= GamepadButtons.DPadDown | GamepadButtons.DPadRight; break;
            case 4: buttons |= GamepadButtons.DPadDown; break;
            case 5: buttons |= GamepadButtons.DPadDown | GamepadButtons.DPadLeft; break;
            case 6: buttons |= GamepadButtons.DPadLeft; break;
            case 7: buttons |= GamepadButtons.DPadUp | GamepadButtons.DPadLeft; break;
        }
        // DualSense buttons0: low nibble is the D-pad hat;
        // high nibble is Square/Cross/Circle/Triangle.
        if ((b0 & 0x10) != 0) buttons |= GamepadButtons.X; // Square
        if ((b0 & 0x20) != 0) buttons |= GamepadButtons.A; // Cross
        if ((b0 & 0x40) != 0) buttons |= GamepadButtons.B; // Circle
        if ((b0 & 0x80) != 0) buttons |= GamepadButtons.Y; // Triangle

        // DualSense buttons1: L1/R1/L2/R2/Create/Options/L3/R3.
        if ((b1 & 0x01) != 0) buttons |= GamepadButtons.LeftShoulder;
        if ((b1 & 0x02) != 0) buttons |= GamepadButtons.RightShoulder;
        if ((b1 & 0x04) != 0) buttons |= GamepadButtons.LeftTriggerDigital;
        if ((b1 & 0x08) != 0) buttons |= GamepadButtons.RightTriggerDigital;
        if ((b1 & 0x10) != 0) buttons |= GamepadButtons.Back;  // Create
        if ((b1 & 0x20) != 0) buttons |= GamepadButtons.Start; // Options
        if ((b1 & 0x40) != 0) buttons |= GamepadButtons.LeftThumb;
        if ((b1 & 0x80) != 0) buttons |= GamepadButtons.RightThumb;
        // DualSense buttons2 contains PS/Home, touchpad click and mute.
        // No launcher action is assigned to those controls yet.
        return buttons;
    }

    private static GamepadState MakeDualSenseState(GamepadButtons buttons, byte lx, byte ly, byte l2, byte r2)
    {
        // WPF navigation uses positive Y for UP. HID reports use positive Y
        // for DOWN, so invert the Y axis when converting the DualSense state.
        // DualSense HID axes are unsigned 8-bit values centered around 128.
        // Map them to the signed Int16 range without overflowing at the extremes.
        // Y is inverted because HID convention is +Y downward while the launcher
        // uses +Y upward for navigation.
        var x = (short)Math.Clamp((int)Math.Round((lx - 128) * 32767.0 / 128.0), -32768, 32767);
        var y = (short)Math.Clamp((int)Math.Round((128 - ly) * 32767.0 / 128.0), -32768, 32767);

        return new GamepadState(true, buttons, x, y, l2, r2);
    }

    private static bool BuildDualSenseState(GamepadButtons buttons, byte lx, byte ly, byte l2, byte r2, out GamepadState state)
    {
        state = MakeDualSenseState(buttons, lx, ly, l2, r2);
        return true;
    }

    #endregion

    #region Direct HID and Bluetooth

    private void StartDirectHidReader()
    {
        try
        {
            _hidCts = new CancellationTokenSource();
            _hidTask = Task.Run(() => DirectHidReaderLoop(_hidCts.Token));
        }
        catch
        {
            _hidCts = null;
            _hidTask = null;
        }
    }

    private void DirectHidReaderLoop(CancellationToken token)
    {
        HidStream? stream = null;
        HidDevice? device = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (stream is null)
                {
                    device = DeviceList.Local.GetHidDevices(0x054C, 0x0CE6).FirstOrDefault()
                        ?? DeviceList.Local.GetHidDevices(0x054C, 0x0DF2).FirstOrDefault();

                    if (device is null || !device.TryOpen(out stream))
                    {
                        lock (_hidSync)
                        {
                            _hidDualSenseConnected = false;
                            _hidDualSenseState = new(false, GamepadButtons.None, 0, 0, 0, 0);
                        }
                        token.WaitHandle.WaitOne(500);
                        continue;
                    }

                    stream.ReadTimeout = 500;
                    TryActivateFeatureReport(device, stream);
                }

                try
                {
                    var activeDevice = device ?? throw new InvalidOperationException("Dispositivo HID indisponível.");
#pragma warning disable CS0612 // HidSharp 2.6 mantém estas propriedades para compatibilidade.
                    var declaredInputLength = activeDevice.MaxInputReportLength;
#pragma warning restore CS0612
                    if (declaredInputLength <= 0 || declaredInputLength > MaxHidReportBytes)
                        throw new InvalidOperationException("Tamanho de relatório HID inválido.");
                    var report = new byte[Math.Max(declaredInputLength, 128)];
                    var count = stream.Read(report, 0, report.Length);
                    if (count <= 0) continue;
                    if (count != report.Length) Array.Resize(ref report, count);

                    if (TryParseDualSenseReport(report, out var state))
                    {
                        lock (_hidSync)
                        {
                            _hidDualSenseConnected = true;
                            _hidDualSenseState = state;
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // Keep the HID stream alive while waiting for the next report.
                }
                catch
                {
                    try { stream.Dispose(); } catch { }
                    stream = null;
                    device = null;
                    lock (_hidSync)
                    {
                        _hidDualSenseConnected = false;
                        _hidDualSenseState = new(false, GamepadButtons.None, 0, 0, 0, 0);
                    }
                    token.WaitHandle.WaitOne(100);
                }
            }
        }
        catch
        {
            lock (_hidSync)
            {
                _hidDualSenseConnected = false;
                _hidDualSenseState = new(false, GamepadButtons.None, 0, 0, 0, 0);
            }
        }
        finally
        {
            try { stream?.Dispose(); } catch { }
        }
    }

    private static void TryActivateFeatureReport(HidDevice device, HidStream stream)
    {
        try
        {
            // DualSense Bluetooth exposes feature report 0x05. Reading it is
            // the documented host-side trigger that makes the controller emit
            // the extended 0x31 input report instead of only report 0x01.
#pragma warning disable CS0612 // HidSharp 2.6 mantém esta propriedade para compatibilidade.
            var declaredFeatureLength = device.MaxFeatureReportLength;
#pragma warning restore CS0612
            if (declaredFeatureLength <= 0 || declaredFeatureLength > MaxHidReportBytes) return;
            var feature = new byte[Math.Max(41, declaredFeatureLength)];
            feature[0] = 0x05;
            stream.GetFeature(feature);
        }
        catch
        {
            // Some Windows HID paths do not expose the feature endpoint; input
            // reports can still be consumed directly.
        }
    }

    private void TryActivateDualSenseBluetooth()
    {
        // A real DualSense connected over Bluetooth may initially expose only
        // the simple HID input report 0x01. Reading Feature Report 0x05 asks
        // the controller to switch to the full Bluetooth input report 0x31.
        // This is intentionally done through the Windows HID API before SDL
        // opens the controller, because Raw Input itself cannot issue HID
        // feature-report requests.
        try
        {
            var hidInterfaceGuid = GUID_DEVINTERFACE_HID;
            var deviceInfoSet = SetupDiGetClassDevs(ref hidInterfaceGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (deviceInfoSet == INVALID_HANDLE_VALUE) return;

            try
            {
                for (uint index = 0; ; index++)
                {
                    var interfaceData = new SP_DEVICE_INTERFACE_DATA
                    { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidInterfaceGuid, index, ref interfaceData))
                        break;

                    uint required = 0;
                    SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                    if (required == 0) continue;

                    var detailBuffer = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, required, ref required, IntPtr.Zero))
                            continue;

                        var pathPtr = IntPtr.Add(detailBuffer, IntPtr.Size == 8 ? 8 : 4);
                        var path = Marshal.PtrToStringUni(pathPtr) ?? string.Empty;
                        if (!IsDualSenseDevicePath(path)) continue;

                        var handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                        if (handle == INVALID_HANDLE_VALUE)
                            continue;

                        try
                        {
                            // 0x05 is a 41-byte feature report including the ID.
                            var feature = new byte[41];
                            feature[0] = 0x05;
                            _ = HidD_GetFeature(handle, feature, feature.Length);
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }
        catch
        {
            // Bluetooth activation is opportunistic; SDL/Raw Input/XInput remain available.
        }
    }

    private static bool IsDualSenseDevicePath(string path)
    {
        return path.Contains("VID_054C", StringComparison.OrdinalIgnoreCase)
            && (path.Contains("PID_0CE6", StringComparison.OrdinalIgnoreCase)
                || path.Contains("PID_0DF2", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region SDL controller support

    private void TryInitializeSdl()
    {
        try
        {
            // DualSense: force the Windows HIDAPI path and keep raw-input/WGI
            // fallbacks available. SDL documents the PS5 HIDAPI hint as the
            // switch that enables native DualSense handling.
            SDL_SetHint("SDL_JOYSTICK_HIDAPI", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS4", "1");
            SDL_SetHint("SDL_JOYSTICK_RAWINPUT", "1");
            SDL_SetHint("SDL_JOYSTICK_WGI", "1");
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (SDL_Init(SDL_INIT_JOYSTICK | SDL_INIT_GAMECONTROLLER) < 0) return;
            _sdlInitialized = true;
            RefreshSdlController();
        }
        catch
        {
            _sdlInitialized = false;
        }
    }

    private void RefreshSdlController()
    {
        if (!_sdlInitialized) return;
        if (_sdlController != IntPtr.Zero)
        {
            try { SDL_GameControllerClose(_sdlController); } catch { }
            _sdlController = IntPtr.Zero;
        }
        if (_sdlJoystick != IntPtr.Zero)
        {
            try { SDL_JoystickClose(_sdlJoystick); } catch { }
            _sdlJoystick = IntPtr.Zero;
        }

        try
        {
            var count = SDL_NumJoysticks();

            // Prioriza o DualSense físico quando o Windows/Steam expõe mais de um
            // dispositivo (por exemplo, um controle virtual criado pelo Steam Input).
            for (var i = 0; i < count; i++)
            {
                if (SDL_IsGameController(i) != 0 && IsDualSenseName(GetSdlJoystickName(i)))
                {
                    var controller = SDL_GameControllerOpen(i);
                    if (controller != IntPtr.Zero)
                    {
                        _sdlController = controller;
                        break;
                    }
                }
            }

            if (_sdlController == IntPtr.Zero)
            {
                for (var i = 0; i < count; i++)
                {
                    if (SDL_IsGameController(i) != 0)
                    {
                        var controller = SDL_GameControllerOpen(i);
                        if (controller != IntPtr.Zero)
                        {
                            _sdlController = controller;
                            break;
                        }
                    }
                }
            }

            // Fallback para HID bruto. Também prioriza o DualSense para não pegar
            // primeiro um dispositivo virtual criado pelo Steam Input.
            if (_sdlController == IntPtr.Zero)
            {
                for (var i = 0; i < count; i++)
                {
                    var name = GetSdlJoystickName(i);
                    if (!IsDualSenseName(name)) continue;
                    var joystick = SDL_JoystickOpen(i);
                    if (joystick != IntPtr.Zero)
                    {
                        _sdlJoystick = joystick;
                        break;
                    }
                }
            }

            if (_sdlController == IntPtr.Zero && _sdlJoystick == IntPtr.Zero)
            {
                for (var i = 0; i < count; i++)
                {
                    var joystick = SDL_JoystickOpen(i);
                    if (joystick != IntPtr.Zero)
                    {
                        _sdlJoystick = joystick;
                        break;
                    }
                }
            }
        }
        catch { }
    }

    #endregion

    #region Polling and state publication

    private void Poll()
    {
        // Direct HID is the primary path. HidSharp opens the physical Sony HID
        // interface directly and works with both USB and Bluetooth Classic HID.
        lock (_hidSync)
        {
            if (_hidDualSenseConnected)
            {
                var direct = _hidDualSenseState;
                ActiveDeviceKind = GamepadDeviceKind.PlayStation;
                StateChanged?.Invoke(this, direct);
                _previous = direct;
                return;
            }
        }

        // Raw Input remains as a Windows fallback.
        if (_rawDualSenseState.IsConnected)
        {
            ActiveDeviceKind = GamepadDeviceKind.PlayStation;
            StateChanged?.Invoke(this, _rawDualSenseState);
            _previous = _rawDualSenseState;
            return;
        }

        // Prefer a real DualSense exposed by SDL over XInput. Steam Input can
        // create a virtual Xbox/XInput device; if XInput is checked first, that
        // virtual device can hide the physical DualSense from the launcher.
        if (_sdlInitialized)
        {
            if (_sdlController == IntPtr.Zero && _sdlJoystick == IntPtr.Zero)
                RefreshSdlController();

            if (IsSdlDualSenseOpen())
            {
                try
                {
                    SDL_PumpEvents();
                    var current = _sdlController != IntPtr.Zero
                        ? ReadSdlState(_sdlController)
                        : ReadRawJoystickState(_sdlJoystick);
                    ActiveDeviceKind = GamepadDeviceKind.PlayStation;
                    StateChanged?.Invoke(this, current);
                    _previous = current;
                    return;
                }
                catch
                {
                    CloseSdlDevices();
                }
            }
        }

        // XInput remains available as the fallback for Xbox/XInput controllers.
        var result = XInputGetState(0, out var state);
        if (result == ERROR_SUCCESS)
        {
            var current = WithDigitalTriggers(new GamepadState(
                true,
                (GamepadButtons)state.Gamepad.wButtons,
                state.Gamepad.sThumbLX,
                state.Gamepad.sThumbLY,
                state.Gamepad.bLeftTrigger,
                state.Gamepad.bRightTrigger));
            ActiveDeviceKind = GamepadDeviceKind.Xbox;
            StateChanged?.Invoke(this, current);
            _previous = current;
            return;
        }

        if (result != ERROR_DEVICE_NOT_CONNECTED) return;

        if (!_sdlInitialized)
        {
            PublishDisconnectedIfNeeded();
            return;
        }

        if (_sdlController == IntPtr.Zero && _sdlJoystick == IntPtr.Zero)
            RefreshSdlController();

        if (_sdlController == IntPtr.Zero && _sdlJoystick == IntPtr.Zero)
        {
            PublishDisconnectedIfNeeded();
            return;
        }

        try
        {
            SDL_PumpEvents();
            GamepadState current;

            if (_sdlController != IntPtr.Zero)
            {
                if (SDL_GameControllerGetAttached(_sdlController) == 0)
                {
                    SDL_GameControllerClose(_sdlController);
                    _sdlController = IntPtr.Zero;
                    RefreshSdlController();
                    if (_sdlController == IntPtr.Zero && _sdlJoystick == IntPtr.Zero)
                    {
                        PublishDisconnectedIfNeeded();
                        return;
                    }
                }
                current = _sdlController != IntPtr.Zero
                    ? ReadSdlState(_sdlController)
                    : ReadRawJoystickState(_sdlJoystick);
            }
            else
            {
                if (SDL_JoystickGetAttached(_sdlJoystick) == 0)
                {
                    SDL_JoystickClose(_sdlJoystick);
                    _sdlJoystick = IntPtr.Zero;
                    RefreshSdlController();
                    if (_sdlController == IntPtr.Zero && _sdlJoystick == IntPtr.Zero)
                    {
                        PublishDisconnectedIfNeeded();
                        return;
                    }
                }
                current = _sdlController != IntPtr.Zero
                    ? ReadSdlState(_sdlController)
                    : ReadRawJoystickState(_sdlJoystick);
            }
            ActiveDeviceKind = IsSdlDualSenseOpen()
                ? GamepadDeviceKind.PlayStation
                : GamepadDeviceKind.Xbox;
            StateChanged?.Invoke(this, current);
            _previous = current;
        }
        catch
        {
            _sdlController = IntPtr.Zero;
            PublishDisconnectedIfNeeded();
        }
    }

    private bool IsSdlDualSenseOpen()
    {
        try
        {
            if (_sdlController != IntPtr.Zero)
            {
                if (SDL_GameControllerGetAttached(_sdlController) == 0)
                    return false;

                var joystick = SDL_GameControllerGetJoystick(_sdlController);
                if (joystick == IntPtr.Zero) return false;
                var namePtr = SDL_JoystickName(joystick);
                var name = namePtr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(namePtr) ?? string.Empty;
                return IsDualSenseName(name);
            }

            if (_sdlJoystick != IntPtr.Zero)
            {
                if (SDL_JoystickGetAttached(_sdlJoystick) == 0) return false;
                var namePtr = SDL_JoystickName(_sdlJoystick);
                var name = namePtr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(namePtr) ?? string.Empty;
                return IsDualSenseName(name);
            }
        }
        catch { }
        return false;
    }

    private void CloseSdlDevices()
    {
        if (_sdlController != IntPtr.Zero)
        {
            try { SDL_GameControllerClose(_sdlController); } catch { }
            _sdlController = IntPtr.Zero;
        }
        if (_sdlJoystick != IntPtr.Zero)
        {
            try { SDL_JoystickClose(_sdlJoystick); } catch { }
            _sdlJoystick = IntPtr.Zero;
        }
    }

    private GamepadState ReadSdlState(IntPtr controller)
    {
        GamepadButtons buttons = GamepadButtons.None;
        AddSdlButton(ref buttons, GamepadButtons.A, SDL_CONTROLLER_BUTTON_A, controller);
        AddSdlButton(ref buttons, GamepadButtons.B, SDL_CONTROLLER_BUTTON_B, controller);
        AddSdlButton(ref buttons, GamepadButtons.X, SDL_CONTROLLER_BUTTON_X, controller);
        AddSdlButton(ref buttons, GamepadButtons.Y, SDL_CONTROLLER_BUTTON_Y, controller);
        AddSdlButton(ref buttons, GamepadButtons.Start, SDL_CONTROLLER_BUTTON_START, controller);
        AddSdlButton(ref buttons, GamepadButtons.Back, SDL_CONTROLLER_BUTTON_BACK, controller);
        AddSdlButton(ref buttons, GamepadButtons.LeftShoulder, SDL_CONTROLLER_BUTTON_LEFTSHOULDER, controller);
        AddSdlButton(ref buttons, GamepadButtons.RightShoulder, SDL_CONTROLLER_BUTTON_RIGHTSHOULDER, controller);
        AddSdlButton(ref buttons, GamepadButtons.DPadUp, SDL_CONTROLLER_BUTTON_DPAD_UP, controller);
        AddSdlButton(ref buttons, GamepadButtons.DPadDown, SDL_CONTROLLER_BUTTON_DPAD_DOWN, controller);
        AddSdlButton(ref buttons, GamepadButtons.DPadLeft, SDL_CONTROLLER_BUTTON_DPAD_LEFT, controller);
        AddSdlButton(ref buttons, GamepadButtons.DPadRight, SDL_CONTROLLER_BUTTON_DPAD_RIGHT, controller);
        AddSdlButton(ref buttons, GamepadButtons.LeftThumb, SDL_CONTROLLER_BUTTON_LEFTSTICK, controller);
        AddSdlButton(ref buttons, GamepadButtons.RightThumb, SDL_CONTROLLER_BUTTON_RIGHTSTICK, controller);

        var lx = SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_LEFTX);
        var ly = SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_LEFTY);
        var lt = SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_TRIGGERLEFT);
        var rt = SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_TRIGGERRIGHT);
        return WithDigitalTriggers(new GamepadState(
            true,
            buttons,
            lx,
            ly,
            (byte)Math.Clamp((lt + 32768) / 256, 0, 255),
            (byte)Math.Clamp((rt + 32768) / 256, 0, 255)));
    }

    private static GamepadState WithDigitalTriggers(GamepadState state)
    {
        var buttons = state.Buttons;

        // Xbox/XInput exposes LT/RT as analog axes rather than bits in wButtons.
        // Convert a deliberate trigger press into the same digital flags already
        // produced by the DualSense HID path, so the rest of the launcher can use
        // one input model for LT/RT and L2/R2.
        if (state.LeftTrigger >= DigitalTriggerThreshold)
            buttons |= GamepadButtons.LeftTriggerDigital;
        else
            buttons &= ~GamepadButtons.LeftTriggerDigital;

        if (state.RightTrigger >= DigitalTriggerThreshold)
            buttons |= GamepadButtons.RightTriggerDigital;
        else
            buttons &= ~GamepadButtons.RightTriggerDigital;

        return state with { Buttons = buttons };
    }

    private static void AddSdlButton(ref GamepadButtons result, GamepadButtons mapped, int sdlButton, IntPtr controller)
    {
        if (SDL_GameControllerGetButton(controller, sdlButton) != 0) result |= mapped;
    }

    private static GamepadState ReadRawJoystickState(IntPtr joystick)
    {
        if (joystick == IntPtr.Zero)
            return new GamepadState(false, GamepadButtons.None, 0, 0, 0, 0);

        GamepadButtons buttons = GamepadButtons.None;
        AddRawButton(ref buttons, GamepadButtons.A, 0, joystick);       // Cross
        AddRawButton(ref buttons, GamepadButtons.B, 1, joystick);       // Circle
        AddRawButton(ref buttons, GamepadButtons.X, 2, joystick);       // Square
        AddRawButton(ref buttons, GamepadButtons.Y, 3, joystick);       // Triangle
        AddRawButton(ref buttons, GamepadButtons.LeftShoulder, 4, joystick);
        AddRawButton(ref buttons, GamepadButtons.RightShoulder, 5, joystick);
        AddRawButton(ref buttons, GamepadButtons.Start, 9, joystick);   // Options
        AddRawButton(ref buttons, GamepadButtons.Back, 8, joystick);    // Create/Share
        AddRawButton(ref buttons, GamepadButtons.LeftThumb, 10, joystick);
        AddRawButton(ref buttons, GamepadButtons.RightThumb, 11, joystick);
        AddRawButton(ref buttons, GamepadButtons.DPadUp, 12, joystick);
        AddRawButton(ref buttons, GamepadButtons.DPadDown, 13, joystick);
        AddRawButton(ref buttons, GamepadButtons.DPadLeft, 14, joystick);
        AddRawButton(ref buttons, GamepadButtons.DPadRight, 15, joystick);

        var hat = SDL_JoystickGetHat(joystick, 0);
        if ((hat & SDL_HAT_UP) != 0) buttons |= GamepadButtons.DPadUp;
        if ((hat & SDL_HAT_DOWN) != 0) buttons |= GamepadButtons.DPadDown;
        if ((hat & SDL_HAT_LEFT) != 0) buttons |= GamepadButtons.DPadLeft;
        if ((hat & SDL_HAT_RIGHT) != 0) buttons |= GamepadButtons.DPadRight;

        var lx = SDL_JoystickGetAxis(joystick, 0);
        var ly = SDL_JoystickGetAxis(joystick, 1);
        var lt = SDL_JoystickGetAxis(joystick, 4);
        var rt = SDL_JoystickGetAxis(joystick, 5);
        return WithDigitalTriggers(new GamepadState(
            true,
            buttons,
            lx,
            ly,
            (byte)Math.Clamp((lt + 32768) / 256, 0, 255),
            (byte)Math.Clamp((rt + 32768) / 256, 0, 255)));
    }

    private static void AddRawButton(ref GamepadButtons result, GamepadButtons mapped, int button, IntPtr joystick)
    {
        if (SDL_JoystickGetButton(joystick, button) != 0) result |= mapped;
    }

    private static string GetSdlJoystickName(int index)
    {
        try
        {
            var ptr = SDL_JoystickNameForIndex(index);
            return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static bool IsDualSenseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PS5", StringComparison.OrdinalIgnoreCase);
    }

    private void PublishDisconnectedIfNeeded()
    {
        ActiveDeviceKind = GamepadDeviceKind.Unknown;
        if (_previous.IsConnected)
        {
            _previous = new(false, GamepadButtons.None, 0, 0, 0, 0);
            StateChanged?.Invoke(this, _previous);
        }
    }

    #endregion

    #region Public navigation helpers and disposal

    public bool WasPressed(GamepadButtons button, GamepadState current)
    {
        return current.Buttons.HasFlag(button) && !_previous.Buttons.HasFlag(button);
    }

    public bool IsNavigationPressed(GamepadButtons button, GamepadState current)
    {
        if (WasPressed(button, current)) { _repeatCooldown = 5; return true; }
        if (!current.Buttons.HasFlag(button)) return false;
        if (_repeatCooldown > 0) { _repeatCooldown--; return false; }
        _repeatCooldown = 3;
        return true;
    }

    public void Dispose()
    {
        if (_window is not null) _window.SourceInitialized -= Window_SourceInitialized;
        try { _hwndSource?.RemoveHook(WndProc); } catch { }
        _hwndSource = null;
        _rawInputRegistered = false;
        try { _hidCts?.Cancel(); } catch { }
        try { _hidTask?.Wait(800); } catch { }
        try { _hidCts?.Dispose(); } catch { }
        _hidCts = null;
        _hidTask = null;
        _timer.Stop();
        _timer.Tick -= _tickHandler;
        if (_sdlController != IntPtr.Zero)
        {
            try { SDL_GameControllerClose(_sdlController); } catch { }
            _sdlController = IntPtr.Zero;
        }
        if (_sdlJoystick != IntPtr.Zero)
        {
            try { SDL_JoystickClose(_sdlJoystick); } catch { }
            _sdlJoystick = IntPtr.Zero;
        }
        if (_sdlInitialized)
        {
            try { SDL_Quit(); } catch { }
            _sdlInitialized = false;
        }
    }

    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIM_TYPEHID = 2;
    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_JOYSTICK = 0x04;
    private const ushort HID_USAGE_GENERIC_GAMEPAD = 0x05;
    private const uint RIDEV_INPUTSINK = 0x00000100;

    #endregion

    #region Win32 Raw Input and XInput interop

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    #endregion

    #region Win32 HID device discovery interop

    private static readonly Guid GUID_DEVINTERFACE_HID = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, ref uint RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(IntPtr HidDeviceObject, [Out] byte[] ReportBuffer, int ReportBufferLength);

    #endregion

    #region SDL native interop

    private const uint SDL_INIT_JOYSTICK = 0x00000200;
    private const uint SDL_INIT_GAMECONTROLLER = 0x00002000;
    private const int SDL_CONTROLLER_BUTTON_A = 0;
    private const int SDL_CONTROLLER_BUTTON_B = 1;
    private const int SDL_CONTROLLER_BUTTON_X = 2;
    private const int SDL_CONTROLLER_BUTTON_Y = 3;
    private const int SDL_CONTROLLER_BUTTON_BACK = 4;
    private const int SDL_CONTROLLER_BUTTON_START = 6;
    private const int SDL_CONTROLLER_BUTTON_LEFTSTICK = 7;
    private const int SDL_CONTROLLER_BUTTON_RIGHTSTICK = 8;
    private const int SDL_CONTROLLER_BUTTON_LEFTSHOULDER = 9;
    private const int SDL_CONTROLLER_BUTTON_RIGHTSHOULDER = 10;
    private const int SDL_CONTROLLER_BUTTON_DPAD_UP = 11;
    private const int SDL_CONTROLLER_BUTTON_DPAD_DOWN = 12;
    private const int SDL_CONTROLLER_BUTTON_DPAD_LEFT = 13;
    private const int SDL_CONTROLLER_BUTTON_DPAD_RIGHT = 14;
    private const int SDL_CONTROLLER_AXIS_LEFTX = 0;
    private const int SDL_CONTROLLER_AXIS_LEFTY = 1;
    private const int SDL_CONTROLLER_AXIS_TRIGGERLEFT = 4;
    private const int SDL_CONTROLLER_AXIS_TRIGGERRIGHT = 5;
    private const byte SDL_HAT_UP = 0x01;
    private const byte SDL_HAT_RIGHT = 0x02;
    private const byte SDL_HAT_DOWN = 0x04;
    private const byte SDL_HAT_LEFT = 0x08;

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_Init(uint flags);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_Quit();
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_PumpEvents();
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_NumJoysticks();
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_JoystickNameForIndex(int joystickIndex);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_IsGameController(int joystickIndex);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GameControllerOpen(int joystickIndex);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GameControllerGetJoystick(IntPtr controller);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GameControllerClose(IntPtr controller);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_JoystickOpen(int joystickIndex);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_JoystickClose(IntPtr joystick);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_JoystickName(IntPtr joystick);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_JoystickGetAttached(IntPtr joystick);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte SDL_JoystickGetButton(IntPtr joystick, int button);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte SDL_JoystickGetHat(IntPtr joystick, int hat);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern short SDL_JoystickGetAxis(IntPtr joystick, int axis);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GameControllerGetAttached(IntPtr controller);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte SDL_GameControllerGetButton(IntPtr controller, int button);
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern short SDL_GameControllerGetAxis(IntPtr controller, int axis);

    #endregion
}
