namespace SubZeroFramework.Controls;

public sealed partial class DurationPicker : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(TimeSpan),
        typeof(DurationPicker),
        new PropertyMetadata(TimeSpan.Zero, OnValueChanged));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
    nameof(Header),
    typeof(string),
    typeof(DurationPicker),
    new PropertyMetadata(string.Empty));

    private bool _isSynchronizingInputs;

    public DurationPicker()
    {
        InitializeComponent();
        ApplyValueToInputs(Normalize(Value));
    }

    public TimeSpan Value
    {
        get => (TimeSpan)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Normalize(value));
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not DurationPicker picker)
        {
            return;
        }

        var normalized = Normalize((TimeSpan)args.NewValue);
        if (normalized != (TimeSpan)args.NewValue)
        {
            picker.SetValue(ValueProperty, normalized);
            return;
        }

        picker.ApplyValueToInputs(normalized);
    }

    private static TimeSpan Normalize(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var wholeSeconds = Math.Truncate(value.TotalSeconds);
        return TimeSpan.FromSeconds(wholeSeconds);
    }

    private void ApplyValueToInputs(TimeSpan value)
    {
        _isSynchronizingInputs = true;

        try
        {
            HoursBox.Value = Math.Truncate(value.TotalHours);
            MinutesBox.Value = value.Minutes;
            SecondsBox.Value = value.Seconds;
        }
        finally
        {
            _isSynchronizingInputs = false;
        }
    }

    public void OnPartValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isSynchronizingInputs)
        {
            return;
        }

        var hours = CoerceWholeValue(HoursBox.Value, 0);
        var minutes = CoerceWholeValue(MinutesBox.Value, 0, 59);
        var seconds = CoerceWholeValue(SecondsBox.Value, 0, 59);

        var newValue = TimeSpan.FromHours(hours)
            + TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds(seconds);

        if (newValue != Value)
        {
            Value = newValue;
            return;
        }

        ApplyValueToInputs(newValue);
    }

    private static double CoerceWholeValue(double value, double minimum, double maximum = double.PositiveInfinity)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return minimum;
        }

        var wholeValue = Math.Truncate(value);
        if (wholeValue < minimum)
        {
            return minimum;
        }

        return wholeValue > maximum ? maximum : wholeValue;
    }
}
