using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Android;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using System.Linq;
using System.Threading.Tasks;

namespace HomeBackup
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        private PowerManager.WakeLock wl = null;
        private ProgressBar progressBar = null;
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            Button btn = FindViewById<Button>(Resource.Id.startButton);
            btn.Click += Btn_Click;

            RequestPermissions(new string[] { Manifest.Permission.WriteExternalStorage,
                Manifest.Permission.ReadExternalStorage, Manifest.Permission.Internet, Manifest.Permission.WakeLock }, 0);
            PowerManager pm = (PowerManager)this.GetSystemService(Android.Content.Context.PowerService);
            wl = pm.NewWakeLock(WakeLockFlags.Partial, "homebackup");

            progressBar = FindViewById<ProgressBar>(Resource.Id.progressBar);
            ServicePointManager.DefaultConnectionLimit = 10;
            

        }

        protected override void OnDestroy()
        {
            if (wl.IsHeld)
            {
                wl.Release();
            }
            base.OnDestroy();
        }

        private ProgressManager progressManager = null;

        private void LoadSettingCache()
        {
            string programPath = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, "HomeBackup");
            if (!Directory.Exists(programPath) || !File.Exists(Path.Combine(programPath, "data")))
            {
                return;
            }
            else
            {
                EditText url = FindViewById<EditText>(Resource.Id.ftpUrl);
                EditText user = FindViewById<EditText>(Resource.Id.ftpUser);
                string[] lines = File.ReadAllLines(Path.Combine(programPath, "data"));
                if (lines.Length == 2)
                {
                    url.Text = lines[0];
                    user.Text = lines[1];
                }
            }
        }
        private void CreateSettingCache(string remoteFolder, string user)
        {
            string programPath = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, "HomeBackup");
            if (!Directory.Exists(programPath))
            {
                Directory.CreateDirectory(programPath);
            }
            File.WriteAllLines(Path.Combine(programPath, "data"), new string[] { remoteFolder, user });
        }
        private void Btn_Click(object sender, EventArgs e)
        {
            wl.Acquire();
            EditText pwd = FindViewById<EditText>(Resource.Id.password);
            EditText url = FindViewById<EditText>(Resource.Id.ftpUrl);
            EditText user = FindViewById<EditText>(Resource.Id.ftpUser);

            string pwdVal = pwd.Text;
            CreateSettingCache(url.Text, user.Text);
            CheckBox deleteByBackup = FindViewById<CheckBox>(Resource.Id.deleteByBackup);
            string path = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDcim).AbsolutePath;
            string wechatPath = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath,"tencent/MicroMsg") ;
            CheckBox defaultDCIM = FindViewById<CheckBox>(Resource.Id.defaultDCIM);
            CheckBox defaultWechat = FindViewById<CheckBox>(Resource.Id.defaultWechat);
            List<BackupFileInfo> allPics = new List<BackupFileInfo>();
            Message("正在扫描文件……\n");

            url.Enabled = false;
            user.Enabled = false;
            pwd.Enabled = false;
            pwd.Text = "";
            (sender as Button).Enabled = false;
            Task searchFileTask = new Task(() =>
            {
                if (defaultDCIM.Checked)
                {
                    allPics.AddRange(GetPictureFilePaths(path));
                    Message(string.Format("于 {0} 找到 {1} 个文件，共计 {2} MB. \n", "默认相册", allPics.Count, allPics.Sum(a => a.File.Length) / (1024 * 1024)));
                }
            });
            Task searchFileTask2 = new Task(() =>
            {

                if (defaultWechat.Checked)
                {
                    allPics.AddRange(this.GetWechatPics(wechatPath));
                    allPics.AddRange(this.GetWechatRemotePics(wechatPath));
                }

            });

            searchFileTask
                .Start();
            searchFileTask2
                .Start();

            Task.Run(()=> 
            {
                Task.WaitAll(searchFileTask, searchFileTask2);
                if (allPics.Count == 0)
                {
                    Message("没有找到需要备份的文件。\n");
                    new Handler(MainLooper).Post(() =>
                    {
                        (sender as Button).Enabled = true;
                        progressBar.Visibility = ViewStates.Invisible;
                        pwd.Enabled = true;
                        url.Enabled = true;
                        user.Enabled = true;
                        wl.Release();
                    });
                    return;
                }
                progressManager = new ProgressManager();
                new Handler(MainLooper).Post(()=> 
                {
                    progressManager.Size = allPics.Sum((a) => { return a.File.Length; });
                    progressBar.Visibility = ViewStates.Visible;
                    progressBar.Max = 1000;
                });
                
                DateTime startTime = DateTime.Now;
                List<Task> tasks = new List<Task>();
                int threadCount = 5;
                FtpWorker.FileQueue = new Queue<BackupFileInfo>(allPics);
                messageLines.Clear();
                WorkingList.Clear();
                for (int i = 0; i < threadCount; i++)
                {
                    Task t = new Task(new Action(() =>
                    {
                        
                        FtpWorker worker = new FtpWorker(path, url.Text,user.Text, pwdVal, deleteByBackup.Checked);
                        worker.Uploaded += Worker_Uploaded;
                        worker.UploadFailed += Worker_UploadFailed;
                        worker.LocalFileDeleted += Worker_LocalFileDeleted;
                        worker.ProgressUpdate += Worker_ProgressUpdate;
                        worker.StartUpload += Worker_StartUpload;
                        worker.SingleProgressUpdate += Worker_SingleProgressUpdate;
                        worker.Start();
                    
                    }));
                    tasks.Add(t);
                    t.Start();
                }

                Task.Run(() =>
                {
                    Task.WaitAll(tasks.ToArray());
                    DateTime endTime = DateTime.Now;
                    Message(string.Format("完毕，耗时{0}s，平均速度{1}KB/s.", (endTime - startTime).Ticks / 10000000, (progressManager.Size / 1024) / ((endTime - startTime).Ticks / 10000000)));
                    new Handler(MainLooper).Post(() =>
                    {
                        (sender as Button).Enabled = true;
                        progressBar.Visibility = ViewStates.Invisible;
                        pwd.Enabled = true;
                        url.Enabled = true;
                        user.Enabled = true;
                        if (wl.IsHeld)
                        {
                            wl.Release();
                        }
                        
                    });
                });

            });
            

        }


        private void Worker_SingleProgressUpdate(string id, int processKB)
        {
            WorkingList[id].CurrentSize = processKB;
            List<string> holdings = new List<string>();

            foreach (string key in WorkingList.Keys)
            {
                string message = string.Format("{0}: {1} / {2} (KB)", key, WorkingList[key].CurrentSize, WorkingList[key].Size);
                holdings.Add(message);
            }
            holdings.Add(string.Format("剩余文件 {0}", FtpWorker.FileQueue.Count));
            HoldingMessages(holdings);
        }

        public Dictionary<string, ProgressManager> WorkingList = new Dictionary<string, ProgressManager>();
        private void Worker_StartUpload(string id, int sizeKB)
        {
            if (!WorkingList.ContainsKey(id))
            {
                WorkingList.Add(id, new ProgressManager() { Size = sizeKB, CurrentSize = 0 });
            }
            else
            {
                WorkingList[id].CurrentSize = 0;
                WorkingList[id].Size = sizeKB;

            }
        }

        private void Worker_ProgressUpdate(long fileSize)
        {
            progressManager.CurrentSize += fileSize;
            new Handler(MainLooper).Post(()=> 
            {
                progressBar.Progress = (int)((progressManager.CurrentSize * 1000) / progressManager.Size);
            });
        }

        private void Worker_LocalFileDeleted(string fileName)
        {
            Message(string.Format("{0} 已本地删除。\n", fileName));
        }

        private void Worker_UploadFailed(string fileName, string message)
        {
            Message(string.Format("{0} 备份失败 :{1}\n", fileName, message));
        }

        private void Worker_Uploaded(string fileName)
        {
            Message(string.Format("{0} 上传完毕。\n", fileName));
        }

        public Queue<string> messageLines = new Queue<string>();

        private void HoldingMessages(List<string> messageLines)
        {
            TextView messageText = FindViewById<TextView>(Resource.Id.holdingMessageText);
            new Handler(MainLooper).Post(() =>
            {
                StringBuilder builder = new StringBuilder();
                foreach (string msg in messageLines)
                {
                    builder.AppendLine(msg);
                }

                messageText.Text = builder.ToString();
            });

        }
        private void Message(string message)
        {

            TextView messageText = FindViewById<TextView>(Resource.Id.messageText);
            ScrollView textScroller = FindViewById<ScrollView>(Resource.Id.textScroller);
            new Handler(MainLooper).Post(() =>
            {
                messageLines.Enqueue(message);
                if (messageLines.Count > 100)
                {
                    messageLines.Dequeue();
                }
                StringBuilder builder = new StringBuilder();
                foreach (string msg in messageLines)
                {
                    builder.Append(msg);
                }

                messageText.Text = builder.ToString();
                textScroller.SmoothScrollTo(0, messageText.Height);
            });
        }

        private List<BackupFileInfo> GetWechatPics(string wechatPath)
        {
            List<BackupFileInfo> results = new List<BackupFileInfo>();
            if (Directory.Exists(Path.Combine(wechatPath, "WeiXin")))
            {
                DirectoryInfo di = new DirectoryInfo(Path.Combine(wechatPath, "WeiXin"));
                results = GetPictureFilePaths(di.FullName);
                
                
            }
            Message(string.Format("于 {0} 找到 {1} 个文件，共计 {2} MB. \n", "微信发送目录", results.Count, results.Sum(a => a.File.Length) / (1024 * 1024)));
            return results;

        }

        private List<BackupFileInfo> GetWechatRemotePics(string wechatPath)
        {
            List<BackupFileInfo> results = new List<BackupFileInfo>();
            if (Directory.Exists(wechatPath))
            {
                DirectoryInfo di = new DirectoryInfo(wechatPath);
                
                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    if (dir.Name.Length >= 20)
                    {
                        results.AddRange(GetPictureFilePaths(Path.Combine(dir.FullName, "image")));
                        results.AddRange(GetPictureFilePaths(Path.Combine(dir.FullName, "image2")));
                        results.AddRange(GetPictureFilePaths(Path.Combine(dir.FullName, "video")));

                    }
                }
                
                
            }
            Message(string.Format("于 {0} 找到 {1} 个文件，共计 {2} MB. \n", "微信他人发送目录", results.Count, results.Sum(a => a.File.Length) / (1024 * 1024)));
            return results;

        }
        private List<BackupFileInfo> GetPictureFilePaths(string parentPath)
        {
            
            DirectoryInfo di = new DirectoryInfo(parentPath);
            FileInfo[] files = di.GetFiles();
            DirectoryInfo[] dirs = di.GetDirectories();
            List<BackupFileInfo> allPics = new List<BackupFileInfo>();
            if (files.Length > 0)
            {
                foreach (FileInfo fi in files)
                {
                    allPics.Add(new BackupFileInfo(fi));
                }
            }

            if (dirs.Length > 0)
            {
                foreach (DirectoryInfo dir in dirs)
                {
                    allPics.AddRange(GetPictureFilePaths(dir.FullName));
                }
            }
            
            return allPics;
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            LoadSettingCache();
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
	}

    public class FtpWorker
    {

        public static Queue<BackupFileInfo> FileQueue = new Queue<BackupFileInfo>();
        public string RemoteFolder { get; set; }
        public string Pwd { get; set; }
        private FtpWebRequest Request { get; set; }
        public string StoragePath { get; set; }

        public delegate void OnProgressUpdate(long fileSize);
        public event OnProgressUpdate ProgressUpdate;
        public delegate void OnSingleProgressUpdate(string id, int processKB);
        public event OnSingleProgressUpdate SingleProgressUpdate;
        public delegate void OnUploaded(string fileName);
        public event OnUploaded Uploaded;

        public delegate void OnUploadFailed(string fileName, string message);
        public event OnUploadFailed UploadFailed;

        public delegate void OnLocalFileDeleted(string fileName);
        public event OnLocalFileDeleted LocalFileDeleted;

        public delegate void OnStartUpload(string id, int sizeKB);
        public event OnStartUpload StartUpload;
        public bool DeleteByBackup { get; set; }
        public string User { get; set; }
        public string Id { get; set; }

        public int CurrentFileWorkingSize { get; set; }
        public FtpWorker(string storagePath, string remoteFolder,string user, string pwd, bool deleteByBackup)
        {
            this.RemoteFolder = remoteFolder;
            this.User = user;
            this.Pwd = pwd;
            this.DeleteByBackup = deleteByBackup;
            this.Id = Guid.NewGuid().ToString();
        }

        public void Start()
        {
            string folderName = "";
            
            lock (FtpWorker.FileQueue)
            {
                folderName = MakeDir();
            }
            

            BackupFileInfo fi = null;
            while (true)
            {
                fi = FtpWorker.FileQueue.Dequeue();
                if (StartUpload != null)
                {
                    StartUpload(Id, (int)(fi.File.Length / (1024)));
                }
                CurrentFileWorkingSize = 0;
                UploadFile(fi, this.StoragePath, folderName);
                // lock (FtpWorker.FileQueue)
                // {
                if (FtpWorker.FileQueue.Count == 0)
                {
                    break;
                }
                //  }

            }
        }

        private string MakeDir()
        {


            string folderName = DateTime.Now.ToString("yyyyMMdd");
            string uri = string.Format("{1}/{0}",
                    folderName, this.RemoteFolder);
            try
            {
                Request = (FtpWebRequest)FtpWebRequest.Create(uri);
                if(User!=null && Pwd != null)
                {
                    Request.Credentials = new NetworkCredential(User, Pwd);
                }
                
                Request.Method = WebRequestMethods.Ftp.MakeDirectory;
                Request.UseBinary = true;
                Request.Timeout = 5000;
                Request.ReadWriteTimeout = 5000;
                using (FtpWebResponse resp = (FtpWebResponse)Request.GetResponse())
                {
                    return folderName;
                }
            }
            catch (Exception e)
            {
                return folderName;
            }

        }
        private bool UploadFile(BackupFileInfo file, string rootPath, string folderName)
        {
            FileStream fileStream = File.OpenRead(file.File.FullName);
            string uri = string.Format("{3}/{2}/{0}-{1}",
                file.File.CreationTime.ToString("yyyyMMddhhmmssfff"), BitConverter.ToString(Encoding.UTF8.GetBytes(file.File.Name),0).Replace("-","")
                +file.File.Extension, folderName, this.RemoteFolder);
            Stream s = null;
            try
            {
                Request = (FtpWebRequest)FtpWebRequest.Create(uri);
                if (User != null && Pwd != null)
                {
                    Request.Credentials = new NetworkCredential(User, Pwd);
                }

                Request.Method = WebRequestMethods.Ftp.UploadFile;
                Request.UseBinary = true;
                Request.Timeout = 5000;
                Request.ReadWriteTimeout = 5000;
                Request.ContentLength = fileStream.Length;
                

                s = Request.GetRequestStream();
                byte[] buffer = new byte[1024 * 1024 * 1];
                int realRead = 0;
                while ((realRead = fileStream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    s.Write(buffer, 0, realRead);
                    CurrentFileWorkingSize += realRead;
                    if (SingleProgressUpdate != null)
                    {
                        SingleProgressUpdate(Id, CurrentFileWorkingSize / 1024);
                    }
                    if (ProgressUpdate != null)
                    {
                        ProgressUpdate(realRead);
                    }
                }
                s.Close();
                Request.GetResponse().Dispose();
                if (this.Uploaded != null)
                {
                    this.Uploaded(file.File.Name);
                }
                if (this.DeleteByBackup)
                {
                    file.File.Delete();
                    if (this.LocalFileDeleted != null)
                    {
                        this.LocalFileDeleted(file.File.Name);
                    }
                }
                return true;


            }
            catch (Exception e)
            {
                if (file.RetryCount == 5)
                {
                    if (this.UploadFailed != null)
                    {
                        this.UploadFailed(file.File.Name, e.Message);
                    }
                }
                else
                {
                    FtpWorker.FileQueue.Enqueue(file);
                }
                return false;
            }
            finally
            {
                fileStream.Dispose();
                
            }


        }


    }

    public class BackupFileInfo
    {
        public FileInfo File{get;set;}
        public int RetryCount { get; set; }

        public BackupFileInfo(FileInfo file)
        {
            this.File = file;
            this.RetryCount = 0;
        }
    }

    public class ProgressManager
    {
        public long Size { get; set; }
        public long CurrentSize { get; set; }

    }
}

