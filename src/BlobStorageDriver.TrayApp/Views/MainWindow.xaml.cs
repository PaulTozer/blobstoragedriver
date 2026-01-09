using System.Windows;
using System.Windows.Controls;
using Azure.Identity;
using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.TrayApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Desktop;
using RadioButton = System.Windows.Controls.RadioButton;

namespace BlobStorageDriver.TrayApp.Views;

public partial class MainWindow : Window
{
    private MainViewModel _mainViewModel = null!;
    private SettingsViewModel _settingsViewModel = null!;
    private ConflictViewModel _conflictViewModel = null!;
    private bool _isInitializingAuthCombo;

    public MainWindow()
    {
        InitializeComponent();
        
        try
        {
            _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
            _settingsViewModel = App.Services.GetRequiredService<SettingsViewModel>();
            _conflictViewModel = App.Services.GetRequiredService<ConflictViewModel>();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing ViewModels: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        
        DataContext = _mainViewModel;
        
        // Set DataContext on each page element immediately so bindings work
        if (SettingsPage != null && _settingsViewModel != null)
            SettingsPage.DataContext = _settingsViewModel;
        if (ConflictsPage != null && _conflictViewModel != null)
            ConflictsPage.DataContext = _conflictViewModel;
    }

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        // Guard against calls during InitializeComponent when elements aren't ready
        if (StatusPage == null || ActivityPage == null || ConflictsPage == null || SettingsPage == null)
            return;
            
        if (sender is RadioButton radioButton)
        {
            // Hide all pages
            StatusPage.Visibility = Visibility.Collapsed;
            ActivityPage.Visibility = Visibility.Collapsed;
            ConflictsPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            
            // Show selected page and set appropriate DataContext
            if (radioButton == NavStatus)
            {
                StatusPage.Visibility = Visibility.Visible;
                DataContext = _mainViewModel;
            }
            else if (radioButton == NavActivity)
            {
                ActivityPage.Visibility = Visibility.Visible;
                DataContext = _mainViewModel;
            }
            else if (radioButton == NavConflicts)
            {
                ConflictsPage.Visibility = Visibility.Visible;
                DataContext = _conflictViewModel;
            }
            else if (radioButton == NavSettings)
            {
                SettingsPage.Visibility = Visibility.Visible;
                DataContext = _settingsViewModel;
                // Also set DataContext on the SettingsPage element itself for proper binding inheritance
                SettingsPage.DataContext = _settingsViewModel;
                
                // Populate ComboBox when Settings page is shown
                try
                {
                    if (AuthTypeCombo != null && AuthTypeCombo.ItemsSource == null)
                    {
                        // Add event handler BEFORE setting items to catch selection changes
                        AuthTypeCombo.SelectionChanged += AuthTypeCombo_SelectionChanged;
                        AuthTypeCombo.ItemsSource = Enum.GetValues<AuthenticationType>();
                        // Set selected item without triggering login (use flag)
                        _isInitializingAuthCombo = true;
                        AuthTypeCombo.SelectedItem = _settingsViewModel.SelectedAuthType;
                        _isInitializingAuthCombo = false;
                    }
                    
                    // Update field visibility after page is visible - use Dispatcher to ensure UI is ready
                    Dispatcher.BeginInvoke(new Action(() => 
                    {
                        UpdateAuthFieldVisibility(_settingsViewModel.SelectedAuthType);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error setting up AuthTypeCombo: {ex.Message}");
                }
            }
        }
    }

    private void AuthTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Skip during initialization
        if (_isInitializingAuthCombo)
            return;
            
        if (AuthTypeCombo.SelectedItem is AuthenticationType authType)
        {
            _settingsViewModel.SelectedAuthType = authType;
            UpdateAuthFieldVisibility(authType);
        }
    }

    private void UpdateAuthFieldVisibility(AuthenticationType authType)
    {
        // Guard against null panels (can happen if called before UI is fully loaded)
        if (ConnectionStringPanel == null || AccountKeyPanel == null || 
            SasTokenPanel == null || EntraIdPanel == null)
            return;
            
        // Hide all auth-specific panels first
        ConnectionStringPanel.Visibility = Visibility.Collapsed;
        AccountKeyPanel.Visibility = Visibility.Collapsed;
        SasTokenPanel.Visibility = Visibility.Collapsed;
        EntraIdPanel.Visibility = Visibility.Collapsed;
        
        // Show the appropriate panel based on auth type
        switch (authType)
        {
            case AuthenticationType.ConnectionString:
                ConnectionStringPanel.Visibility = Visibility.Visible;
                break;
            case AuthenticationType.AccountKey:
                AccountKeyPanel.Visibility = Visibility.Visible;
                break;
            case AuthenticationType.SasToken:
                SasTokenPanel.Visibility = Visibility.Visible;
                break;
            case AuthenticationType.EntraIdInteractive:
            case AuthenticationType.EntraIdDefault:
                EntraIdPanel.Visibility = Visibility.Visible;
                break;
            case AuthenticationType.ManagedIdentity:
                // No additional fields needed for managed identity
                break;
        }
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        var originalContent = TestConnectionButton.Content;
        TestConnectionButton.Content = "Testing...";
        
        try
        {
            // Get SettingsViewModel - try multiple sources
            var settingsVm = _settingsViewModel;
            
            if (settingsVm == null)
            {
                settingsVm = SettingsPage?.DataContext as SettingsViewModel;
            }
            
            if (settingsVm == null)
            {
                settingsVm = App.Services?.GetService<SettingsViewModel>();
            }
            
            if (settingsVm == null)
            {
                // Create one directly as last resort
                settingsVm = new SettingsViewModel(App.Services?.GetService<AppConfiguration>() ?? new AppConfiguration());
            }

            // Read values directly from TextBoxes
            settingsVm.AccountName = AccountNameTextBox.Text;
            settingsVm.ContainerName = ContainerNameTextBox.Text;
            
            if (AuthTypeCombo.SelectedItem is AuthenticationType authType)
            {
                settingsVm.SelectedAuthType = authType;
            }

            await settingsVm.TestConnectionCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
            TestConnectionButton.Content = originalContent;
        }
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        SignInButton.Content = "Signing in...";
        
        try
        {
            await TriggerEntraIdLoginAsync();
        }
        finally
        {
            SignInButton.IsEnabled = true;
            SignInButton.Content = "🔑 Sign In with Microsoft";
        }
    }

    private async Task TriggerEntraIdLoginAsync()
    {
        try
        {
            // Get SettingsViewModel - try from DataContext first (if on Settings page), then from field
            var settingsVm = DataContext as SettingsViewModel ?? _settingsViewModel;
            
            if (settingsVm == null)
            {
                // Try to get from DI as a fallback
                settingsVm = App.Services.GetService<SettingsViewModel>();
            }
            
            if (settingsVm == null)
            {
                MessageBox.Show("Unable to access settings. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Get tenant ID if specified
            var tenantId = string.IsNullOrWhiteSpace(settingsVm.TenantId) 
                ? null 
                : settingsVm.TenantId;

            // Use MSAL directly with embedded WebView2 for a smaller, self-contained login window
            var appBuilder = PublicClientApplicationBuilder
                .Create(MsalTokenProvider.ClientId) // Azure CLI client ID (well-known)
                .WithAuthority(AzureCloudInstance.AzurePublic, tenantId ?? "common")
                .WithDefaultRedirectUri();
            
            // Enable Windows embedded browser support
            appBuilder = appBuilder.WithWindowsEmbeddedBrowserSupport();
            
            var app = appBuilder.Build();
            
            // Request a token to trigger the login - this opens the embedded browser
            var scopes = new[] { "https://storage.azure.com/.default" };
            var result = await app.AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(true)
                .ExecuteAsync();
            
            // Store the token in our shared cache for use by AzureBlobStorageProvider
            MsalTokenProvider.SetCachedToken(result);
            
            // Update status (with null check since SignInStatus might be in a collapsed panel)
            if (SignInStatus != null)
            {
                SignInStatus.Text = "✓ Signed in successfully! Credentials cached.";
                SignInStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                SignInStatus.Visibility = Visibility.Visible;
            }
            
            MessageBox.Show(
                "✓ Successfully authenticated with Microsoft Entra ID!\n\n" +
                "Your credentials have been cached and will be used when connecting to Azure Blob Storage.",
                "Authentication Successful",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (MsalException ex)
        {
            if (SignInStatus != null)
            {
                SignInStatus.Text = "✗ Sign-in failed. Please try again.";
                SignInStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                SignInStatus.Visibility = Visibility.Visible;
            }
            
            MessageBox.Show(
                $"Authentication failed: {ex.Message}\n\nPlease try again or use a different authentication method.",
                "Authentication Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            if (SignInStatus != null)
            {
                SignInStatus.Text = "✗ Error during sign-in.";
                SignInStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                SignInStatus.Visibility = Visibility.Visible;
            }
            
            MessageBox.Show(
                $"An error occurred during authentication:\n{ex.GetType().Name}: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }
}
