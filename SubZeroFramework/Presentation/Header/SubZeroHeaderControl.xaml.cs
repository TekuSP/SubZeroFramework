// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace SubZeroFramework.Presentation.Header;

public sealed partial class SubZeroHeaderControl : UserControl
{
    public static readonly DependencyProperty SystemContainerProperty = DependencyProperty.Register(
        nameof(SystemContainer),
        typeof(IServiceProvider),
        typeof(SubZeroHeaderControl),
        new PropertyMetadata(null, ContainerChanged)
    );

    public static readonly DependencyProperty SuppressErrorBarProperty = DependencyProperty.Register(
        nameof(SuppressErrorBar),
        typeof(bool),
        typeof(SubZeroHeaderControl),
        new PropertyMetadata(false, SuppressErrorBarChanged)
    );

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(SubZeroHeaderModel),
        typeof(SubZeroHeaderControl),
        new PropertyMetadata(null));

    private static void ContainerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SubZeroHeaderControl control && e.NewValue is IServiceProvider serv)
        {
            control.ViewModel.IServiceProviderChanged(serv); //Refresh VM
        }
    }

    private static void SuppressErrorBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SubZeroHeaderControl control && e.NewValue is bool suppressErrorBar)
        {
            control.ViewModel.SuppressErrorBar = suppressErrorBar;
        }
    }

    public SubZeroHeaderModel ViewModel
    {
        get => (SubZeroHeaderModel)GetValue(ViewModelProperty);
        private set => SetValue(ViewModelProperty, value);
    }

    public IServiceProvider SystemContainer
    {
        get => (IServiceProvider)this.GetValue(SystemContainerProperty);
        set => this.SetValue(SystemContainerProperty, value);
    }

    public bool SuppressErrorBar
    {
        get => (bool)this.GetValue(SuppressErrorBarProperty);
        set => this.SetValue(SuppressErrorBarProperty, value);
    }

    public SubZeroHeaderControl()
    {
        this.InitializeComponent();
        ViewModel = new SubZeroHeaderModel();
        ViewModel.SuppressErrorBar = SuppressErrorBar;
    }
}
