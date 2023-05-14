using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace AP_Project_RonnieConlon
{
    class ImageViewModel : INotifyPropertyChanged
    {

        const string IS_FILE_PATH = "ap-project-image.png";
        const string IS_AUTO_FILE_PATH = "ap-project-image-auto.png";
        volatile BitmapImage _img;
        string _urlImage;
        string filepath;

        Object _lock = new Object();
        Object _ISlock = new Object();

        private ICommand _loadImageCommand;
        private ICommand _loadImageISCommand;
        private ICommand _blackWhiteFilterCommand;
        private ICommand _flipFilterCommand;
        private ICommand _getOnlineImageCommmand;
        private ICommand _saveImageCommand;
        private ICommand _saveImageISCommand;

        private ObservableCollection<KeyValuePair<string, string>> _currentThreads;

        public ObservableCollection<KeyValuePair<string, string>> CurrentThreads
        {
            get { return _currentThreads; }
            set
            {
                if (_currentThreads != value)
                {
                    _currentThreads = value;
                    OnPropertyChanged(nameof(CurrentThreads));
                }
            }
        }

        public BitmapImage Img
        {
            get { return _img; }
            set
            {
                _img = value;
                OnPropertyChanged(nameof(Img));
            }
        }

        public string URLImage
        {
            get { return _urlImage; }
            set
            {
                _urlImage = value;
                OnPropertyChanged(nameof(URLImage));
            }
        }
        public ImageViewModel()
        {
            TimerAutoSaveImageIS(IS_AUTO_FILE_PATH);
            CurrentThreads = new ObservableCollection<KeyValuePair<string, string>>();
        }

        private Boolean CanLoadImage()
        {
            return true;
        }

        public ICommand LoadImageCommand
        {
            get
            {
                return _loadImageCommand ??= new RelayCommand(param => LoadImage(), param => CanLoadImage());
            }
        }

        private void LoadImage()
        {
            const string THREAD_NAME = "Load Image";
            ThreadPool.QueueUserWorkItem(state =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                Monitor.Enter(_lock);

                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Image files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp|All files (*.*)|*.*";
                if (openFileDialog.ShowDialog() == true)
                {
                    string filePath = openFileDialog.FileName;
                    this.filepath = filePath;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Img = new BitmapImage(new Uri(filePath));
                    });
                }

                Monitor.Exit(_lock);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                    CurrentThreads.Remove(threadToRemove);
                });
            });
        }

        public ICommand LoadImageISCommand
        {
            get
            {
                return _loadImageISCommand ??= new RelayCommand(param => LoadImageIS(IS_FILE_PATH), param => CanLoadImage());
            }
        }


        private void LoadImageIS(String file)
        {
            const string THREAD_NAME = "Load Image IS";
            ThreadPool.QueueUserWorkItem(state =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                Monitor.Enter(_lock);
                Monitor.Enter(_ISlock);

                using (IsolatedStorageFile isolatedStorage = IsolatedStorageFile.GetUserStoreForDomain())
                {
                    if (isolatedStorage.FileExists(file))
                    {
                        using (IsolatedStorageFileStream fileStream = isolatedStorage.OpenFile(file, FileMode.Open, FileAccess.Read))
                        {
                            using (MemoryStream memStream = new MemoryStream())
                            {

                                fileStream.CopyTo(memStream);
                                memStream.Position = 0;

                                Bitmap bmp;
                                using (var bmpTemp = new Bitmap(memStream))
                                {
                                    bmp = new Bitmap(bmpTemp);
                                }

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    this.Img = ConvertBitmapToBitmapImage(bmp);
                                });
                            }
                        }
                    }
                }

                Monitor.Exit(_ISlock);
                Monitor.Exit(_lock);


                Application.Current.Dispatcher.Invoke(() =>
                {
                    var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                    CurrentThreads.Remove(threadToRemove);
                });
            });
        }

        private void TimerAutoSaveImageIS(string file)
        {
            const string THREAD_NAME = "Auto save";

            Thread timersi = new Thread(() => {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                while (true)
                {
                    if (this.Img != null)
                    {
                        Monitor.Enter(_lock);
                        Monitor.Enter(_ISlock);

                        using (IsolatedStorageFile isolatedStorage = IsolatedStorageFile.GetUserStoreForDomain())
                        {
                            if (isolatedStorage.FileExists(file))
                            {
                                isolatedStorage.DeleteFile(file);
                            }

                            using (IsolatedStorageFileStream fileStream = isolatedStorage.CreateFile(file))
                            {
                                BitmapEncoder encoder = new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(this.Img));
                                encoder.Save(fileStream);
                                fileStream.Close();
                            }
                        }

                        Monitor.Exit(_ISlock);
                        Monitor.Exit(_lock);
                    }

                    // Sleep 20 seconds
                    Thread.Sleep(5000);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                    CurrentThreads.Remove(threadToRemove);
                });

            });
            timersi.Start();
        }

        public ICommand SaveImageCommand
        {
            get
            {
                return _saveImageCommand ??= new RelayCommand(param => SaveImage(), param => CanApplyFilter());
            }
        }

        private void SaveImage()
        {
            const string THREAD_NAME = "Same Image";

            if (Img != null)
            {
                ThreadPool.QueueUserWorkItem(state =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                    });

                    Monitor.Enter(_lock);

                    Bitmap bm = ConvertBitmapImageToBitmap(Img);

                    SaveFileDialog svg = new SaveFileDialog();
                    svg.Filter = "PNG Image|*.png|JPEG Image|*.jpg|BMP Image|*.bmp";
                    svg.Title = "Save Image";
                    svg.ShowDialog();

                    if (svg.FileName != "")
                    {
                        // Saves the Image via a BitmapImage in the Stream
                        var encoder = new PngBitmapEncoder(); // or use JpegBitmapEncoder, etc.
                        encoder.Frames.Add(BitmapFrame.Create(Img));

                        using (var fileStream = new FileStream(svg.FileName, FileMode.Create))
                        {
                            encoder.Save(fileStream);
                        }
                    }

                    Monitor.Exit(_lock);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                        CurrentThreads.Remove(threadToRemove);
                    });
                });
            }
        }
        public ICommand SaveImageISCommand
        {
            get
            {
                return _saveImageISCommand ??= new RelayCommand(param => SaveImageIS(IS_FILE_PATH), param => CanApplyFilter());
            }
        }

        private void SaveImageIS(String file)
        {
            const string THREAD_NAME = "Save Image IS";

            ThreadPool.QueueUserWorkItem(state =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                Monitor.Enter(_lock);
                Monitor.Enter(_ISlock);

                using (IsolatedStorageFile isolatedStorage = IsolatedStorageFile.GetUserStoreForDomain())
                {
                    if (isolatedStorage.FileExists(file))
                    {
                        isolatedStorage.DeleteFile(file);
                    }

                    IsolatedStorageFileStream fileStream = isolatedStorage.CreateFile(file);
                    BitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(Img));
                    encoder.Save(fileStream);
                    fileStream.Close();
                }

                Monitor.Exit(_ISlock);
                Monitor.Exit(_lock);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                    CurrentThreads.Remove(threadToRemove);
                });
            });
        }


        public ICommand BlackWhiteFilterCommand
        {
            get
            {
                return _blackWhiteFilterCommand ??= new RelayCommand(param => ApplyBlackWhiteFilter(), param => CanApplyFilter());
            }
        }
        private void ApplyBlackWhiteFilter()
        {
            const string THREAD_NAME = "Black White Filter";

            ThreadPool.QueueUserWorkItem(state =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                Monitor.Enter(_lock);

                Bitmap original = ConvertBitmapImageToBitmap(Img);
                Bitmap grayscale = new Bitmap(original.Width, original.Height);

                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        System.Drawing.Color originalColor = original.GetPixel(x, y);
                        int grayScale = (int)((originalColor.R * 0.3) + (originalColor.G * 0.59) + (originalColor.B * 0.11));
                        System.Drawing.Color grayColor = System.Drawing.Color.FromArgb(originalColor.A, grayScale, grayScale, grayScale);
                        grayscale.SetPixel(x, y, grayColor);
                    }
                }

                original.Dispose();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    this.Img = ConvertBitmapToBitmapImage(grayscale);
                });

                grayscale.Dispose();

                Monitor.Exit(_lock);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                    CurrentThreads.Remove(threadToRemove);
                });
            });
        }



        public ICommand FlipFilterCommand
        {
            get
            {
                return _flipFilterCommand ??= new RelayCommand(param => ApplyFlipFilter(), param => CanApplyFilter());
            }
        }

        private void ApplyFlipFilter()
        {
            const string THREAD_NAME = "Flip filter";

            ThreadPool.QueueUserWorkItem(state =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                Monitor.Enter(_lock);

                Bitmap original = ConvertBitmapImageToBitmap(Img);
                original.RotateFlip(RotateFlipType.RotateNoneFlipX);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    this.Img = ConvertBitmapToBitmapImage(original);
                });

                original.Dispose();

                Monitor.Exit(_lock);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                    CurrentThreads.Remove(threadToRemove);
                });
            });
        }

        private bool CanApplyFilter()
        {
            // Check if the image exists
            return Img != null;
        }

        public ICommand GetOnlineImageCommand
        {
            get
            {
                return _getOnlineImageCommmand ??= new RelayCommand(param => GetOnlineImage(), param => ValidUrl());
            }
        }

        async private void GetOnlineImage()
        {
            const string THREAD_NAME = "Get Online Img";

            ThreadPool.QueueUserWorkItem(async state =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                Monitor.Enter(_lock);

                using (HttpClient client = new HttpClient())
                {
                    byte[] imageData = await client.GetByteArrayAsync(URLImage);

                    using (MemoryStream memoryStream = new MemoryStream(imageData))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Img = ConvertBitmapToBitmapImage(new Bitmap(memoryStream));
                        });
                    }
                }

                Monitor.Exit(_lock);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                    CurrentThreads.Remove(threadToRemove);
                });
            });
        }

        public Boolean ValidUrl()
        {
            if (URLImage != "")
            {
                return true;
            }
            return false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;


        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public Bitmap ConvertBitmapImageToBitmap(BitmapImage bitmapImage)
        {
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                enc.Save(outStream);
                Bitmap bitmap = new System.Drawing.Bitmap(outStream);

                return new Bitmap(bitmap);
            }
        }
        public BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;

                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();

                return bitmapImage;
            }
        }
    }

 

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
}
