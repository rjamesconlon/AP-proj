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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace AP_Project_RonnieConlon
{
    class ImageViewModel : INotifyPropertyChanged
    {
        // View Model for the image handling
        // There is no traditional data model, as the only data being handled is the image (BitmapImage),

        // File paths for the Isolated Storage saving
        // The second file path is for auth storage, saving every 20 seconds
        const string IS_FILE_PATH = "ap-project-image.png";
        const string IS_AUTO_FILE_PATH = "ap-project-image-auto.png";

        // private vars
        volatile BitmapImage _img;
        string _urlImage;
        private ObservableCollection<KeyValuePair<string, string>> _currentThreads;
        private int _filterprogressbar;

        // filepath of the image
        string filepath;

        // _lock is for locking of the image
        // _ISLock is for the Isolated Storage lock
        Object _lock = new Object();
        Object _ISlock = new Object();

        // Commands which are implemented below
        // These are called by the view
        private ICommand _loadImageCommand;
        private ICommand _loadImageISCommand;
        private ICommand _blackWhiteFilterCommand;
        private ICommand _flipFilterCommand;
        private ICommand _getOnlineImageCommmand;
        private ICommand _saveImageCommand;
        private ICommand _saveImageISCommand;
        private ICommand _applyNegativeFilterCommand;
        private ICommand _cancelNegativeFilterCommand;
        private ICommand _loadBackupImageISCommand;

        // Background worker which is used for the Negative filter
        BackgroundWorker bg = new BackgroundWorker();

        // Used for the listbox in the view, which shows the current active threads (aside from UI)
        // When one is added or removed, the OnPropertyChanged notifies this, so the binding updates
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

        // Similar to above on notifying
        // Img is the image displayed
        public BitmapImage Img
        {
            get { return _img; }
            set
            {
                _img = value;
                OnPropertyChanged(nameof(Img));
            }
        }

        // URLImage is the URL entered by the user
        public string URLImage
        {
            get { return _urlImage; }
            set
            {
                _urlImage = value;
                OnPropertyChanged(nameof(URLImage));
            }
        }

        // The progress bar for the filters
        public int FilterProgressBar
        {
            get { return _filterprogressbar; }
            set
            {
                if (_filterprogressbar != value)
                {
                    _filterprogressbar = value;
                    OnPropertyChanged("FilterProgressBar");
                }
            }
        }

        public ImageViewModel()
        {
            // Begin the auto save thread
            TimerAutoSaveImageIS(IS_AUTO_FILE_PATH);

            // collection for thread displaying
            CurrentThreads = new ObservableCollection<KeyValuePair<string, string>>();

            // Set the methods for the background worker
            bg.WorkerSupportsCancellation = true;
            bg.WorkerReportsProgress = true;

            bg.DoWork += bg_DoWork;
            bg.ProgressChanged += bg_ReportProgress;
            bg.RunWorkerCompleted += bg_WorkComplete;
        }

        private Boolean CanLoadImage()
        {
            return true;
        }

        // Check if the image can be loaded, then call the Load Image func
        public ICommand LoadImageCommand
        {
            get
            {
                return _loadImageCommand ??= new RelayCommand(param => LoadImage(), param => CanLoadImage());
            }
        }

        // Thread Pool
        private void LoadImage()
        {
            // The thread name to be displayed in the listbox
            const string THREAD_NAME = "Load Image";

            // This uses the Mirocost .NET thread pool, which is made to be efficient
            // Better than initializing a single thread using new Thread()
            // as it calls from a pool of optimized threads by queueing
            ThreadPool.QueueUserWorkItem(state =>
            {
                // Add the thread to the listbox
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                // Try to get a lock on the image
                Monitor.Enter(_lock);

                // This opens the file explorer, filtering for images
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

                // Remove lock
                Monitor.Exit(_lock);

                // Remove the thread from the listbox
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
        public ICommand LoadBackupImageISCommand
        {
            get
            {
                return _loadBackupImageISCommand ??= new RelayCommand(param => LoadImageIS(IS_AUTO_FILE_PATH), param => CanLoadImage());
            }
        }


        private void LoadImageIS(String file)
        {
            // Loads the image from isolated storage if it exists

            const string THREAD_NAME = "Load Image IS";
            ThreadPool.QueueUserWorkItem(state =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                // Enter the image and then isolated storage lock in this order
                Monitor.Enter(_lock);
                Monitor.Enter(_ISlock);

                try
                {
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
                } catch (Exception ex)
                {
                    MessageBox.Show("Issue with loading image from IS");
                }

                // Exit the isolated storage lock first, then the image lock. This prevents deadlock
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
            // Begins a timer for the auto save on isolated storage

            const string THREAD_NAME = "Auto save";

            Thread timersi = new Thread(() => {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                // this loop runs indefinitely, every 15 seconds
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

                            // create a file stream with the image, encode to a png
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

                    // Sleep 15 seconds
                    Thread.Sleep(15000);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                    CurrentThreads.Remove(threadToRemove);
                });

            });

            timersi.IsBackground = true;
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
            // save the image locally

            const string THREAD_NAME = "Same Image";

            if (Img != null)
            {
                ThreadPool.QueueUserWorkItem(state =>
                {
                    // this is for the message box
                    bool img_saved = false;

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

                            img_saved = true;
                        }
                    }

                    Monitor.Exit(_lock);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                        CurrentThreads.Remove(threadToRemove);

                        if (img_saved) MessageBox.Show("Image Saved!");
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
            // save the image to isolated storage

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
                    // easier to delete the old file if it exits
                    // this is ok as images are generally not too large
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
                    MessageBox.Show("Image Saved to IS!");
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

        // Thread Pool
        private void ApplyBlackWhiteFilter()
        {
            // apply the black and white filter to the image

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

                // set progress to 0
                FilterProgressBar = 0;

                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        System.Drawing.Color originalColor = original.GetPixel(x, y);
                        int grayScale = (int)((originalColor.R * 0.3) + (originalColor.G * 0.59) + (originalColor.B * 0.11));
                        System.Drawing.Color grayColor = System.Drawing.Color.FromArgb(originalColor.A, grayScale, grayScale, grayScale);
                        grayscale.SetPixel(x, y, grayColor);
                    }

                    // set progress equal to the row currently on (y), out of all rows of pixels in the image
                    FilterProgressBar = (y * 100 / original.Height);
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

        // Thread Pool
        private void ApplyFlipFilter()
        {
            // Flip the image on the horizontal axis

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

        // Thread pool
        async private void GetOnlineImage()
        {
            // Gets the image from online using a URL
            // Uses an Async callback with the thread pool
            const string THREAD_NAME = "Get Online Img";

            ThreadPool.QueueUserWorkItem(async state =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
                });

                try
                {
                    // use the http client to call URL
                    using (HttpClient client = new HttpClient())
                    {
                        // Get bytes of URL with http
                        byte[] imageData = await client.GetByteArrayAsync(URLImage);


                        // Monitor is here since await can cause issues with threading
                        // and monitor is not used for async functions
                        // make bitmap from stream, convert to bitmap image for View
                        using (MemoryStream memoryStream = new MemoryStream(imageData))
                        {
                            Bitmap bm = new Bitmap(memoryStream);
                            Monitor.Enter(_lock);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Img = ConvertBitmapToBitmapImage(bm);
                            });
                            Monitor.Exit(_lock);

                        }

                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("URL entered was not valid. Please try again!");
                }


                // Invoke UI thread, remove from running threads
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                    CurrentThreads.Remove(threadToRemove);
                });
            });
        }

        // Background worker
        public ICommand ApplyNegativeFilterCommand
        {
            get
            {
                return _applyNegativeFilterCommand ??= new RelayCommand(param => ApplyNegativeFilter(), param => CanApplyFilter());
            }
        }

        private void ApplyNegativeFilter()
        {
            bg.RunWorkerAsync();
        }

        // Background worker
        public ICommand CancelNegativeFilterCommand
        {
            get
            {
                return _cancelNegativeFilterCommand ??= new RelayCommand(param => CancelNegativeFilter(), param => CanApplyFilter());
            }
        }

        private void CancelNegativeFilter()
        {
            bg.CancelAsync();
        }

        private void bg_DoWork(object sender, DoWorkEventArgs e)
        {
            // this is for the negative filter

            const string THREAD_NAME = "Negative Filter";

            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentThreads.Add(new KeyValuePair<string, string>(THREAD_NAME, Thread.CurrentThread.ThreadState.ToString()));
            });

            Bitmap b = ConvertBitmapImageToBitmap(Img);

            for (int y = 0; y < b.Height; y++)
            {
                if (bg.CancellationPending != true)

                {
                    for (int x = 0; x < b.Width; x++)
                    {
                        System.Drawing.Color c = b.GetPixel(x, y);
                        int red = 255 - c.R;
                        int green = 255 - c.G;
                        int blue = 255 - c.B;
                        b.SetPixel(x, y, System.Drawing.Color.FromArgb(red, green, blue));
                    }
                    bg.ReportProgress(y * 100 / b.Height);
                }
            }

            if (bg.CancellationPending != true)

            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    this.Img = ConvertBitmapToBitmapImage(b);
                });
            }
        }

        private void bg_ReportProgress(object sender, ProgressChangedEventArgs e)
        {
            FilterProgressBar = e.ProgressPercentage;
        }

        private void bg_WorkComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            const string THREAD_NAME = "Negative Filter";

            Application.Current.Dispatcher.Invoke(() =>
            {
                var threadToRemove = CurrentThreads.FirstOrDefault(t => t.Key == THREAD_NAME);
                CurrentThreads.Remove(threadToRemove);
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
