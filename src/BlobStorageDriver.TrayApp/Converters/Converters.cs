using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using System.Windows.Media;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.Common.Configuration;
using Application = System.Windows.Application;

namespace BlobStorageDriver.TrayApp.Converters;

/// <summary>
/// Converts boolean to visibility (inverse)
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return false;
    }
}

/// <summary>
/// Converts sync state to visibility (visible when syncing)
/// </summary>
public class SyncingToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SyncState state)
        {
            return state == SyncState.Syncing ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts file state to a color brush
/// </summary>
public class StateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FileState state)
        {
            return state switch
            {
                FileState.Synced => Application.Current.Resources["SuccessBrush"],
                FileState.Uploading or FileState.Downloading => Application.Current.Resources["PrimaryBrush"],
                FileState.Conflict => Application.Current.Resources["WarningBrush"],
                FileState.Error => Application.Current.Resources["ErrorBrush"],
                _ => Application.Current.Resources["TextSecondaryBrush"]
            };
        }
        return Application.Current.Resources["TextSecondaryBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts zero count to visibility
/// </summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean to text
/// </summary>
public class BoolToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string options)
        {
            var parts = options.Split('|');
            if (parts.Length >= 2)
            {
                return boolValue ? parts[0] : parts[1];
            }
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bytes to human-readable format
/// </summary>
public class BytesToStringConverter : IValueConverter
{
    private static readonly string[] Sizes = { "B", "KB", "MB", "GB", "TB" };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < Sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {Sizes[order]}";
        }
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts AuthenticationType enum to user-friendly display name
/// </summary>
public class AuthTypeToDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AuthenticationType authType)
        {
            return authType switch
            {
                AuthenticationType.ConnectionString => "Connection String",
                AuthenticationType.AccountKey => "Account Name + Key",
                AuthenticationType.SasToken => "SAS Token",
                AuthenticationType.EntraIdInteractive => "Microsoft Entra ID (Sign-in)",
                AuthenticationType.EntraIdDefault => "Microsoft Entra ID (Default)",
                AuthenticationType.ManagedIdentity => "Managed Identity",
                _ => authType.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts IntegrationMode enum to user-friendly display name
/// </summary>
public class IntegrationModeToDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IntegrationMode mode)
        {
            return mode switch
            {
                IntegrationMode.LocalFolder => "Local Folder (Cloud Files)",
                IntegrationMode.ShellNamespace => "Navigation Pane",
                IntegrationMode.VirtualDrive => "Drive Letter (SMB-compatible)",
                _ => mode.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
