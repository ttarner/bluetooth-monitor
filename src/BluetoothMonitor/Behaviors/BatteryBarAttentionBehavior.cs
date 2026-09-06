using System.Windows;
using System.Windows.Media.Animation;
using WpfProgressBar = System.Windows.Controls.ProgressBar;

namespace BluetoothMonitor.Behaviors;

public static class BatteryBarAttentionBehavior
{
    private const double DimmedOpacity = 0.38d;
    private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(130);
    private static readonly TimeSpan PauseBetweenFlashes = TimeSpan.FromMilliseconds(180);

    public static readonly DependencyProperty AttentionSequenceProperty =
        DependencyProperty.RegisterAttached(
            "AttentionSequence",
            typeof(int),
            typeof(BatteryBarAttentionBehavior),
            new PropertyMetadata(0, OnAttentionSequenceChanged));

    private static readonly DependencyProperty PendingAttentionSequenceProperty =
        DependencyProperty.RegisterAttached(
            "PendingAttentionSequence",
            typeof(int),
            typeof(BatteryBarAttentionBehavior),
            new PropertyMetadata(0));

    public static int GetAttentionSequence(DependencyObject obj) =>
        (int)obj.GetValue(AttentionSequenceProperty);

    public static void SetAttentionSequence(DependencyObject obj, int value) =>
        obj.SetValue(AttentionSequenceProperty, value);

    private static int GetPendingAttentionSequence(DependencyObject obj) =>
        (int)obj.GetValue(PendingAttentionSequenceProperty);

    private static void SetPendingAttentionSequence(DependencyObject obj, int value) =>
        obj.SetValue(PendingAttentionSequenceProperty, value);

    private static void OnAttentionSequenceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not WpfProgressBar progressBar)
            return;

        if (args.NewValue is not int nextSequence || args.OldValue is not int previousSequence || nextSequence <= previousSequence)
            return;

        if (!progressBar.IsLoaded)
        {
            SetPendingAttentionSequence(progressBar, nextSequence);
            progressBar.Loaded -= ProgressBar_Loaded;
            progressBar.Loaded += ProgressBar_Loaded;
            return;
        }

        RunAttentionAnimation(progressBar);
    }

    private static void ProgressBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfProgressBar progressBar)
            return;

        progressBar.Loaded -= ProgressBar_Loaded;

        if (GetPendingAttentionSequence(progressBar) <= 0)
            return;

        SetPendingAttentionSequence(progressBar, 0);
        RunAttentionAnimation(progressBar);
    }

    private static void RunAttentionAnimation(WpfProgressBar progressBar)
    {
        progressBar.BeginAnimation(UIElement.OpacityProperty, null);
        progressBar.Opacity = 1d;

        var firstFlashEnd = FlashDuration;
        var firstRecoveryEnd = firstFlashEnd + FlashDuration;
        var pauseEnd = firstRecoveryEnd + PauseBetweenFlashes;
        var secondFlashEnd = pauseEnd + FlashDuration;
        var secondRecoveryEnd = secondFlashEnd + FlashDuration;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.Stop
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(DimmedOpacity, KeyTime.FromTimeSpan(firstFlashEnd)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1d, KeyTime.FromTimeSpan(firstRecoveryEnd)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1d, KeyTime.FromTimeSpan(pauseEnd)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(DimmedOpacity, KeyTime.FromTimeSpan(secondFlashEnd)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1d, KeyTime.FromTimeSpan(secondRecoveryEnd)));
        animation.Completed += (_, _) => progressBar.Opacity = 1d;

        progressBar.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
