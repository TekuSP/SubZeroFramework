using System.ComponentModel;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace SubZeroFramework.Presentation.MenuItems.WarningsIssues;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class WarningIssuesPage : Page, INotifyPropertyChanged
{
    public WarningIssuesPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public WarningIssuesModel? ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    }

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is WarningIssuesModel model)
        {
            ViewModel = model;
        }
    }
    private void ErrorGridShadow_Loaded(object sender, RoutedEventArgs e)
    {
        errorGridShadow.Receivers.Add(errorGrid);
    }
    private void Card00Shadow_Loaded(object sender, RoutedEventArgs e)
    {
        card00Shadow.Receivers.Add(card00);
    }
    private void Card01Shadow_Loaded(object sender, RoutedEventArgs e)
    {
        card01Shadow.Receivers.Add(card01);
    }
    private void Card10Shadow_Loaded(object sender, RoutedEventArgs e)
    {
        card10Shadow.Receivers.Add(card10);
    }
    private void Card11Shadow_Loaded(object sender, RoutedEventArgs e)
    {
        card11Shadow.Receivers.Add(card11);
    }
}
