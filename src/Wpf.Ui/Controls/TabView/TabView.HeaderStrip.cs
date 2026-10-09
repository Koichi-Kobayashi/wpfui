// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Wpf.Ui.Appearance;

namespace Wpf.Ui.Controls;

/// <summary>Group backgrounds and optional scrolling parts for the document header strip.</summary>
public partial class TabView
{
    private static readonly DependencyProperty HeaderScrollPositionProperty = DependencyProperty.Register(
        "HeaderScrollPosition",
        typeof(double),
        typeof(TabView),
        new PropertyMetadata(0.0, OnHeaderScrollPositionChanged)
    );

    private readonly Dictionary<TabGroup, Border> _groupBackgrounds = new();
    private Canvas? _groupBackgroundLayer;
    private ScrollViewer? _headerScroller;
    private System.Windows.Controls.Button? _scrollLeftButton;
    private System.Windows.Controls.Button? _scrollRightButton;
    private bool _suppressScrollAnimation;
    private bool _headerThemeSubscribed;

    private bool UseHighContrastGroupBackground =>
        SystemParameters.HighContrast || ApplicationThemeManager.IsHighContrast();

    private bool HasHeaderScrollButtons =>
        IsGroupingActive
        && _headerScroller != null
        && _scrollLeftButton != null
        && _scrollRightButton != null;

    internal void ArrangeGroupBackgrounds(List<(TabGroup Group, Rect Bounds)> ranges)
    {
        if (_groupBackgroundLayer == null || !IsGroupingActive || !_groupSubscriptionsEnabled)
        {
            return;
        }

        foreach (
            TabGroup removed in _groupBackgrounds
                .Keys.Where(group => !ranges.Any(range => range.Group == group))
                .ToList()
        )
        {
            _groupBackgroundLayer.Children.Remove(_groupBackgrounds[removed]);
            _groupBackgrounds.Remove(removed);
        }

        foreach ((TabGroup Group, Rect Bounds) range in ranges)
        {
            if (!_groupBackgrounds.TryGetValue(range.Group, out Border? background))
            {
                background = new Border { IsHitTestVisible = false, CornerRadius = new CornerRadius(8) };
                _groupBackgrounds.Add(range.Group, background);
                _groupBackgroundLayer.Children.Add(background);
            }

            Canvas.SetLeft(background, range.Bounds.X);
            Canvas.SetTop(background, range.Bounds.Y);
            background.SetCurrentValue(WidthProperty, range.Bounds.Width);
            background.SetCurrentValue(HeightProperty, range.Bounds.Height);
        }

        UpdateGroupBackgroundAppearance(UseHighContrastGroupBackground);
    }

    private void ClearGroupBackgrounds()
    {
        _groupBackgrounds.Clear();
        _groupBackgroundLayer?.Children.Clear();
    }

    private void UpdateGroupBackgroundAppearance(bool highContrast)
    {
        foreach (KeyValuePair<TabGroup, Border> pair in _groupBackgrounds)
        {
            Border background = pair.Value;
            background.SetCurrentValue(
                VisibilityProperty,
                _groupsById.ContainsValue(pair.Key) ? Visibility.Visible : Visibility.Collapsed
            );
            if (highContrast)
            {
                background.SetCurrentValue(Border.BackgroundProperty, SystemColors.WindowBrush);
                background.SetCurrentValue(Border.BorderBrushProperty, SystemColors.WindowTextBrush);
            }
            else
            {
                Color color = pair.Key.Color;
                SetGroupBrush(
                    background,
                    Border.BackgroundProperty,
                    Color.FromArgb((byte)(color.A * .12), color.R, color.G, color.B)
                );
                SetGroupBrush(
                    background,
                    Border.BorderBrushProperty,
                    Color.FromArgb((byte)(color.A * .28), color.R, color.G, color.B)
                );
            }

            background.SetCurrentValue(Border.BorderThicknessProperty, new Thickness(1));
        }
    }

    private static void SetGroupBrush(Border border, DependencyProperty property, Color color)
    {
        if (border.GetValue(property) is SolidColorBrush current && current.Color == color)
        {
            return;
        }

        SolidColorBrush brush = new(color);
        brush.Freeze();
        border.SetCurrentValue(property, brush);
    }

    private void AttachHeaderStrip()
    {
        DetachHeaderStrip();
        if (!_groupSubscriptionsEnabled)
        {
            return;
        }

        if (_headerScroller != null)
        {
            _headerScroller.ScrollChanged += OnHeaderScrollChanged;
            _headerScroller.PreviewMouseWheel += OnHeaderMouseWheel;
        }

        if (_scrollLeftButton != null)
        {
            _scrollLeftButton.Click += OnScrollLeftClick;
        }

        if (_scrollRightButton != null)
        {
            _scrollRightButton.Click += OnScrollRightClick;
        }

        SizeChanged += OnHeaderStripSizeChanged;
        if (IsLoaded)
        {
            ApplicationThemeManager.Changed += OnHeaderThemeChanged;
            SystemParameters.StaticPropertyChanged += OnHeaderSystemPropertiesChanged;
            _headerThemeSubscribed = true;
        }

        UpdateHeaderScrollButtons();
    }

    private void DetachHeaderStrip()
    {
        StopHeaderScrollAnimation();
        if (_headerScroller != null)
        {
            _headerScroller.ScrollChanged -= OnHeaderScrollChanged;
            _headerScroller.PreviewMouseWheel -= OnHeaderMouseWheel;
        }

        if (_scrollLeftButton != null)
        {
            _scrollLeftButton.Click -= OnScrollLeftClick;
        }

        if (_scrollRightButton != null)
        {
            _scrollRightButton.Click -= OnScrollRightClick;
        }

        SizeChanged -= OnHeaderStripSizeChanged;
        if (_headerThemeSubscribed)
        {
            ApplicationThemeManager.Changed -= OnHeaderThemeChanged;
            SystemParameters.StaticPropertyChanged -= OnHeaderSystemPropertiesChanged;
            _headerThemeSubscribed = false;
        }
    }

    private void OnHeaderThemeChanged(ApplicationTheme theme, Color accent) => RefreshHeaderTheme();

    private void OnHeaderSystemPropertiesChanged(object? sender, PropertyChangedEventArgs e) =>
        RefreshHeaderTheme();

    private void RefreshHeaderTheme()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshHeaderTheme));
            return;
        }

        StopHeaderScrollAnimation();
        UpdateGroupBackgroundAppearance(UseHighContrastGroupBackground);
        foreach (KeyValuePair<TabGroup, Button> pair in _groupHeaders)
        {
            UpdateGroupHeader(pair.Key, pair.Value);
        }
    }

    private void OnHeaderStripSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateHeaderScrollButtons();

    private void OnHeaderScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.Source != _headerScroller)
        {
            return;
        }

        if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0)
        {
            StopHeaderScrollAnimation();
        }

        UpdateHeaderScrollButtons();
    }

    private void UpdateHeaderScrollButtons()
    {
        bool enhanced = HasHeaderScrollButtons;
        if (_headerScroller != null)
        {
            if (enhanced)
            {
                _headerScroller.SetCurrentValue(
                    ScrollViewer.HorizontalScrollBarVisibilityProperty,
                    ScrollBarVisibility.Hidden
                );
            }
            else if (IsGroupingActive)
            {
                // Older templates keep their usable scrollbar when the optional buttons are absent.
                _headerScroller.SetCurrentValue(
                    ScrollViewer.HorizontalScrollBarVisibilityProperty,
                    ScrollBarVisibility.Auto
                );
            }
            else if (TabGroups == null)
            {
                StopHeaderScrollAnimation();
                _headerScroller.SetCurrentValue(
                    ScrollViewer.HorizontalScrollBarVisibilityProperty,
                    ScrollBarVisibility.Disabled
                );
                _headerScroller.ScrollToHorizontalOffset(0);
            }
        }

        double available = _headerScroller?.ViewportWidth ?? 0;
        if (_scrollLeftButton?.Visibility == Visibility.Visible)
        {
            available += _scrollLeftButton.ActualWidth;
        }

        if (_scrollRightButton?.Visibility == Visibility.Visible)
        {
            available += _scrollRightButton.ActualWidth;
        }

        bool overflow = enhanced && _headerScroller!.ExtentWidth > available + .5;
        _scrollLeftButton?.SetCurrentValue(
            VisibilityProperty,
            overflow ? Visibility.Visible : Visibility.Collapsed
        );
        _scrollRightButton?.SetCurrentValue(
            VisibilityProperty,
            overflow ? Visibility.Visible : Visibility.Collapsed
        );
        _scrollLeftButton?.SetCurrentValue(
            IsEnabledProperty,
            overflow && _headerScroller!.HorizontalOffset > .5
        );
        _scrollRightButton?.SetCurrentValue(
            IsEnabledProperty,
            overflow && _headerScroller!.HorizontalOffset < _headerScroller.ScrollableWidth - .5
        );
        if (enhanced && !overflow)
        {
            StopHeaderScrollAnimation();
            _headerScroller!.ScrollToHorizontalOffset(0);
        }
    }

    private void OnScrollLeftClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (HasHeaderScrollButtons && _scrollLeftButton!.IsEnabled)
        {
            ScrollHeaderTo(
                (_headerScroller!.HorizontalOffset) - Math.Max(48, _headerScroller.ViewportWidth * .6)
            );
        }
    }

    private void OnScrollRightClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (HasHeaderScrollButtons && _scrollRightButton!.IsEnabled)
        {
            ScrollHeaderTo(
                (_headerScroller!.HorizontalOffset) + Math.Max(48, _headerScroller.ViewportWidth * .6)
            );
        }
    }

    private void OnHeaderMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!HasHeaderScrollButtons || _headerScroller!.ScrollableWidth <= 0)
        {
            return;
        }

        double direction = FlowDirection == FlowDirection.RightToLeft ? 1 : -1;
        double next = Math.Max(
            0,
            Math.Min(
                _headerScroller.ScrollableWidth,
                _headerScroller.HorizontalOffset + direction * e.Delta * .8
            )
        );
        if (Math.Abs(next - _headerScroller.HorizontalOffset) > .5)
        {
            ScrollHeaderTo(next);
            e.Handled = true;
        }
    }

    private static void OnHeaderScrollPositionChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    )
    {
        TabView owner = (TabView)d;
        if (!owner._suppressScrollAnimation)
        {
            owner._headerScroller?.ScrollToHorizontalOffset((double)e.NewValue);
        }
    }

#pragma warning disable WPF0041 // The private, unbound animation property requires an actual base value.
    private void StopHeaderScrollAnimation()
    {
        _suppressScrollAnimation = true;
        try
        {
            double offset = _headerScroller?.HorizontalOffset ?? 0;
            BeginAnimation(HeaderScrollPositionProperty, null);
            // This private, unbound animation property needs a real base value (not a current-value coercion).
            SetValue(HeaderScrollPositionProperty, offset);
        }
        finally
        {
            _suppressScrollAnimation = false;
        }
    }

    private void ScrollHeaderTo(double offset, bool animate = true)
    {
        if (!HasHeaderScrollButtons)
        {
            return;
        }

        double from = _headerScroller!.HorizontalOffset;
        double target = Math.Max(0, Math.Min(_headerScroller.ScrollableWidth, offset));
        StopHeaderScrollAnimation();
        _suppressScrollAnimation = true;
        SetValue(HeaderScrollPositionProperty, target);
        _suppressScrollAnimation = false;
        if (!animate || !SystemParameters.ClientAreaAnimation || UseHighContrastGroupBackground)
        {
            _headerScroller.ScrollToHorizontalOffset(target);
            return;
        }

        BeginAnimation(
            HeaderScrollPositionProperty,
            new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            }
        );
    }
#pragma warning restore WPF0041
}
