using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Noted;

public partial class MainWindow
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MidiPlayerMciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    private static extern bool MidiPlayerMciGetErrorString(int errorCode, StringBuilder buffer, int bufferLength);

    private sealed class MidiHeaderInfo
    {
        public int FormatType { get; init; }
        public int TrackCount { get; init; }
        public int Division { get; init; }
        public bool IsSmpte { get; init; }
        public long FileSizeBytes { get; init; }
        public bool IsRiff { get; init; }
        public bool HasMthd { get; init; }
    }

    private void ShowMidiPlayerDialog()
    {
        var dlg = new Window
        {
            Title = "MIDI Player",
            Width = 760,
            Height = 520,
            MinWidth = 560,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            AllowDrop = true
        };

        var alias = "noted_midi_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var isOpen = false;
        var isPlaying = false;
        var isPaused = false;
        var seekDragging = false;
        long lengthMs = 0;
        long positionMs = 0;

        var root = new DockPanel { Margin = new Thickness(12) };

        var fileRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtFile = new TextBox
        {
            IsReadOnly = true,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        Grid.SetColumn(txtFile, 0);
        var btnBrowse = new Button
        {
            Content = "Browse...",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(btnBrowse, 1);
        var btnUnload = new Button
        {
            Content = "Unload",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        Grid.SetColumn(btnUnload, 2);
        fileRow.Children.Add(txtFile);
        fileRow.Children.Add(btnBrowse);
        fileRow.Children.Add(btnUnload);
        DockPanel.SetDock(fileRow, Dock.Top);
        root.Children.Add(fileRow);

        var infoBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var txtInfo = new TextBlock
        {
            Text = "No file loaded.",
            FontFamily = new FontFamily("Consolas, Courier New"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray
        };
        infoBox.Child = txtInfo;
        DockPanel.SetDock(infoBox, Dock.Top);
        root.Children.Add(infoBox);

        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);

        var seekRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lblPosition = new TextBlock
        {
            Text = "00:00",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 48,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        Grid.SetColumn(lblPosition, 0);
        var seekSlider = new Slider
        {
            Minimum = 0,
            Maximum = 0,
            Value = 0,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = false
        };
        Grid.SetColumn(seekSlider, 1);
        var lblLength = new TextBlock
        {
            Text = "00:00",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 48,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(8, 0, 0, 0),
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        Grid.SetColumn(lblLength, 2);
        seekRow.Children.Add(lblPosition);
        seekRow.Children.Add(seekSlider);
        seekRow.Children.Add(lblLength);
        bottom.Children.Add(seekRow);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        var btnPlay = new Button
        {
            Content = "▶ Play",
            Padding = new Thickness(20, 8, 20, 8),
            FontSize = 14,
            Width = 110,
            IsEnabled = false
        };
        var btnPause = new Button
        {
            Content = "❚❚ Pause",
            Padding = new Thickness(20, 8, 20, 8),
            FontSize = 14,
            Width = 110,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        var btnStop = new Button
        {
            Content = "■ Stop",
            Padding = new Thickness(20, 8, 20, 8),
            FontSize = 14,
            Width = 110,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        var loopCheck = new CheckBox
        {
            Content = "Loop",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        controls.Children.Add(btnPlay);
        controls.Children.Add(btnPause);
        controls.Children.Add(btnStop);
        controls.Children.Add(loopCheck);
        bottom.Children.Add(controls);

        var lblStatus = new TextBlock
        {
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        bottom.Children.Add(lblStatus);

        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var btnClose = new Button
        {
            Content = "Close",
            Width = 90,
            IsCancel = true
        };
        closeRow.Children.Add(btnClose);
        bottom.Children.Add(closeRow);
        root.Children.Add(bottom);

        var dropHint = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromRgb(252, 252, 252)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        dropHint.Child = new TextBlock
        {
            Text = "Drop a MIDI file here, or click Browse...\nSupports .mid, .midi, .rmi (RIFF MIDI)",
            Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        root.Children.Add(dropHint);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        void SetStatus(string text, Brush? color = null)
        {
            lblStatus.Text = text;
            lblStatus.Foreground = color ?? Brushes.DimGray;
        }

        static string FormatTime(long ms)
        {
            if (ms < 0)
                ms = 0;
            var totalSeconds = ms / 1000;
            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }

        bool TryMci(string command, StringBuilder? returnValue, out int errorCode)
        {
            errorCode = MidiPlayerMciSendString(command, returnValue, returnValue?.Capacity ?? 0, IntPtr.Zero);
            return errorCode == 0;
        }

        string GetMciError(int errorCode)
        {
            var sb = new StringBuilder(256);
            MidiPlayerMciGetErrorString(errorCode, sb, sb.Capacity);
            return sb.ToString();
        }

        long QueryStatusNumber(string what)
        {
            var sb = new StringBuilder(64);
            if (!TryMci($"status {alias} {what}", sb, out _))
                return 0;
            return long.TryParse(sb.ToString().Trim(), out var value) ? value : 0;
        }

        string QueryStatusText(string what)
        {
            var sb = new StringBuilder(64);
            return TryMci($"status {alias} {what}", sb, out _) ? sb.ToString().Trim() : string.Empty;
        }

        static int SwapEndian32(int value)
        {
            var u = (uint)value;
            return (int)(((u & 0x000000FFu) << 24) |
                         ((u & 0x0000FF00u) << 8) |
                         ((u & 0x00FF0000u) >> 8) |
                         ((u & 0xFF000000u) >> 24));
        }

        static int SwapEndian16(ushort value)
            => ((value & 0xFF) << 8) | ((value & 0xFF00) >> 8);

        MidiHeaderInfo? TryParseMidiHeader(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);
                if (fs.Length < 14)
                    return new MidiHeaderInfo { FileSizeBytes = fi.Length };

                var firstFour = br.ReadBytes(4);
                var isRiff = firstFour.Length == 4 && firstFour[0] == 'R' && firstFour[1] == 'I' && firstFour[2] == 'F' && firstFour[3] == 'F';

                if (isRiff)
                {
                    br.ReadInt32(); // riff file size (little-endian)
                    br.ReadBytes(4); // 'RMID'
                    var foundData = false;
                    while (fs.Position + 8 <= fs.Length)
                    {
                        var chunkId = br.ReadBytes(4);
                        var chunkSize = br.ReadInt32(); // little-endian for RIFF
                        if (chunkId.Length < 4)
                            break;
                        if (chunkId[0] == 'd' && chunkId[1] == 'a' && chunkId[2] == 't' && chunkId[3] == 'a')
                        {
                            firstFour = br.ReadBytes(4);
                            foundData = true;
                            break;
                        }
                        var skip = chunkSize + (chunkSize % 2);
                        if (skip < 0 || fs.Position + skip > fs.Length)
                            break;
                        fs.Seek(skip, SeekOrigin.Current);
                    }

                    if (!foundData)
                        return new MidiHeaderInfo { FileSizeBytes = fi.Length, IsRiff = true };
                }

                if (firstFour.Length < 4 || firstFour[0] != 'M' || firstFour[1] != 'T' || firstFour[2] != 'h' || firstFour[3] != 'd')
                    return new MidiHeaderInfo { FileSizeBytes = fi.Length, IsRiff = isRiff };

                var headerLength = SwapEndian32(br.ReadInt32());
                if (headerLength < 6 || fs.Position + 6 > fs.Length)
                    return new MidiHeaderInfo { FileSizeBytes = fi.Length, IsRiff = isRiff, HasMthd = true };
                var format = SwapEndian16(br.ReadUInt16());
                var tracks = SwapEndian16(br.ReadUInt16());
                var division = SwapEndian16(br.ReadUInt16());
                var isSmpte = (division & 0x8000) != 0;

                return new MidiHeaderInfo
                {
                    FormatType = format,
                    TrackCount = tracks,
                    Division = division,
                    IsSmpte = isSmpte,
                    FileSizeBytes = fi.Length,
                    IsRiff = isRiff,
                    HasMthd = true
                };
            }
            catch
            {
                return null;
            }
        }

        void UpdateButtons()
        {
            btnPlay.IsEnabled = isOpen && !isPlaying;
            btnPause.IsEnabled = isOpen && isPlaying && !isPaused;
            btnStop.IsEnabled = isOpen && (isPlaying || isPaused);
            btnUnload.IsEnabled = isOpen;
            seekSlider.IsEnabled = isOpen && lengthMs > 0;
        }

        void UpdateInfo(string path, MidiHeaderInfo? info)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"File: {Path.GetFileName(path)}");
            if (info == null)
            {
                sb.Append("(unable to read MIDI file)");
            }
            else
            {
                sb.Append($"Size: {info.FileSizeBytes:N0} bytes");
                if (info.IsRiff)
                    sb.Append("  (RIFF MIDI)");
                sb.AppendLine();
                if (info.HasMthd)
                {
                    var formatLabel = info.FormatType switch
                    {
                        0 => "0 (single track)",
                        1 => "1 (multi-track, simultaneous)",
                        2 => "2 (multi-song / multi-sequence)",
                        _ => info.FormatType.ToString()
                    };
                    sb.AppendLine($"Format: {formatLabel}");
                    sb.AppendLine($"Tracks: {info.TrackCount}");
                    if (info.IsSmpte)
                    {
                        var negFps = -(sbyte)((info.Division >> 8) & 0xFF);
                        var subFrame = info.Division & 0xFF;
                        sb.Append($"Division: SMPTE {negFps} fps, {subFrame} ticks/frame");
                    }
                    else
                    {
                        sb.Append($"Division: {info.Division} ticks per quarter note (PPQN)");
                    }
                }
                else
                {
                    sb.Append("(no MThd header found)");
                }
            }
            txtInfo.Text = sb.ToString().TrimEnd();
            txtInfo.Foreground = Brushes.Black;
        }

        void CloseDevice()
        {
            timer.Stop();
            if (isOpen)
            {
                TryMci($"stop {alias}", null, out _);
                TryMci($"close {alias}", null, out _);
            }
            isOpen = false;
            isPlaying = false;
            isPaused = false;
        }

        bool LoadFile(string path)
        {
            CloseDevice();
            if (!File.Exists(path))
            {
                SetStatus($"File not found: {path}", Brushes.IndianRed);
                return false;
            }

            var openCommand = $"open \"{path}\" type sequencer alias {alias}";
            if (!TryMci(openCommand, null, out var err))
            {
                SetStatus($"Failed to open MIDI: {GetMciError(err)}", Brushes.IndianRed);
                return false;
            }

            isOpen = true;
            TryMci($"set {alias} time format milliseconds", null, out _);
            lengthMs = QueryStatusNumber("length");
            positionMs = 0;

            seekSlider.Maximum = Math.Max(1, lengthMs);
            seekSlider.Value = 0;
            lblLength.Text = FormatTime(lengthMs);
            lblPosition.Text = "00:00";

            txtFile.Text = path;
            dropHint.Visibility = Visibility.Collapsed;

            var info = TryParseMidiHeader(path);
            UpdateInfo(path, info);
            UpdateButtons();
            SetStatus($"Loaded {Path.GetFileName(path)} ({FormatTime(lengthMs)})");
            return true;
        }

        void Play()
        {
            if (!isOpen) return;
            var command = isPaused ? $"resume {alias}" : $"play {alias}";
            if (!TryMci(command, null, out var err))
            {
                SetStatus($"Play failed: {GetMciError(err)}", Brushes.IndianRed);
                return;
            }
            isPlaying = true;
            isPaused = false;
            timer.Start();
            UpdateButtons();
            SetStatus("Playing.");
        }

        void Pause()
        {
            if (!isOpen || !isPlaying) return;
            if (!TryMci($"pause {alias}", null, out var err))
            {
                SetStatus($"Pause failed: {GetMciError(err)}", Brushes.IndianRed);
                return;
            }
            isPlaying = false;
            isPaused = true;
            timer.Stop();
            UpdateButtons();
            SetStatus("Paused.");
        }

        void Stop()
        {
            if (!isOpen) return;
            TryMci($"stop {alias}", null, out _);
            TryMci($"seek {alias} to start", null, out _);
            isPlaying = false;
            isPaused = false;
            timer.Stop();
            positionMs = 0;
            seekSlider.Value = 0;
            lblPosition.Text = "00:00";
            UpdateButtons();
            SetStatus("Stopped.");
        }

        void PerformSeek(long target)
        {
            if (!isOpen) return;
            if (target < 0) target = 0;
            if (lengthMs > 0 && target > lengthMs) target = lengthMs;

            var wasPlaying = isPlaying;
            TryMci($"stop {alias}", null, out _);
            TryMci($"seek {alias} to {target}", null, out _);
            if (wasPlaying)
            {
                if (TryMci($"play {alias}", null, out _))
                {
                    isPlaying = true;
                    isPaused = false;
                    timer.Start();
                }
            }
            else
            {
                isPlaying = false;
                isPaused = false;
                positionMs = target;
                lblPosition.Text = FormatTime(target);
            }
            UpdateButtons();
        }

        void OnTick()
        {
            if (!isOpen)
                return;

            positionMs = QueryStatusNumber("position");
            if (!seekDragging)
            {
                seekSlider.Value = Math.Min(positionMs, seekSlider.Maximum);
                lblPosition.Text = FormatTime(positionMs);
            }

            var mode = QueryStatusText("mode");
            if (isPlaying && mode == "stopped")
            {
                isPlaying = false;
                isPaused = false;
                timer.Stop();
                UpdateButtons();

                if (loopCheck.IsChecked == true)
                {
                    TryMci($"seek {alias} to start", null, out _);
                    Play();
                    SetStatus("Looping.");
                }
                else
                {
                    seekSlider.Value = 0;
                    lblPosition.Text = "00:00";
                    SetStatus("Finished.");
                }
            }
        }

        timer.Tick += (_, _) => OnTick();

        btnBrowse.Click += (_, _) =>
        {
            var ofd = new OpenFileDialog
            {
                Title = "Open MIDI file",
                Filter = "MIDI files (*.mid;*.midi;*.rmi)|*.mid;*.midi;*.rmi|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog(dlg) == true)
                LoadFile(ofd.FileName);
        };

        btnUnload.Click += (_, _) =>
        {
            CloseDevice();
            lengthMs = 0;
            positionMs = 0;
            seekSlider.Maximum = 0;
            seekSlider.Value = 0;
            txtFile.Text = string.Empty;
            lblLength.Text = "00:00";
            lblPosition.Text = "00:00";
            txtInfo.Text = "No file loaded.";
            txtInfo.Foreground = Brushes.DimGray;
            dropHint.Visibility = Visibility.Visible;
            UpdateButtons();
            SetStatus("Unloaded.");
        };

        btnPlay.Click += (_, _) => Play();
        btnPause.Click += (_, _) => Pause();
        btnStop.Click += (_, _) => Stop();

        seekSlider.PreviewMouseDown += (_, _) =>
        {
            if (isOpen && lengthMs > 0)
                seekDragging = true;
        };
        seekSlider.PreviewMouseUp += (_, _) =>
        {
            if (!seekDragging)
                return;
            seekDragging = false;
            PerformSeek((long)seekSlider.Value);
        };
        seekSlider.LostMouseCapture += (_, _) =>
        {
            if (!seekDragging)
                return;
            seekDragging = false;
            PerformSeek((long)seekSlider.Value);
        };
        seekSlider.ValueChanged += (_, _) =>
        {
            if (seekDragging)
                lblPosition.Text = FormatTime((long)seekSlider.Value);
        };

        static bool IsMidiPath(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mid" || ext == ".midi" || ext == ".rmi";
        }

        dlg.PreviewDragOver += (_, e) =>
        {
            var ok = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        dlg.Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
                return;
            var first = files[0];
            if (!IsMidiPath(first))
            {
                SetStatus($"Not a MIDI file: {Path.GetFileName(first)}", Brushes.IndianRed);
                return;
            }
            LoadFile(first);
        };

        dlg.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && isOpen)
            {
                if (isPlaying)
                    Pause();
                else
                    Play();
                e.Handled = true;
            }
        };

        btnClose.Click += (_, _) => dlg.Close();
        dlg.Closed += (_, _) => CloseDevice();

        UpdateButtons();
        dlg.Content = root;
        dlg.ShowDialog();
    }
}
