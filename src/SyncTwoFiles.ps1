<#
    Sync Two Files
    Version : 1.1.1
    Autor   : Enmanuel
#>
param([switch]$Agent)

$APP_DIR = $PSScriptRoot
Import-Module (Join-Path $APP_DIR "SyncTwoFiles.Core.psm1") -Force -DisableNameChecking
$INFO  = Get-AppInfo
$PATHS = Get-AppPaths
$ICO   = Join-Path $APP_DIR "SyncTwoFiles.ico"
$PNG   = Join-Path $APP_DIR "SyncTwoFiles.png"

# Modo agente: se ejecuta al iniciar Windows y arranca la sincronizacion automatica de cada par.
if ($Agent) {
    try { Start-AllAutoSync (Read-SyncConfig $PATHS.ConfigFile) $PATHS.LogDir } catch { }
    exit
}

Add-Type -Namespace Native -Name Win -MemberDefinition @'
[DllImport("shell32.dll")] public static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string id);
[DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
'@

$created = $false
$mutex = New-Object Threading.Mutex($true, "Local\SyncTwoFiles", [ref]$created)
if (-not $created) {
    $other = Get-Process powershell -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -ne $PID -and $_.MainWindowTitle -eq $INFO.Name } | Select-Object -First 1
    if ($other) {
        $h = $other.MainWindowHandle
        if ([Native.Win]::IsIconic($h)) { [void][Native.Win]::ShowWindowAsync($h, 9) }
        [void][Native.Win]::SetForegroundWindow($h)
    }
    exit
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
[void][Native.Win]::SetCurrentProcessExplicitAppUserModelID("Enmanuel.SyncTwoFiles")

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class FolderPicker {
    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog {
        [PreserveSig] int Show(IntPtr hwndOwner);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

    public static string Pick(IntPtr owner, string title, string initialFolder) {
        IFileOpenDialog dlg = (IFileOpenDialog)new FileOpenDialogRCW();
        uint opts;
        dlg.GetOptions(out opts);
        dlg.SetOptions(opts | 0x20 | 0x40 | 0x800); // PICKFOLDERS | FORCEFILESYSTEM | PATHMUSTEXIST
        if (!string.IsNullOrEmpty(title)) dlg.SetTitle(title);
        if (!string.IsNullOrEmpty(initialFolder) && System.IO.Directory.Exists(initialFolder)) {
            try {
                Guid g = typeof(IShellItem).GUID;
                IShellItem item;
                SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref g, out item);
                dlg.SetFolder(item);
            } catch { }
        }
        if (dlg.Show(owner) != 0) return null;
        IShellItem result;
        dlg.GetResult(out result);
        IntPtr p;
        result.GetDisplayName(0x80058000, out p); // SIGDN_FILESYSPATH
        string path = Marshal.PtrToStringUni(p);
        Marshal.FreeCoTaskMem(p);
        return path;
    }
}
'@

try {
    $script:config = Read-SyncConfig $PATHS.ConfigFile
} catch {
    [void][Windows.MessageBox]::Show($_.Exception.Message, $INFO.Name, 'OK', 'Error')
    exit
}

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Sync Two Files" Width="1000" Height="740" MinWidth="860" MinHeight="600"
        WindowStartupLocation="CenterScreen" ResizeMode="CanResize" Background="#1C1C1E"
        FontFamily="Segoe UI Variable Text, Segoe UI" FontSize="13"
        TextOptions.TextFormattingMode="Display" TextOptions.TextRenderingMode="ClearType"
        UseLayoutRounding="True" SnapsToDevicePixels="True">
  <WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight="40" ResizeBorderThickness="6" GlassFrameThickness="0,0,0,1"
                  UseAeroCaptionButtons="False" CornerRadius="0"/>
  </WindowChrome.WindowChrome>

  <Window.Resources>
    <SolidColorBrush x:Key="Label" Color="#F5F5F7"/>
    <SolidColorBrush x:Key="Secondary" Color="#98989D"/>
    <SolidColorBrush x:Key="Sep" Color="#38383A"/>
    <FontFamily x:Key="Icons">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>

    <Style x:Key="Cap" TargetType="Button">
      <Setter Property="Width" Value="46"/>
      <Setter Property="Height" Value="40"/>
      <Setter Property="Foreground" Value="#E5E5EA"/>
      <Setter Property="FontFamily" Value="{StaticResource Icons}"/>
      <Setter Property="FontSize" Value="10"/>
      <Setter Property="WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="b" Background="Transparent">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#1FFFFFFF"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="Background" Value="#14FFFFFF"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="CapClose" TargetType="Button" BasedOn="{StaticResource Cap}">
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="b" Background="Transparent">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="b" Property="Background" Value="#C42B1C"/>
                <Setter Property="Foreground" Value="White"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="Background" Value="#A3271B"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="Btn" TargetType="Button">
      <Setter Property="Background" Value="#2E0A84FF"/>
      <Setter Property="Foreground" Value="#409CFF"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Padding" Value="16,6"/>
      <Setter Property="MinWidth" Value="96"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="b" Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.82"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.6"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.35"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="BtnPrimary" TargetType="Button" BasedOn="{StaticResource Btn}">
      <Setter Property="Background" Value="#0A84FF"/>
      <Setter Property="Foreground" Value="White"/>
    </Style>
    <Style x:Key="BtnGray" TargetType="Button" BasedOn="{StaticResource Btn}">
      <Setter Property="Background" Value="#3A3A3C"/>
      <Setter Property="Foreground" Value="#F5F5F7"/>
    </Style>
    <Style x:Key="BtnDanger" TargetType="Button" BasedOn="{StaticResource Btn}">
      <Setter Property="Background" Value="#2EFF453A"/>
      <Setter Property="Foreground" Value="#FF6961"/>
    </Style>

    <Style x:Key="Link" TargetType="Button">
      <Setter Property="Foreground" Value="#409CFF"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="b" Background="Transparent" Padding="6,2">
              <ContentPresenter VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.7"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.5"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="Switch" TargetType="ToggleButton">
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ToggleButton">
            <Grid Width="46" Height="28">
              <Border x:Name="track" CornerRadius="14" Background="#39393D"/>
              <Ellipse x:Name="knob" Width="24" Height="24" Fill="White" HorizontalAlignment="Left" Margin="2,0,0,0">
                <Ellipse.Effect><DropShadowEffect BlurRadius="5" ShadowDepth="1" Direction="270" Opacity="0.35"/></Ellipse.Effect>
                <Ellipse.RenderTransform><TranslateTransform X="0"/></Ellipse.RenderTransform>
              </Ellipse>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="track" Property="Background" Value="#30D158"/>
                <Trigger.EnterActions>
                  <BeginStoryboard><Storyboard>
                    <DoubleAnimation Storyboard.TargetName="knob" Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.X)" To="18" Duration="0:0:0.2">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard></BeginStoryboard>
                </Trigger.EnterActions>
                <Trigger.ExitActions>
                  <BeginStoryboard><Storyboard>
                    <DoubleAnimation Storyboard.TargetName="knob" Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.X)" To="0" Duration="0:0:0.2">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard></BeginStoryboard>
                </Trigger.ExitActions>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style TargetType="ScrollBar">
      <Setter Property="Width" Value="10"/>
      <Setter Property="MinWidth" Value="10"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ScrollBar">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType="Thumb">
                      <Border Background="#55FFFFFF" CornerRadius="3" Margin="2"/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="Field" TargetType="TextBox">
      <Setter Property="Foreground" Value="#F5F5F7"/>
      <Setter Property="CaretBrush" Value="#F5F5F7"/>
      <Setter Property="SelectionBrush" Value="#0A84FF"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="Padding" Value="10,7"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="TextBox">
            <Border x:Name="b" Background="#1C1C1E" BorderBrush="#3A3A3C" BorderThickness="1" CornerRadius="8">
              <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsReadOnly" Value="True"><Setter TargetName="b" Property="Background" Value="#242426"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#0A84FF"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="Card" TargetType="Border">
      <Setter Property="Background" Value="#2C2C2E"/>
      <Setter Property="CornerRadius" Value="12"/>
      <Setter Property="BorderBrush" Value="#38383A"/>
      <Setter Property="BorderThickness" Value="1"/>
    </Style>
    <Style x:Key="Section" TargetType="TextBlock">
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Foreground" Value="#8E8E93"/>
      <Setter Property="Margin" Value="14,20,0,7"/>
    </Style>
    <Style x:Key="FieldLabel" TargetType="TextBlock">
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Foreground" Value="#AEAEB2"/>
      <Setter Property="Margin" Value="2,14,0,6"/>
    </Style>
    <Style x:Key="RowIcon" TargetType="Border">
      <Setter Property="Width" Value="32"/>
      <Setter Property="Height" Value="32"/>
      <Setter Property="CornerRadius" Value="8"/>
      <Setter Property="Margin" Value="0,0,14,0"/>
      <Setter Property="VerticalAlignment" Value="Center"/>
    </Style>
    <Style x:Key="Glyph" TargetType="TextBlock">
      <Setter Property="FontFamily" Value="{StaticResource Icons}"/>
      <Setter Property="FontSize" Value="16"/>
      <Setter Property="Foreground" Value="White"/>
      <Setter Property="HorizontalAlignment" Value="Center"/>
      <Setter Property="VerticalAlignment" Value="Center"/>
    </Style>
    <Style x:Key="RowTitle" TargetType="TextBlock">
      <Setter Property="FontSize" Value="13.5"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Foreground" Value="{StaticResource Label}"/>
    </Style>
    <Style x:Key="RowSub" TargetType="TextBlock">
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Foreground" Value="{StaticResource Secondary}"/>
      <Setter Property="Margin" Value="0,2,0,0"/>
      <Setter Property="TextTrimming" Value="CharacterEllipsis"/>
    </Style>
  </Window.Resources>

  <Grid x:Name="Root">
    <Grid.ColumnDefinitions><ColumnDefinition Width="270"/><ColumnDefinition/></Grid.ColumnDefinitions>
    <Grid.RowDefinitions><RowDefinition Height="40"/><RowDefinition/></Grid.RowDefinitions>

    <!-- Barra lateral -->
    <Border Grid.RowSpan="2" Background="#232326" BorderBrush="#2E2E31" BorderThickness="0,0,1,0"/>
    <StackPanel Grid.Row="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="16,0,0,0">
      <Image x:Name="SideIcon" Width="16" Height="16" RenderOptions.BitmapScalingMode="HighQuality"/>
      <TextBlock Text="Sync Two Files" Margin="10,0,0,0" FontSize="12" Foreground="#C7C7CC" VerticalAlignment="Center"/>
    </StackPanel>
    <Grid Grid.Row="1" Margin="12,4,12,14">
      <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
      <TextBlock Text="SINCRONIZACIONES" Style="{StaticResource Section}" Margin="8,8,0,8"/>
      <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
        <StackPanel x:Name="PairPanel"/>
      </ScrollViewer>
      <Button x:Name="BtnAdd" Grid.Row="2" Style="{StaticResource Btn}" Margin="0,10,0,0" Padding="0,9">
        <StackPanel Orientation="Horizontal">
          <TextBlock FontFamily="{StaticResource Icons}" Text="&#xE710;" FontSize="12" VerticalAlignment="Center" Margin="0,0,8,0"/>
          <TextBlock Text="Nueva sincronización"/>
        </StackPanel>
      </Button>
      <TextBlock x:Name="VersionText" Grid.Row="3" HorizontalAlignment="Center" Margin="0,12,0,0" FontSize="11" Foreground="#636366"/>
    </Grid>

    <!-- Botones de ventana -->
    <StackPanel Grid.Column="1" Grid.Row="0" Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Top">
      <Button x:Name="BtnMin" Style="{StaticResource Cap}" Content="&#xE921;" ToolTip="Minimizar"/>
      <Button x:Name="BtnMax" Style="{StaticResource Cap}" Content="&#xE922;" ToolTip="Maximizar"/>
      <Button x:Name="BtnClose" Style="{StaticResource CapClose}" Content="&#xE8BB;" ToolTip="Cerrar"/>
    </StackPanel>

    <!-- Sin sincronizaciones -->
    <StackPanel x:Name="EmptyView" Grid.Column="1" Grid.Row="1" VerticalAlignment="Center" HorizontalAlignment="Center"
                MaxWidth="400" Margin="0,0,0,60" Visibility="Collapsed">
      <Image x:Name="EmptyIcon" Width="96" Height="96" RenderOptions.BitmapScalingMode="HighQuality"/>
      <TextBlock Text="Aún no tienes sincronizaciones" FontSize="20" FontWeight="Bold" Foreground="{StaticResource Label}"
                 HorizontalAlignment="Center" Margin="0,18,0,8"/>
      <TextBlock Text="Elige una carpeta y dónde guardar su copia. Sync Two Files la mantendrá respaldada automáticamente."
                 TextWrapping="Wrap" TextAlignment="Center" Foreground="{StaticResource Secondary}" LineHeight="19"/>
      <Button x:Name="BtnAddEmpty" Style="{StaticResource BtnPrimary}" Content="Crear sincronización" HorizontalAlignment="Center"
              Margin="0,22,0,0" Padding="22,9"/>
    </StackPanel>

    <!-- Detalle -->
    <Grid x:Name="DetailView" Grid.Column="1" Grid.Row="1" Margin="32,4,32,22" MaxWidth="880">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
        <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
        <RowDefinition Height="*" MinHeight="90"/>
      </Grid.RowDefinitions>

      <Grid Grid.Row="0">
        <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
        <Image x:Name="PairIcon" Width="56" Height="56" RenderOptions.BitmapScalingMode="HighQuality"/>
        <StackPanel Grid.Column="1" Margin="16,0,12,0" VerticalAlignment="Center">
          <TextBlock x:Name="PairName" FontFamily="Segoe UI Variable Display, Segoe UI" FontSize="24" FontWeight="Bold"
                     Foreground="{StaticResource Label}" TextTrimming="CharacterEllipsis"/>
          <StackPanel Orientation="Horizontal" Margin="0,3,0,0">
            <TextBlock x:Name="LockBadge" FontFamily="{StaticResource Icons}" Text="&#xE72E;" FontSize="11" Foreground="#FF9F0A"
                       VerticalAlignment="Center" Margin="0,1,6,0"/>
            <TextBlock x:Name="PairSub" FontSize="12.5" Foreground="{StaticResource Secondary}"/>
          </StackPanel>
        </StackPanel>
        <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
          <Button x:Name="BtnEdit" Style="{StaticResource Btn}" Content="Editar" Margin="0,0,8,0"/>
          <Button x:Name="BtnLock" Style="{StaticResource Btn}" Content="Bloquear"/>
          <Button x:Name="BtnDelete" Style="{StaticResource BtnDanger}" Content="Eliminar" Margin="8,0,0,0"/>
        </StackPanel>
      </Grid>

      <TextBlock Grid.Row="1" Text="CARPETAS" Style="{StaticResource Section}"/>
      <Border Grid.Row="2" Style="{StaticResource Card}">
        <StackPanel>
          <Grid Margin="16,12">
            <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <Border Style="{StaticResource RowIcon}" Background="#0A84FF"><TextBlock Style="{StaticResource Glyph}" Text="&#xE8B7;"/></Border>
            <StackPanel Grid.Column="1" VerticalAlignment="Center">
              <TextBlock Text="Carpeta a respaldar" Style="{StaticResource RowTitle}"/>
              <TextBlock x:Name="SrcPath" Style="{StaticResource RowSub}"/>
            </StackPanel>
            <Button x:Name="BtnOpenSrc" Grid.Column="2" Style="{StaticResource Link}" Content="Abrir"/>
          </Grid>
          <Border Height="1" Background="{StaticResource Sep}" Margin="62,0,0,0"/>
          <Grid Margin="16,12">
            <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <Border Style="{StaticResource RowIcon}" Background="#636366"><TextBlock Style="{StaticResource Glyph}" Text="&#xEDA2;"/></Border>
            <StackPanel Grid.Column="1" VerticalAlignment="Center">
              <TextBlock Text="Backup" Style="{StaticResource RowTitle}"/>
              <TextBlock x:Name="DstPath" Style="{StaticResource RowSub}"/>
            </StackPanel>
            <Button x:Name="BtnOpenDst" Grid.Column="2" Style="{StaticResource Link}" Content="Abrir"/>
          </Grid>
        </StackPanel>
      </Border>

      <TextBlock Grid.Row="3" Text="SINCRONIZACIÓN" Style="{StaticResource Section}"/>
      <Border Grid.Row="4" Style="{StaticResource Card}">
        <StackPanel>
          <Grid Margin="16,13">
            <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <Border Style="{StaticResource RowIcon}" Background="#30D158"><TextBlock Style="{StaticResource Glyph}" Text="&#xE895;"/></Border>
            <StackPanel Grid.Column="1" VerticalAlignment="Center">
              <TextBlock Text="Sincronización automática" Style="{StaticResource RowTitle}"/>
              <TextBlock x:Name="AutoStatus" Style="{StaticResource RowSub}" Text="Comprobando…"/>
            </StackPanel>
            <ToggleButton x:Name="AutoSwitch" Grid.Column="2" Style="{StaticResource Switch}" VerticalAlignment="Center"/>
          </Grid>
          <Border Height="1" Background="{StaticResource Sep}" Margin="62,0,0,0"/>
          <Grid Margin="16,13">
            <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <Border Style="{StaticResource RowIcon}" Background="#FF9F0A"><TextBlock Style="{StaticResource Glyph}" Text="&#xE8C8;"/></Border>
            <StackPanel Grid.Column="1" VerticalAlignment="Center">
              <TextBlock Text="Espejo completo" Style="{StaticResource RowTitle}"/>
              <TextBlock Text="Deja el backup idéntico. Borra lo que sobra." Style="{StaticResource RowSub}"/>
            </StackPanel>
            <Button x:Name="BtnMirror" Grid.Column="2" Style="{StaticResource Btn}" Content="Sincronizar" VerticalAlignment="Center" MinWidth="110"/>
          </Grid>
          <Border Height="1" Background="{StaticResource Sep}" Margin="62,0,0,0"/>
          <Grid Margin="16,13">
            <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <Border Style="{StaticResource RowIcon}" Background="#5E5CE6"><TextBlock Style="{StaticResource Glyph}" Text="&#xE777;"/></Border>
            <StackPanel Grid.Column="1" VerticalAlignment="Center">
              <TextBlock Text="Restaurar desde el backup" Style="{StaticResource RowTitle}"/>
              <TextBlock Text="Copia a la carpeta original lo que falte. No borra nada." Style="{StaticResource RowSub}"/>
            </StackPanel>
            <Button x:Name="BtnRestore" Grid.Column="2" Style="{StaticResource Btn}" Content="Restaurar" VerticalAlignment="Center" MinWidth="110"/>
          </Grid>
        </StackPanel>
      </Border>

      <Grid Grid.Row="5">
        <TextBlock Text="ACTIVIDAD" Style="{StaticResource Section}"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,4,5">
          <Button x:Name="BtnLog" Style="{StaticResource Link}" Content="Ver historial" FontSize="12"/>
          <Button x:Name="BtnClear" Style="{StaticResource Link}" Content="Limpiar" FontSize="12"/>
        </StackPanel>
      </Grid>
      <Border Grid.Row="6" Style="{StaticResource Card}" ClipToBounds="True">
        <Grid>
          <Border x:Name="BarTrack" Height="3" VerticalAlignment="Top" ClipToBounds="True" Visibility="Hidden" Margin="12,0">
            <Border x:Name="BarFill" Width="140" Background="#0A84FF" CornerRadius="1.5" HorizontalAlignment="Left"/>
          </Border>
          <TextBox x:Name="Out" IsReadOnly="True" BorderThickness="0" Background="Transparent" CaretBrush="Transparent"
                   SelectionBrush="#0A84FF" FontFamily="Cascadia Mono, Consolas" FontSize="11.5" Foreground="#D1D1D6" Padding="12,10"
                   VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" TextWrapping="Wrap"/>
        </Grid>
      </Border>
    </Grid>

    <!-- Capa de dialogos -->
    <Border x:Name="Overlay" Grid.RowSpan="2" Grid.ColumnSpan="2" Background="#A6000000" Visibility="Collapsed">
      <Grid>
        <!-- Confirmacion -->
        <Border x:Name="ConfirmPanel" Width="380" Background="#2C2C2E" BorderBrush="#3A3A3C" BorderThickness="1" CornerRadius="14"
                Padding="24,22" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed">
          <Border.Effect><DropShadowEffect BlurRadius="30" ShadowDepth="8" Direction="270" Opacity="0.5"/></Border.Effect>
          <StackPanel>
            <Image x:Name="DlgIcon" Width="52" Height="52" RenderOptions.BitmapScalingMode="HighQuality"/>
            <TextBlock x:Name="DlgTitle" FontSize="15" FontWeight="Bold" Foreground="{StaticResource Label}" TextAlignment="Center"
                       TextWrapping="Wrap" Margin="0,12,0,6"/>
            <ScrollViewer MaxHeight="190" VerticalScrollBarVisibility="Auto">
              <TextBlock x:Name="DlgText" FontSize="12" Foreground="{StaticResource Secondary}" TextAlignment="Center" TextWrapping="Wrap"/>
            </ScrollViewer>
            <TextBlock x:Name="DlgInputHint" FontSize="12" Foreground="#AEAEB2" TextAlignment="Center" TextWrapping="Wrap" Margin="0,14,0,6"/>
            <TextBox x:Name="DlgInput" Style="{StaticResource Field}"/>
            <Grid Margin="0,20,0,0">
              <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="10"/><ColumnDefinition/></Grid.ColumnDefinitions>
              <Button x:Name="DlgCancel" Style="{StaticResource BtnGray}" Content="Cancelar" Padding="0,8"/>
              <Button x:Name="DlgOk" Grid.Column="2" Style="{StaticResource BtnPrimary}" Content="Aceptar" Padding="0,8"/>
            </Grid>
          </StackPanel>
        </Border>

        <!-- Editor -->
        <Border x:Name="EditorPanel" Width="520" Background="#2C2C2E" BorderBrush="#3A3A3C" BorderThickness="1" CornerRadius="14"
                Padding="26,22" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed">
          <Border.Effect><DropShadowEffect BlurRadius="30" ShadowDepth="8" Direction="270" Opacity="0.5"/></Border.Effect>
          <StackPanel>
            <TextBlock x:Name="EdTitle" FontSize="17" FontWeight="Bold" Foreground="{StaticResource Label}"/>
            <TextBlock Text="Nombre" Style="{StaticResource FieldLabel}"/>
            <TextBox x:Name="EdName" Style="{StaticResource Field}"/>
            <TextBlock Text="Carpeta a respaldar" Style="{StaticResource FieldLabel}"/>
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <TextBox x:Name="EdSrc" Style="{StaticResource Field}" IsReadOnly="True"/>
              <Button x:Name="EdPickSrc" Grid.Column="1" Style="{StaticResource Btn}" Content="Elegir…" Margin="8,0,0,0"/>
            </Grid>
            <TextBlock Text="Guardar el backup en" Style="{StaticResource FieldLabel}"/>
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <TextBox x:Name="EdDst" Style="{StaticResource Field}" IsReadOnly="True"/>
              <Button x:Name="EdPickDst" Grid.Column="1" Style="{StaticResource Btn}" Content="Elegir…" Margin="8,0,0,0"/>
            </Grid>
            <TextBlock Text="Puede estar en otro disco. Si la carpeta no existe, se creará." FontSize="11.5" Foreground="#8E8E93" Margin="2,6,0,0"/>

            <Border Background="#232325" CornerRadius="10" Margin="0,18,0,0">
              <StackPanel>
                <Grid Margin="14,11">
                  <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <StackPanel VerticalAlignment="Center" Margin="0,0,12,0">
                    <TextBlock Text="Sincronización automática" Style="{StaticResource RowTitle}"/>
                    <TextBlock Text="Copia los cambios cada minuto, sin borrar nada del backup." Style="{StaticResource RowSub}" TextWrapping="Wrap"/>
                  </StackPanel>
                  <ToggleButton x:Name="EdAuto" Grid.Column="1" Style="{StaticResource Switch}" VerticalAlignment="Center"/>
                </Grid>
                <Border Height="1" Background="{StaticResource Sep}" Margin="14,0,0,0"/>
                <Grid Margin="14,11">
                  <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <StackPanel VerticalAlignment="Center" Margin="0,0,12,0">
                    <TextBlock Text="Bloquear configuración" Style="{StaticResource RowTitle}"/>
                    <TextBlock Text="Evita cambios accidentales. Para quitar el bloqueo se pedirán 3 confirmaciones." Style="{StaticResource RowSub}" TextWrapping="Wrap"/>
                  </StackPanel>
                  <ToggleButton x:Name="EdLock" Grid.Column="1" Style="{StaticResource Switch}" VerticalAlignment="Center"/>
                </Grid>
              </StackPanel>
            </Border>

            <TextBlock x:Name="EdError" Foreground="#FF6961" TextWrapping="Wrap" Margin="2,12,0,0" Visibility="Collapsed"/>
            <Grid Margin="0,20,0,0">
              <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="10"/><ColumnDefinition/></Grid.ColumnDefinitions>
              <Button x:Name="EdCancel" Style="{StaticResource BtnGray}" Content="Cancelar" Padding="0,8"/>
              <Button x:Name="EdSave" Grid.Column="2" Style="{StaticResource BtnPrimary}" Content="Guardar" Padding="0,8"/>
            </Grid>
          </StackPanel>
        </Border>
      </Grid>
    </Border>
  </Grid>
</Window>
'@

$win = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $xaml))
$names = 'Root','SideIcon','PairPanel','BtnAdd','VersionText','BtnMin','BtnMax','BtnClose',
         'EmptyView','EmptyIcon','BtnAddEmpty','DetailView','PairIcon','PairName','LockBadge','PairSub',
         'BtnEdit','BtnLock','BtnDelete','SrcPath','DstPath','BtnOpenSrc','BtnOpenDst',
         'AutoSwitch','AutoStatus','BtnMirror','BtnRestore','BtnLog','BtnClear','BarTrack','BarFill','Out',
         'Overlay','ConfirmPanel','DlgIcon','DlgTitle','DlgText','DlgInputHint','DlgInput','DlgOk','DlgCancel',
         'EditorPanel','EdTitle','EdName','EdSrc','EdDst','EdPickSrc','EdPickDst','EdAuto','EdLock','EdError','EdCancel','EdSave'
foreach ($n in $names) { Set-Variable -Name $n -Value $win.FindName($n) }

# ---------- Recursos ----------
if (Test-Path $ICO) { $win.Icon = [Windows.Media.Imaging.BitmapFrame]::Create([Uri]$ICO) }
if (Test-Path $PNG) {
    $img = New-Object Windows.Media.Imaging.BitmapImage
    $img.BeginInit(); $img.UriSource = [Uri]$PNG; $img.CacheOption = 'OnLoad'; $img.EndInit()
    foreach ($i in $SideIcon, $EmptyIcon, $PairIcon, $DlgIcon) { $i.Source = $img }
}
$VersionText.Text = "Versión $($INFO.Version)  ·  © 2026 $($INFO.Author)"
$win.Height = [Math]::Min($win.Height, [Windows.SystemParameters]::WorkArea.Height - 40)
$ICON_FONT = $win.FindResource('Icons')

$script:brushCache = @{}
function B([string]$hex) {
    if (-not $script:brushCache.ContainsKey($hex)) {
        $script:brushCache[$hex] = (New-Object Windows.Media.BrushConverter).ConvertFromString($hex)
    }
    $script:brushCache[$hex]
}

# ---------- Ventana ----------
$script:hwnd = [IntPtr]::Zero
$win.Add_SourceInitialized({
    $script:hwnd = (New-Object Windows.Interop.WindowInteropHelper $win).Handle
    $one = 1;             [void][Native.Win]::DwmSetWindowAttribute($script:hwnd, 20, [ref]$one, 4)
    $round = 2;           [void][Native.Win]::DwmSetWindowAttribute($script:hwnd, 33, [ref]$round, 4)
    $border = 0x003A3A3A; [void][Native.Win]::DwmSetWindowAttribute($script:hwnd, 34, [ref]$border, 4)
})

function Update-MaxButton {
    if ($win.WindowState -eq 'Maximized') {
        $Root.Margin = New-Object Windows.Thickness 8
        $BtnMax.Content = [string][char]0xE923; $BtnMax.ToolTip = "Restaurar"
    } else {
        $Root.Margin = New-Object Windows.Thickness 0
        $BtnMax.Content = [string][char]0xE922; $BtnMax.ToolTip = "Maximizar"
    }
}
function Switch-Maximize { $win.WindowState = if ($win.WindowState -eq 'Maximized') { 'Normal' } else { 'Maximized' } }

$win.Add_StateChanged({ Update-MaxButton })
$BtnMin.Add_Click({ $win.WindowState = 'Minimized' })
$BtnMax.Add_Click({ Switch-Maximize })
$BtnClose.Add_Click({ $win.Close() })

# ---------- Estado ----------
$script:selectedId = $null
$script:sideItems = @{}
$script:buffers = @{}
$script:running = @{}
$script:statusHold = 0
$MAX_OUT_CHARS = 150000

function Get-SelectedPair { Get-SyncPair $script:config $script:selectedId }

$win.Dispatcher.Add_UnhandledException({
    param($s, $e)
    $e.Handled = $true
    Write-Out "Error inesperado: $($e.Exception.Message)"
})

function Save-Config {
    try { Save-SyncConfig $script:config $PATHS.ConfigFile; $true }
    catch { Write-Out "No se pudo guardar la configuración: $($_.Exception.Message)"; $false }
}

function Add-Out([string]$text, [string]$pairId) {
    if (-not $pairId) { return }
    if (-not $script:buffers.ContainsKey($pairId)) { $script:buffers[$pairId] = New-Object Text.StringBuilder }
    $sb = $script:buffers[$pairId]
    [void]$sb.Append($text)
    $trimmed = $false
    if ($sb.Length -gt $MAX_OUT_CHARS) {
        [void]$sb.Remove(0, $sb.Length - [int]($MAX_OUT_CHARS * 0.8))
        $trimmed = $true
    }
    if ($pairId -eq $script:selectedId) {
        if ($trimmed) { $Out.Text = $sb.ToString() } else { $Out.AppendText($text) }
        $Out.ScrollToEnd()
    }
}
function Write-Out([string]$msg, [string]$pairId = $script:selectedId) { Add-Out "$msg`r`n" $pairId }

# ---------- Barra lateral ----------
function New-SideItem($pair) {
    $b = New-Object Windows.Controls.Border
    $b.CornerRadius = New-Object Windows.CornerRadius 8
    $b.Padding = New-Object Windows.Thickness(10, 8, 10, 8)
    $b.Margin = New-Object Windows.Thickness(0, 0, 0, 2)
    $b.Background = [Windows.Media.Brushes]::Transparent
    $b.Cursor = [Windows.Input.Cursors]::Hand
    $b.Tag = $pair.id

    $g = New-Object Windows.Controls.Grid
    foreach ($w in 'Auto', '*', 'Auto') {
        $cd = New-Object Windows.Controls.ColumnDefinition
        $cd.Width = (New-Object Windows.GridLengthConverter).ConvertFromString($w)
        $g.ColumnDefinitions.Add($cd)
    }

    $dot = New-Object Windows.Shapes.Ellipse
    $dot.Width = 8; $dot.Height = 8; $dot.Fill = B '#636366'
    $dot.Margin = New-Object Windows.Thickness(2, 0, 10, 0)
    $dot.VerticalAlignment = 'Center'

    $sp = New-Object Windows.Controls.StackPanel
    [Windows.Controls.Grid]::SetColumn($sp, 1)
    $t1 = New-Object Windows.Controls.TextBlock
    $t1.Text = $pair.name; $t1.FontSize = 13; $t1.FontWeight = 'SemiBold'
    $t1.Foreground = B '#F5F5F7'; $t1.TextTrimming = 'CharacterEllipsis'
    $t2 = New-Object Windows.Controls.TextBlock
    $t2.Text = "$(Get-PathLabel $pair.source)  →  $(Get-PathLabel $pair.backup)"
    $t2.FontSize = 11; $t2.Foreground = B '#8E8E93'; $t2.TextTrimming = 'CharacterEllipsis'
    $t2.Margin = New-Object Windows.Thickness(0, 1, 0, 0)
    [void]$sp.Children.Add($t1); [void]$sp.Children.Add($t2)

    $lk = New-Object Windows.Controls.TextBlock
    $lk.FontFamily = $ICON_FONT; $lk.Text = [string][char]0xE72E; $lk.FontSize = 11
    $lk.Foreground = B '#8E8E93'; $lk.VerticalAlignment = 'Center'
    $lk.Margin = New-Object Windows.Thickness(8, 0, 2, 0)
    $lk.Visibility = if ($pair.locked) { 'Visible' } else { 'Collapsed' }
    [Windows.Controls.Grid]::SetColumn($lk, 2)

    [void]$g.Children.Add($dot); [void]$g.Children.Add($sp); [void]$g.Children.Add($lk)
    $b.Child = $g

    $b.Add_MouseLeftButtonUp({ Select-Pair $this.Tag })
    $b.Add_MouseEnter({ if ($this.Tag -ne $script:selectedId) { $this.Background = B '#2C2C2F' } })
    $b.Add_MouseLeave({ if ($this.Tag -ne $script:selectedId) { $this.Background = [Windows.Media.Brushes]::Transparent } })

    $script:sideItems[$pair.id] = @{ Border = $b; Dot = $dot }
    $b
}

function Update-Sidebar {
    $PairPanel.Children.Clear()
    $script:sideItems = @{}
    foreach ($p in @($script:config.pairs)) { [void]$PairPanel.Children.Add((New-SideItem $p)) }
    $pairs = @($script:config.pairs)
    if (-not (Get-SelectedPair) -and $pairs.Count -gt 0) { $script:selectedId = $pairs[0].id }
    Select-Pair $script:selectedId
}

function Select-Pair([string]$id) {
    $script:selectedId = $id
    foreach ($k in @($script:sideItems.Keys)) {
        $script:sideItems[$k].Border.Background = if ($k -eq $id) { B '#38383C' } else { [Windows.Media.Brushes]::Transparent }
    }
    Update-Detail
}

function Update-Detail {
    $p = Get-SelectedPair
    if (-not $p) {
        $EmptyView.Visibility = 'Visible'
        $DetailView.Visibility = 'Collapsed'
        return
    }
    $EmptyView.Visibility = 'Collapsed'
    $DetailView.Visibility = 'Visible'
    $PairName.Text = $p.name
    $SrcPath.Text = $p.source
    $DstPath.Text = $p.backup
    if ($p.locked) {
        $LockBadge.Visibility = 'Visible'
        $PairSub.Text = "Bloqueada · protegida contra cambios accidentales"
        $BtnEdit.Visibility = 'Collapsed'
        $BtnDelete.Visibility = 'Collapsed'
        $BtnLock.Content = "Desbloquear…"
    } else {
        $LockBadge.Visibility = 'Collapsed'
        $PairSub.Text = "Sin bloquear · puedes editarla o eliminarla"
        $BtnEdit.Visibility = 'Visible'
        $BtnDelete.Visibility = 'Visible'
        $BtnLock.Content = "Bloquear"
    }
    $Out.Text = if ($script:buffers.ContainsKey($p.id)) { $script:buffers[$p.id].ToString() } else { "" }
    $Out.ScrollToEnd()
    Update-SwitchFromCache
}

function Update-SwitchFromCache {
    $sel = Get-SelectedPair
    if (-not $sel) { return }
    $on = [bool]$script:running[$sel.id]
    $AutoSwitch.IsChecked = $on
    $AutoStatus.Text = if ($on) { "Activa · copia cambios cada minuto, sin borrar" }
                       else {
                           switch (Get-PairAvailability $sel) {
                               'SourceMissing' { "La carpeta a respaldar no está disponible" }
                               'BackupMissing' { "El disco del backup no está conectado" }
                               default { if ($sel.autoSync) { "Iniciando…" } else { "Desactivada" } }
                           }
                       }
}

# Tambien reanuda las sincronizaciones activadas que se detuvieron (p. ej. al reconectar un disco).
function Update-Status {
    $procs = @(Get-RobocopyProcesses)
    if ((Start-AllAutoSync $script:config $PATHS.LogDir $procs) -gt 0) {
        Start-Sleep -Milliseconds 300
        $procs = @(Get-RobocopyProcesses)
    }
    foreach ($p in @($script:config.pairs)) {
        $on = @(Get-PairAutoSyncProcess $p $procs).Count -gt 0
        $script:running[$p.id] = $on
        $item = $script:sideItems[$p.id]
        if ($item) { $item.Dot.Fill = if ($on) { B '#30D158' } else { B '#636366' } }
    }
    if ($script:statusHold -gt 0) { $script:statusHold--; return }
    Update-SwitchFromCache
}

# ---------- Progreso y tareas ----------
$barTT = New-Object Windows.Media.TranslateTransform
$BarFill.RenderTransform = $barTT
$barAnim = New-Object Windows.Media.Animation.DoubleAnimation(-140, 900, [Windows.Duration][TimeSpan]::FromSeconds(1.4))
$barAnim.RepeatBehavior = [Windows.Media.Animation.RepeatBehavior]::Forever
$ease = New-Object Windows.Media.Animation.SineEase
$ease.EasingMode = 'EaseInOut'
$barAnim.EasingFunction = $ease

function Set-Busy($busy) {
    foreach ($b in $BtnMirror, $BtnRestore, $BtnAdd, $BtnAddEmpty, $BtnEdit, $BtnLock, $BtnDelete) { $b.IsEnabled = -not $busy }
    if ($busy) {
        $BarTrack.Visibility = 'Visible'
        $barTT.BeginAnimation([Windows.Media.TranslateTransform]::XProperty, $barAnim)
    } else {
        $barTT.BeginAnimation([Windows.Media.TranslateTransform]::XProperty, $null)
        $BarTrack.Visibility = 'Hidden'
    }
}

$script:job = $null
function Start-Robo([string]$pairId, [string[]]$roboArgs, [bool]$stream, [scriptblock]$onDone) {
    if ($script:job) { Write-Out "Ya hay una operación en curso." $pairId; return }
    $log = Join-Path $env:TEMP ("stf_" + [guid]::NewGuid().ToString('N') + ".log")
    $argString = ConvertTo-ArgumentString ($roboArgs + @('/NP', '/NJH', "/UNILOG:$log"))
    try {
        $p = Start-Process robocopy -ArgumentList $argString -WindowStyle Hidden -PassThru -ErrorAction Stop
    } catch {
        Write-Out "No se pudo iniciar robocopy: $($_.Exception.Message)" $pairId
        return
    }
    $null = $p.Handle
    $script:job = @{ Proc = $p; Log = $log; Pos = 0; Stream = $stream; OnDone = $onDone; PairId = $pairId }
    Set-Busy $true
}

# Lee solo bytes completos de UTF-16 para no partir un caracter a la mitad.
function Read-JobLog {
    $j = $script:job
    if (-not (Test-Path -LiteralPath $j.Log)) { return }
    try {
        $fs = [IO.File]::Open($j.Log, 'Open', 'Read', 'ReadWrite')
        try {
            $count = $fs.Length - $j.Pos
            $count -= $count % 2
            if ($count -le 0) { return }
            [void]$fs.Seek($j.Pos, 'Begin')
            $bytes = New-Object byte[] $count
            $read = $fs.Read($bytes, 0, $count)
            $read -= $read % 2
            $j.Pos += $read
            $txt = [Text.Encoding]::Unicode.GetString($bytes, 0, $read).TrimStart([char]0xFEFF)
            if ($txt.Trim()) { Add-Out $txt $j.PairId }
        } finally { $fs.Close() }
    } catch { }
}

function Show-Result($code, $pairId) {
    if (Test-RobocopySuccess $code) { Write-Out "✓ Terminado correctamente." $pairId }
    else { Write-Out "✗ Terminado con errores (código $code). Revisa que los discos estén conectados y que tengas permisos." $pairId }
}

function Test-PairFolders($p) {
    if (-not (Test-Path -LiteralPath $p.source)) { Write-Out "No se encuentra la carpeta a respaldar: $($p.source)" $p.id; return $false }
    if (-not (Test-Path -LiteralPath $p.backup)) { Write-Out "No se encuentra la carpeta de backup: $($p.backup)" $p.id; return $false }
    $true
}

# ---------- Dialogos ----------
function Show-Overlay($panel) {
    $ConfirmPanel.Visibility = 'Collapsed'
    $EditorPanel.Visibility = 'Collapsed'
    $panel.Visibility = 'Visible'
    $Overlay.Opacity = 0
    $Overlay.Visibility = 'Visible'
    $fade = New-Object Windows.Media.Animation.DoubleAnimation(0, 1, [Windows.Duration][TimeSpan]::FromMilliseconds(160))
    $Overlay.BeginAnimation([Windows.UIElement]::OpacityProperty, $fade)
}
function Hide-Overlay {
    $cdTimer.Stop()
    $Overlay.BeginAnimation([Windows.UIElement]::OpacityProperty, $null)
    $Overlay.Visibility = 'Collapsed'
}

# Cada dialogo lleva su propio contexto (Ctx) y la accion lo recibe como parametro,
# asi un dialogo nunca actua sobre la sincronizacion de otro.
$script:confirm = @{ Action = $null; Ctx = $null; Required = $null; Countdown = 0; OkText = '' }
function Show-Confirm {
    param(
        [string]$Title,
        [string]$Text,
        [string]$OkText = "Aceptar",
        [bool]$Destructive = $false,
        [scriptblock]$Action,
        [hashtable]$Ctx = @{},
        [string]$TypeToConfirm,
        [int]$Countdown = 0
    )
    $DlgTitle.Text = $Title
    $DlgText.Text = $Text
    $DlgOk.Background = if ($Destructive) { B '#FF453A' } else { B '#0A84FF' }
    $script:confirm = @{ Action = $Action; Ctx = $Ctx; Required = $TypeToConfirm; Countdown = $Countdown; OkText = $OkText }
    $DlgInput.Text = ''
    $vis = if ($TypeToConfirm) { 'Visible' } else { 'Collapsed' }
    $DlgInputHint.Visibility = $vis
    $DlgInput.Visibility = $vis
    $DlgInputHint.Text = "Escribe «$TypeToConfirm» para continuar:"
    Update-DlgOk
    Show-Overlay $ConfirmPanel
    if ($Countdown -gt 0) { $cdTimer.Start() }
    if ($TypeToConfirm) { [void]$DlgInput.Focus() }
}

function Update-DlgOk {
    $c = $script:confirm
    $typedOk = (-not $c.Required) -or (Test-UnlockConfirmation $c.Required $DlgInput.Text)
    $DlgOk.IsEnabled = $typedOk -and $c.Countdown -le 0
    $DlgOk.Content = if ($c.Countdown -gt 0) { "$($c.OkText) ($($c.Countdown))" } else { $c.OkText }
}

$cdTimer = New-Object Windows.Threading.DispatcherTimer
$cdTimer.Interval = [TimeSpan]::FromSeconds(1)
$cdTimer.Add_Tick({
    $script:confirm.Countdown--
    Update-DlgOk
    if ($script:confirm.Countdown -le 0) { $cdTimer.Stop() }
})

function Close-Confirm {
    Hide-Overlay
    if ($ConfirmPanel.Visibility -eq 'Visible') { Write-Out "Cancelado." }
}

$DlgInput.Add_TextChanged({ Update-DlgOk })
$DlgCancel.Add_Click({ Close-Confirm })
$DlgOk.Add_Click({
    if (-not $DlgOk.IsEnabled) { return }
    $a = $script:confirm.Action
    $ctx = $script:confirm.Ctx
    Hide-Overlay
    if ($a) { & $a $ctx }
})

$win.Add_KeyDown({
    param($s, $e)
    if ($e.Key -eq 'Escape' -and $Overlay.Visibility -eq 'Visible') { Close-Confirm }
    elseif ($e.Key -eq 'F11') { Switch-Maximize }
})

# ---------- Editor ----------
$script:editingId = $null
function Show-Editor($pair) {
    $script:editingId = if ($pair) { $pair.id } else { $null }
    $EdTitle.Text = if ($pair) { "Editar sincronización" } else { "Nueva sincronización" }
    $EdName.Text = if ($pair) { $pair.name } else { "" }
    $EdSrc.Text = if ($pair) { $pair.source } else { "" }
    $EdDst.Text = if ($pair) { $pair.backup } else { "" }
    $EdAuto.IsChecked = if ($pair) { [bool]$pair.autoSync } else { $true }
    $EdLock.IsChecked = if ($pair) { [bool]$pair.locked } else { $true }
    $EdError.Visibility = 'Collapsed'
    Show-Overlay $EditorPanel
    [void]$EdName.Focus()
}

$EdPickSrc.Add_Click({
    $r = [FolderPicker]::Pick($script:hwnd, "Elige la carpeta que quieres respaldar", $EdSrc.Text)
    if ($r) {
        $EdSrc.Text = Get-NormalizedPath $r
        if (-not $EdName.Text.Trim()) { $EdName.Text = Get-PathLabel $r }
    }
})
$EdPickDst.Add_Click({
    $r = [FolderPicker]::Pick($script:hwnd, "Elige dónde guardar el backup", $EdDst.Text)
    if ($r) { $EdDst.Text = Get-NormalizedPath $r }
})
$EdCancel.Add_Click({ Hide-Overlay })

$EdSave.Add_Click({
    $err = Test-PairInput -Name $EdName.Text -Source $EdSrc.Text -Backup $EdDst.Text -Pairs $script:config.pairs -ExcludeId $script:editingId
    if (-not $err) {
        try { New-Item -ItemType Directory -Path $EdDst.Text -Force -ErrorAction Stop | Out-Null }
        catch { $err = "No se pudo crear la carpeta de backup: $($_.Exception.Message)" }
    }
    if ($err) { $EdError.Text = $err; $EdError.Visibility = 'Visible'; return }

    $restart = $true
    if ($script:editingId) {
        $p = Get-SyncPair $script:config $script:editingId
        $pathsChanged = ($p.source -ine (Get-NormalizedPath $EdSrc.Text)) -or ($p.backup -ine (Get-NormalizedPath $EdDst.Text))
        $autoChanged = [bool]$p.autoSync -ne [bool]$EdAuto.IsChecked
        $restart = $pathsChanged -or $autoChanged
        if ($restart) { Stop-PairAutoSync $p }
        try { Update-SyncPair -Config $script:config -Id $p.id -Name $EdName.Text -Source $EdSrc.Text -Backup $EdDst.Text }
        catch { $EdError.Text = $_.Exception.Message; $EdError.Visibility = 'Visible'; return }
        $p.autoSync = [bool]$EdAuto.IsChecked
        $p.locked = [bool]$EdLock.IsChecked
    } else {
        $p = New-SyncPair -Name $EdName.Text -Source $EdSrc.Text -Backup $EdDst.Text -Locked ([bool]$EdLock.IsChecked) -AutoSync ([bool]$EdAuto.IsChecked)
        Add-SyncPair $script:config $p
    }
    if (-not (Save-Config)) { return }
    Hide-Overlay
    $script:selectedId = $p.id
    Update-Sidebar
    Write-Out "Configuración guardada."
    if ($p.autoSync -and $restart) {
        if (Start-PairAutoSync $p $PATHS.LogDir) {
            Write-Out "Sincronización automática activada. La primera copia puede tardar según el tamaño de la carpeta."
        } else {
            Write-Out "No se pudo iniciar la sincronización automática. Revisa que las carpetas estén disponibles."
        }
        $script:statusHold = 2
    }
})

# ---------- Acciones ----------
$BtnAdd.Add_Click({ Show-Editor $null })
$BtnAddEmpty.Add_Click({ Show-Editor $null })
$BtnEdit.Add_Click({ $p = Get-SelectedPair; if ($p -and -not $p.locked) { Show-Editor $p } })

$BtnLock.Add_Click({
    $p = Get-SelectedPair
    if (-not $p) { return }
    if (-not $p.locked) {
        Lock-SyncPair $script:config $p.id
        if (Save-Config) { Write-Out "Configuración bloqueada." }
        Update-Sidebar
        return
    }
    Show-Confirm -Title "¿Desbloquear «$($p.name)»?" `
        -Text "Al desbloquearla podrás cambiar sus carpetas o eliminarla. Tus archivos no se tocan." `
        -OkText "Continuar" -Ctx @{ Id = $p.id; Name = $p.name } -Action {
            param($ctx)
            Show-Confirm -Title "Confirma que quieres desbloquearla" `
                -Text "Esta configuración está protegida contra cambios accidentales." `
                -OkText "Continuar" -Ctx $ctx -TypeToConfirm $ctx.Name -Action {
                    param($ctx)
                    Show-Confirm -Title "Última confirmación" `
                        -Text "«$($ctx.Name)» quedará desbloqueada y podrá modificarse o eliminarse." `
                        -OkText "Desbloquear" -Destructive $true -Countdown 5 -Ctx $ctx -Action {
                            param($ctx)
                            Unlock-SyncPair $script:config $ctx.Id
                            if (Save-Config) { Write-Out "Configuración desbloqueada." $ctx.Id }
                            Update-Sidebar
                        }
                }
        }
})

$BtnDelete.Add_Click({
    $p = Get-SelectedPair
    if (-not $p -or $p.locked) { return }
    Show-Confirm -Title "¿Eliminar «$($p.name)»?" `
        -Text "Se detendrá su sincronización automática y se quitará de la lista. No se borrará ningún archivo de tus carpetas." `
        -OkText "Eliminar" -Destructive $true -Ctx @{ Id = $p.id } -Action {
            param($ctx)
            $p = Get-SyncPair $script:config $ctx.Id
            if ($p) {
                try { Remove-SyncPair $script:config $p.id } catch { Write-Out $_.Exception.Message; return }
                Stop-PairAutoSync $p
                [void](Save-Config)
            }
            $script:buffers.Remove($ctx.Id)
            $script:running.Remove($ctx.Id)
            $script:selectedId = $null
            Update-Sidebar
        }
})

$BtnOpenSrc.Add_Click({ $p = Get-SelectedPair; if ($p) { Start-Process explorer.exe "`"$($p.source)`"" } })
$BtnOpenDst.Add_Click({ $p = Get-SelectedPair; if ($p) { Start-Process explorer.exe "`"$($p.backup)`"" } })
$BtnLog.Add_Click({
    $p = Get-SelectedPair
    if (-not $p) { return }
    $log = Get-PairLogPath $p $PATHS.LogDir
    if (Test-Path -LiteralPath $log) { Start-Process notepad.exe "`"$log`"" } else { Write-Out "Aún no hay historial." }
})
$BtnClear.Add_Click({
    $p = Get-SelectedPair
    if ($p) { $script:buffers[$p.id] = New-Object Text.StringBuilder }
    $Out.Clear()
})

$AutoSwitch.Add_Click({
    $p = Get-SelectedPair
    if (-not $p) { return }
    if ($AutoSwitch.IsChecked) {
        $p.autoSync = $true
        [void](Save-Config)
        if (Start-PairAutoSync $p $PATHS.LogDir) {
            $AutoStatus.Text = "Activa · copia cambios cada minuto, sin borrar"
            Write-Out "Sincronización automática activada."
        } else {
            $AutoSwitch.IsChecked = $false
            Update-SwitchFromCache
            Write-Out "Se activará sola en cuanto las carpetas estén disponibles."
        }
    } else {
        $p.autoSync = $false
        [void](Save-Config)
        Stop-PairAutoSync $p
        $AutoStatus.Text = "Desactivada"
        Write-Out "Sincronización automática desactivada."
    }
    $script:statusHold = 2
})

$BtnMirror.Add_Click({
    $p = Get-SelectedPair
    if (-not $p -or -not (Test-PairFolders $p)) { return }
    Write-Out "Analizando diferencias…"
    Start-Robo $p.id (Get-MirrorPreviewArgs $p.source $p.backup) $false {
        param($code, $log, $pairId)
        if (-not (Test-RobocopySuccess $code)) {
            Write-Out "✗ No se pudo analizar (código $code). No se hizo ningún cambio." $pairId
            return
        }
        $extras = @(Get-MirrorExtras (Get-Content -LiteralPath $log -Encoding Unicode -ErrorAction SilentlyContinue))
        $c = Get-MirrorConfirm $extras
        Write-Out "Análisis listo." $pairId
        Show-Confirm -Title $c.Title -Text $c.Text -OkText $c.OkText -Destructive $c.Destructive -Ctx @{ Id = $pairId } -Action {
            param($ctx)
            $p = Get-SyncPair $script:config $ctx.Id
            if (-not $p -or -not (Test-PairFolders $p)) { return }
            Write-Out "" $p.id
            Write-Out "— Espejo completo  $(Get-Date -Format 'HH:mm:ss') —" $p.id
            Start-Robo $p.id (Get-MirrorArgs $p.source $p.backup) $true { param($code, $log, $pairId) Show-Result $code $pairId }
        }
    }
})

$BtnRestore.Add_Click({
    $p = Get-SelectedPair
    if (-not $p -or -not (Test-PairFolders $p)) { return }
    Show-Confirm -Title "¿Restaurar desde el backup?" `
        -Text "Se copiará a «$(Get-PathLabel $p.source)» todo lo que falte o sea más nuevo en el backup. No se borrará nada." `
        -OkText "Restaurar" -Ctx @{ Id = $p.id } -Action {
            param($ctx)
            $p = Get-SyncPair $script:config $ctx.Id
            if (-not $p -or -not (Test-PairFolders $p)) { return }
            Write-Out "" $p.id
            Write-Out "— Restaurar desde el backup  $(Get-Date -Format 'HH:mm:ss') —" $p.id
            Start-Robo $p.id (Get-RestoreArgs $p.source $p.backup) $true { param($code, $log, $pairId) Show-Result $code $pairId }
        }
})

# ---------- Temporizador ----------
$script:tick = 0
$timer = New-Object Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds(400)
$timer.Add_Tick({
    if ($script:job) {
        if ($script:job.Stream) { Read-JobLog }
        if ($script:job.Proc.HasExited) {
            if ($script:job.Stream) { Read-JobLog }
            $j = $script:job
            $script:job = $null
            Set-Busy $false
            try { & $j.OnDone $j.Proc.ExitCode $j.Log $j.PairId }
            finally { Remove-Item -LiteralPath $j.Log -ErrorAction SilentlyContinue }
        }
    }
    $script:tick++
    if ($script:tick % 12 -eq 0) { Update-Status }
})

$win.Add_ContentRendered({
    Update-Status
    foreach ($p in @($script:config.pairs)) { Write-Out "$($INFO.Name) $($INFO.Version) listo." $p.id }
    $timer.Start()
})

Update-Sidebar
[void]$win.ShowDialog()
$mutex.ReleaseMutex()
