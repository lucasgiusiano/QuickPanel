using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuickPanel.Core;
using QuickPanel.Services;

namespace QuickPanel.Overlay;

public partial class FloatingButtonWindow : Window
{
    private readonly OverlayManager _manager;
    private IntPtr _edgeOwner;

    private bool _moveMode;
    private bool _dragging;
    private Point _dragStartMouse;
    private Point _dragStartWindow;

    private readonly DispatcherTimer _autoHideTimer;
    private bool _faded;

    public FloatingButtonWindow(OverlayManager manager)
    {
        _manager = manager;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            MakeToolWindow();
            ApplyEdgeOwner();
        };

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _autoHideTimer.Tick += (_, _) => UpdateAutoHide();
        _autoHideTimer.Start();
        Closed += (_, _) => _autoHideTimer.Stop();
    }

    /// <summary>
    /// Atenúa el botón cuando el cursor está lejos (radio configurable) y lo
    /// restaura al acercarse. Solo activo si AutoHide está habilitado (Pro).
    /// </summary>
    private void UpdateAutoHide()
    {
        if (!SettingsService.Current.AutoHide || _moveMode || _dragging)
        {
            if (_faded) FadeTo(1.0);
            _faded = false;
            return;
        }

        if (!Win32.GetCursorPos(out var p)) return;

        var src = PresentationSource.FromVisual(this);
        double scale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double cx = p.X / scale, cy = p.Y / scale;

        double bx = Left + ActualWidth / 2;
        double by = Top + ActualHeight / 2;
        double dist = Math.Sqrt((cx - bx) * (cx - bx) + (cy - by) * (cy - by));

        const double showRadius = 90; // DIPs alrededor del botón
        bool shouldShow = dist < showRadius;

        if (shouldShow && _faded) { FadeTo(1.0); _faded = false; }
        else if (!shouldShow && !_faded) { FadeTo(0.15); _faded = true; }
    }

    private void FadeTo(double target)
    {
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(250))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public void SetEdgeOwner(IntPtr edgeHwnd)
    {
        _edgeOwner = edgeHwnd;
        if (IsLoaded) ApplyEdgeOwner();
    }

    private void ApplyEdgeOwner()
    {
        if (_edgeOwner == IntPtr.Zero) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Win32.SetWindowLongPtr(hwnd, Win32.GWLP_HWNDPARENT, _edgeOwner);
    }

    private void MakeToolWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex   = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE,
            new IntPtr(ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE));
    }

    public void EnterMoveMode()
    {
        _moveMode = true;
        MoveHalo.Visibility = Visibility.Visible;
    }

    /// <summary>Muestra/oculta el badge de no leídos con el total (0 = oculto, 99+ tope).</summary>
    public void SetBadge(int count)
    {
        if (count <= 0)
        {
            Badge.Visibility = Visibility.Collapsed;
            return;
        }
        BadgeText.Text = count > 99 ? "99+" : count.ToString();
        Badge.Visibility = Visibility.Visible;
    }

    private void ExitMoveMode()
    {
        _moveMode = false;
        MoveHalo.Visibility = Visibility.Collapsed;
        _manager.SaveButtonPositionFromCurrent();
        _manager.Reposition();
    }

    private void Fab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_moveMode)
        {
            _dragging        = true;
            _dragStartMouse  = PointToScreen(e.GetPosition(this));
            _dragStartWindow = new Point(Left, Top);
            Fab.CaptureMouse();
            e.Handled = true;
            return;
        }
        AnimatePress(0.88);
    }

    private void Fab_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        var now = PointToScreen(e.GetPosition(this));
        var src = PresentationSource.FromVisual(this);
        double scale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        Left = _dragStartWindow.X + (now.X - _dragStartMouse.X) / scale;
        Top  = _dragStartWindow.Y + (now.Y - _dragStartMouse.Y) / scale;
    }

    private void Fab_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Fab.ReleaseMouseCapture();
            ExitMoveMode();
            return;
        }
        AnimatePress(1.0);
        _manager.ToggleMenu();
    }

    private void AnimatePress(double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        FabScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
        FabScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, anim);
    }
}
