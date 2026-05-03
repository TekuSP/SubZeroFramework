using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;

using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Interfaces;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using Windows.Foundation;
using Windows.Foundation.Collections;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace SubZeroFramework.Presentation.Header;

public sealed partial class SubZeroHeaderControl : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty SystemContainerProperty = DependencyProperty.Register(
        nameof(SystemContainer),
        typeof(IServiceProvider),
        typeof(SubZeroHeaderControl),
        new PropertyMetadata(null, ContainerChanged)
    );
    private static void ContainerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SubZeroHeaderControl control && e.NewValue is IServiceProvider serv)
        {
            control.ViewModel.IServiceProviderChanged(serv); //Refresh VM
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SubZeroHeaderModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    }

    public IServiceProvider SystemContainer
    {
        get => (IServiceProvider)this.GetValue(SystemContainerProperty);
        set => this.SetValue(SystemContainerProperty, value);
    }

    public SubZeroHeaderControl()
    {
        this.InitializeComponent();
        ViewModel = new SubZeroHeaderModel();
    }

    private void CardShadow_Loaded(object sender, RoutedEventArgs e)
    {
        cardShadow.Receivers.Add(cardGrid);
    }
    private void BrandShadow_Loaded(object sender, RoutedEventArgs e)
    {
        brandShadow.Receivers.Add(brandGrid);
    }
}
