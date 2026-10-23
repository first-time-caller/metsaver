using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MetSaver;

public partial class SettingsWindow : Window
{
    readonly Settings _s = Settings.Load();
    static readonly string[] Motions = { "off", "subtle", "ken_burns" };

    public SettingsWindow()
    {
        InitializeComponent();
        IdleBox.Text = _s.IdleSeconds.ToString();
        SecondsBox.Text = _s.SecondsPerPainting.ToString();
        FillBox.SelectedIndex = _s.Cover ? 0 : 1;
        StrictSlider.Value = _s.MinScreenFill;
        StrictSlider.ValueChanged += (_, _) => StrictLabel.Text = $"{StrictSlider.Value:0.00}";
        StrictLabel.Text = $"{_s.MinScreenFill:0.00}";
        MotionBox.SelectedIndex = Math.Max(0, Array.IndexOf(Motions, _s.Motion));
        CaptionCheck.IsChecked = _s.ShowCaption;
        CornerBox.SelectedIndex = Math.Max(0, new[] { "top-left", "top-right", "bottom-left", "bottom-right" }.ToList().IndexOf(_s.InfoCorner));
        HideBox.Text = _s.InfoSeconds.ToString();
        Dept11.IsChecked = _s.DepartmentIds.Contains(11);
        Dept1.IsChecked = _s.DepartmentIds.Contains(1);
        Dept21.IsChecked = _s.DepartmentIds.Contains(21);
        Dept6.IsChecked = _s.DepartmentIds.Contains(6);
        TermsBox.Text = string.Join(", ", _s.SearchTerms);
        MoodCheck.IsChecked = _s.TimeOfDay;
        CropCheck.IsChecked = _s.SmartCrop;
        AmbientCheck.IsChecked = _s.AmbientAware;
        Refresh();
    }

    void Refresh() =>
        Status.Text = Installer.IsActive()
            ? "MetSaver is your screensaver."
            : "MetSaver is not your screensaver yet - click \"Set as my screensaver\".";

    bool Collect()
    {
        if (!int.TryParse(IdleBox.Text, out var idle) || !int.TryParse(SecondsBox.Text, out var secs) || !int.TryParse(HideBox.Text, out var hide))
        {
            Status.Text = "Seconds fields must be whole numbers.";
            return false;
        }
        _s.IdleSeconds = idle; _s.SecondsPerPainting = secs; _s.InfoSeconds = hide;
        _s.FillMode = FillBox.SelectedIndex == 0 ? "cover" : "fit";
        _s.MinScreenFill = Math.Round(StrictSlider.Value, 2);
        _s.Motion = Motions[Math.Max(0, MotionBox.SelectedIndex)];
        _s.ShowCaption = CaptionCheck.IsChecked == true;
        _s.InfoCorner = ((ComboBoxItem)CornerBox.SelectedItem).Content.ToString()!;
        _s.DepartmentIds = new[] { (11, Dept11), (1, Dept1), (21, Dept21), (6, Dept6) }.Where(d => d.Item2.IsChecked == true).Select(d => d.Item1).ToList();
        _s.SearchTerms = TermsBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        _s.TimeOfDay = MoodCheck.IsChecked == true;
        _s.SmartCrop = CropCheck.IsChecked == true;
        _s.AmbientAware = AmbientCheck.IsChecked == true;
        _s.Normalize();
        _s.Save();
        return true;
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        if (!Collect()) return;
        if (Installer.IsActive()) Installer.Install(_s);  // re-apply the idle timeout
        Close();
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();

    void OnInstall(object sender, RoutedEventArgs e)
    {
        if (!Collect()) return;
        try { Installer.Install(_s); Refresh(); Status.Text += $" Starts after {_s.IdleSeconds} s idle."; }
        catch (Exception ex) { Log.Error(ex, "install"); Status.Text = "Couldn't register: " + ex.Message; }
    }

    void OnRestore(object sender, RoutedEventArgs e)
    {
        try { Installer.Uninstall(_s); Refresh(); }
        catch (Exception ex) { Log.Error(ex, "uninstall"); Status.Text = "Couldn't restore: " + ex.Message; }
    }

    void OnTest(object sender, RoutedEventArgs e)
    {
        if (Collect()) Process.Start(Environment.ProcessPath!, "/test");
    }

    void OnGallery(object sender, RoutedEventArgs e)
    {
        if (Collect()) Process.Start(Environment.ProcessPath!, "/gallery");
    }

    void OnClearHistory(object sender, RoutedEventArgs e)
    {
        try { File.Delete(Paths.HistoryFile); Status.Text = "Seen-paintings history cleared."; }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
}
