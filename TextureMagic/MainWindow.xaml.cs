using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using ImageMagick;
using Microsoft.Win32;
using Path = System.IO.Path;
using Window = System.Windows.Window;

namespace TextureMagic
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public const int MAX_THREADS = 4;
        private object progressLock = new object();
        private object _geometryLock = new Object();
        private bool _fillBackGround = false;
        private bool _rearrangeTexture = false;
        private bool _isWorking = false;
        private int _selectedResolition = 512;
        private int _selectedResolutionHeight = 512;
        private int _totalProgress = 0;
        private int _currentProgress = 0;
        private string _lastPath;
        private bool _geometryInitialized = false;
        private static readonly MagickColor _backgroundColor = new MagickColor(0x1f, 0x20, 0x20, 0xff);
        private bool _squareTexture = true;
        private MagickGeometry[] _geometry;
        private List<IConnectedComponent<byte>> _connectedComponents = new ();
        private IMagickImage[] _masks;
        private CancellationTokenSource _cancellationTokenSource;
        private CompressionMethod _selectedCompression = CompressionMethod.DXT1;
        private FileEntry[] _files = Array.Empty<FileEntry>();
        private readonly BackgroundWorker _worker = new BackgroundWorker();
        private int _textureBorder = 0;

        struct FileEntry
        {
            public int Index { get; set; }
            public string Path { get; set; }
        }
        
        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "Texture Magic by Dustin Slane ( v 0.6.2 )"; 
            Progress.Value = 0;
            _cancellationTokenSource = new CancellationTokenSource();
            _worker.DoWork += WorkerOnDoWork;
            _worker.RunWorkerCompleted += RunWorkerCompleted;
            _worker.WorkerSupportsCancellation = true;
        }
        
        private void RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {

        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderPicker();
            this.FilesFound.Text = $"{_files.Length} files found";
        }

        private void OpenFolderPicker()
        {
            var filepicker = new OpenFileDialog();
            filepicker.Multiselect = true;
            filepicker.Filter = "Texture Files (*.png, *.dds, *.ytd)|*.png;*.dds;*.ytd";
            filepicker.InitialDirectory = string.IsNullOrEmpty(_lastPath)? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : _lastPath;

            if (filepicker.ShowDialog() == true)
            {
                _files = new FileEntry[filepicker.FileNames.Length];
                for (var i = 0; i < filepicker.FileNames.Length; i++)
                {
                    _files[i] = new FileEntry
                    {
                        Index = i,
                        Path = filepicker.FileNames[i]
                    };
                }
                if (_files.Length > 0)
                {
                    _lastPath = Path.GetDirectoryName(_files[0].Path) ?? filepicker.InitialDirectory;
                }
                else
                {
                    _lastPath = "Operation cancelled.";
                }
                SelectedPath.Text = _lastPath;

            }
        }

        private void FilesFound_SelectionChanged(object sender, RoutedEventArgs e)
        {

        }

        private void FillBackgroundCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _fillBackGround = true;
            // this.BackgroundColor.Visibility = Visibility.Visible;
        }

        private void FillBackgroundCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _fillBackGround = false;
            // this.BackgroundColor.Visibility = Visibility.Collapsed;
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            // _dispatcher = Window.Current.Dispatcher;

            

            if (_isWorking)
            {
                _cancellationTokenSource.Cancel();
                // _worker.CancelAsync();
                StartButton.IsEnabled = false;
                StartButton.Content = "BUSY";
                SetStatus("Canceling...");
            }
            else
            {
                _cancellationTokenSource = new CancellationTokenSource();
                StartButton.Content = "CANCEL";
                StartButton.IsEnabled = true;
                _worker.RunWorkerAsync();
            }
        }

        private void SetStatus(string status)
        {
            Dispatcher.Invoke(() => { CurrentStatus.Text = status; });
        }

        private async void WorkerOnDoWork(object? sender, DoWorkEventArgs e)
        {
            _totalProgress = _files.Length;
            _isWorking = true;
            SetJobTarget(_files.Length);
            
            // Limit to MAX_THREADS because while taking over the whole processor is *fast*.... I would be
            // taking over the whole processor and using this while you have OBS going is... challenging.
            var opts = new ParallelOptions();
            int processors = Environment.ProcessorCount;
            if (processors > MAX_THREADS)
            {
                processors = MAX_THREADS;
            }

            opts.MaxDegreeOfParallelism = processors;
            opts.CancellationToken = _cancellationTokenSource.Token;

            try
            {
                await Parallel.ForEachAsync(_files, opts, ProcessFile);
                Dispatcher.Invoke(() =>
                {
                    _ = MessageBox.Show($"Optimized {_currentProgress} textures!", "Texture Magic has finished!",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (TaskCanceledException exception)
            {
                Dispatcher.Invoke(() =>
                {
                    _ = MessageBox.Show($"Magic Dampened. {_currentProgress} textures were optimized.",
                        "Texture Magic was canceled!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                });
            }
            catch (Exception error)
            {
                Dispatcher.Invoke(() =>
                {
                    _ = MessageBox.Show($"You rolled a nat 1! \n\n" + error.Message + "\n\n" + "",
                        "WILD MAGIC!", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    // Turn the start button back in to a start button
                    StartButton.Content = "START";
                });
                _isWorking = false;
                _geometryInitialized = false;
                SetStatus("Ready");
            }
        }

        private void SetJobTarget(int count)
        {
            _totalProgress = count;
            _currentProgress = 0;
        }
        
        private void MarkJobDone()
        {
            lock (progressLock)
            {
                Dispatcher.Invoke(() =>
                {
                    float current = ++_currentProgress;
                    float perc = current / (float)_totalProgress;
                    int val = (int)(perc * 100.0f);
                    this.Progress.Value = val;
                });
            }
        }

        private async ValueTask ProcessFile(FileEntry entry, CancellationToken t)
        {
            try
            {
                if (t.IsCancellationRequested) return;

                string path = entry.Path.ToLowerInvariant();

                if (path.EndsWith(".png"))
                {
                    await ProcessPng(entry);
                } else if (path.EndsWith(".dds"))
                {
                    await ProcessDds(entry);
                } else if (path.EndsWith(".ytd"))
                {
                    await ProcessYtd(entry);
                }
                
                
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            MarkJobDone();
        }

        private async Task ProcessYtd(FileEntry entry)
        {
            try
            {
                var ytd = new TextureDictionary(entry.Path);
                await ytd.Load();

                List<Texture> editedTextures = new List<Texture>();
                foreach (var texture in ytd.Textures)
                {
                    var dds = DDSIO.GetDDSFile(texture);
                    var image = ProcessImage(new MagickImage(dds), Path.GetFileName(entry.Path));
                    var editedTexture = DDSIO.GetTexture(image.ToByteArray());
                    editedTexture.Name = texture.Name;
                    editedTextures.Add(editedTexture);
                }
                
                ytd.Rebuild(editedTextures);
                await ytd.SaveToDisk();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }


        private async Task ProcessPng(FileEntry entry)
        {
            try
            {
                var openedFile = await File.ReadAllBytesAsync(entry.Path);
                var img = new MagickImage(openedFile, MagickFormat.Png);
                img.Format = MagickFormat.Dds;
                using var imageFromFile = ProcessImage(img, Path.GetFileName(entry.Path));
                await File.WriteAllBytesAsync($"{Path.GetDirectoryName(entry.Path)}\\{entry.Index}.dds", imageFromFile.ToByteArray());
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private async Task ProcessDds(FileEntry entry)
        {
            try
            {
                var openedFile = await File.ReadAllBytesAsync(entry.Path);
                using var imageFromFile = ProcessImage(new MagickImage(openedFile), Path.GetFileName(entry.Path));
                await File.WriteAllBytesAsync($"{Path.GetDirectoryName(entry.Path)}\\{entry.Index}.dds", imageFromFile.ToByteArray());
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        
        private IMagickImage ProcessImage(MagickImage img, string name)
        {
            MagickImage processedImage;
            if (_rearrangeTexture)
            {
                SetStatus($"Rearranging {name}");
                processedImage = CutImageIntoPieces(img, name);
            }
            else
            {
                SetStatus($"Optimizing {name}");
                processedImage = NormalOptimization(img, name);
            }
            
            processedImage.Settings.Compression = _selectedCompression;
            MagickColor color;
            if (_fillBackGround)
            {
                color = new MagickColor(0x20, 0x20, 0x20, 0xff);
            }
            else
            {
                color = new MagickColor(0x00, 0x00, 0x00, 0x00);
            }

            int height = _squareTexture ? _selectedResolition : _selectedResolutionHeight;
            processedImage.Extent(_selectedResolition, height, Gravity.Center, color);

            return processedImage;
        }

        private MagickImage NormalOptimization(MagickImage img, string name)
        {
            MagickGeometry res;
            int height = _squareTexture ? _selectedResolition : _selectedResolutionHeight;
            if (img.Height > img.Width)
            {
                res = new MagickGeometry()
                {
                    Height = height,
                };
            }
            else
            {
                res = new MagickGeometry
                {
                    Width = _selectedResolition
                };
            }
            
            if (_textureBorder == 0)
            {
                img.Trim();
                img.Scale(res);
            }
            else
            {
                // Trim the image to it's extents
                img.Trim();
                // Add a little border around the image of 16 px
                img.BorderColor = _backgroundColor;
                img.Border(_textureBorder, _textureBorder);
                // Scale the image down
                img.Scale(res);
            }
            
            if (img.Height > _selectedResolutionHeight)
            {
                img.Scale(new MagickGeometry()
                {
                    Height = _selectedResolutionHeight,
                });
            }
            if (img.Width > _selectedResolition)
            {
                img.Scale(new MagickGeometry()
                {
                    Width = _selectedResolition,
                });
            }

            if (_fillBackGround)
            {
                img.BackgroundColor = _backgroundColor;
            }
            return img;
        }
 
        private MagickImage CutImageIntoPieces(MagickImage img, string imageName)
        {
            // Last resort
            using var clone = img.Clone();

            var pixel = clone.GetPixels().GetPixel(1,1);
            var col = pixel.ToColor();
            if (col != null && col.A > 0)
            {
                clone.ColorFuzz = new Percentage(5);
                clone.Transparent(col);
            }
            clone.Alpha(AlphaOption.Extract);
            clone.Threshold(new Percentage(10));
            clone.Negate();
            
            var opts = new ConnectedComponentsSettings
            {
                Connectivity = 4,
                // AreaThreshold = new Threshold(2048)
            };
     
            using var masked = new MagickImageCollection();
            
            // ONLY run this once
            lock (_geometryLock)
            {
                if (!_geometryInitialized)
                {
                    SetStatus("Calculating Texture Geometry...");
                    _connectedComponents = clone.ConnectedComponents(opts).OrderBy(a => a.Area).ToList();
                    int count = _connectedComponents.Count;
                    _geometry = new MagickGeometry[count];
                    _masks = new IMagickImage[count];
                    for (int index = 0; index < count; index++)
                    {
                        var component = _connectedComponents[index];
                        _geometry[index] = new MagickGeometry(component.X, component.Y, component.Width, component.Height);
                        var mask = clone.Clone();
                        mask.FloodFill(MagickColors.White, (int)component.Centroid.X, (int)component.Centroid.Y);
                        mask.Threshold(new Percentage(50));
                        mask.Negate();
                        mask.Crop(_geometry[index]);
                        mask.RePage();
                        _masks[index] = mask;
                    }

                    _geometryInitialized = true;
                }
            }

            for (int index = 0; index < _geometry.Length; index++)
            {
                var component = _connectedComponents[index];
                if (component.Id == 0) continue; // Do not grab the whole image
            
                var geometry = _geometry[index];
            
                using var copy = img.Clone();
                copy.Crop(geometry);
                copy.RePage();
            
                var item = new MagickImage(MagickColors.Transparent, img.Width, img.Height);
                item.Format = MagickFormat.Dds;
                item.Crop(geometry);
                item.RePage();
                item.SetWriteMask(_masks[index]);
                item.Composite(copy, CompositeOperator.Over);
            
                masked.Add(item);
            }

            using var newTexture = new MagickImage(new MagickColor(0x02, 0x02, 0x02, 0xff), img.Width, img.Height);

            var montageSettings = new MontageSettings
            {
                Geometry = new MagickGeometry(5, 5, 0, 0),
                BackgroundColor = _fillBackGround ? new MagickColor(0x20, 0x20, 0x20, 0xff) : MagickColors.Transparent
            };
            
            using var montage = masked.Montage(montageSettings);
            
            montage.RePage();

            if (_textureBorder == 0)
            {
                montage.BorderColor = montage.BackgroundColor;
                montage.Border((int)(montage.Width * 0.01), (int)(montage.Height * 0.01));
            }
            
            var res = new MagickGeometry
            {
                Width = _selectedResolition
            };
            
            montage.Scale(res);
            
            // montage.Extent(_selectedResolition, _selectedResolition, Gravity.Center);

            return new MagickImage(montage);
        }

        public void onProgress(object sender, EventArgs e)
        {
            if (e is ProgressChangedEventArgs args)
            {
                this.Progress.Value = args.ProgressPercentage;
            }
        }


        private void ResolutionPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (ComboBoxItem)this.ResolutionPicker.SelectedValue;
            _selectedResolition = Convert.ToInt32(selected.Content);

            if (_squareTexture && this.ResolutionPickerHeight != null)
            {
                ResolutionPickerHeight.SelectedIndex = ResolutionPicker.SelectedIndex;
            }
        }

        private void CompressionPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (ComboBoxItem)CompressionPicker.SelectedValue;

            string name = selected.Name;
            switch (name)
            {
                case "DXT1":
                    _selectedCompression = CompressionMethod.DXT1;
                    break;
                case "DXT3":
                    _selectedCompression = CompressionMethod.DXT3;
                    break;
                case "DXT5":
                    _selectedCompression = CompressionMethod.DXT5;
                    break;
            }
        }

        private void RearrangeCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            _rearrangeTexture = false;
        }

        private void RearrangeCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            _rearrangeTexture = true;
        }

        private void SquareTextureCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _squareTexture = true;
            if (ResolutionPickerHeight != null)
            {
                ResolutionPickerHeight.IsEnabled = false;
            }
        }

        private void SquareTextureCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _squareTexture = false;
            if (ResolutionPickerHeight != null)
            {
                ResolutionPickerHeight.IsEnabled = true;
            }
        }

        private void ResolutionPickerHeight_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (ComboBoxItem)this.ResolutionPickerHeight.SelectedValue;
            _selectedResolutionHeight = Convert.ToInt32(selected.Content);
        }

        private void TextureBorderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (ComboBoxItem)this.TextureBorderComboBox.SelectedValue;
            switch (selected.Name)
            {
                case "x64":
                    _textureBorder = 64;
                    break;
                case "x32":
                    _textureBorder = 32;
                    break;
                case "x8":
                    _textureBorder = 8;
                    break;
                default:
                    _textureBorder = 0;
                    break;
            }
        }
        
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}