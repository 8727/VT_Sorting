using Microsoft.Win32;
using System;
using System.Collections;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using System.Xml;

namespace VT_Sorting
{
    public partial class Service1 : ServiceBase
    {
        Thread GetSQL = new Thread(GetSQLViolations);
        static HttpClient httpClient = new HttpClient();
        TimeSpan localZone = TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now);
        class ReplicatorCh
        {
            public string host;
            public string LastReplicationTime;
            public Int64 LastReplicationTimeFt;
        }

        public Service1()
        {
            InitializeComponent();
        }

        Hashtable ViolationCode = new Hashtable();
        Hashtable Replicator = new Hashtable();
        void HashVuolation()
        {
            ViolationCode.Add("0", "0 - Stream");
            ViolationCode.Add("2", "2 - OverSpeed");
            ViolationCode.Add("4", "4 - WrongDirection");
            ViolationCode.Add("5", "5 - BusLane");
            ViolationCode.Add("10", "10 - RedLightCross");
            ViolationCode.Add("31", "31 - SeatBelt");
            ViolationCode.Add("81", "81 - WrongCross");
            ViolationCode.Add("83", "83 - StopLine");
            ViolationCode.Add("90", "90 - WrongTurnTwoFrontier");
            ViolationCode.Add("112", "112 - WrongLineTurn");
            ViolationCode.Add("113", "113 - NoForwardZnak");
            ViolationCode.Add("114", "114 - NoUTurnOnlyForward");
            ViolationCode.Add("127", "127 - Lights");
        }

        string sourceFolderPr = "D:\\Duplo";
        string sourceFolderSc = "D:\\Doris";
        string sortingFolderPr = "D:\\!Duplo";
        string sortingFolderSc = "D:\\!Doris";

        int storageDays = 10;
        int storageSortingIntervalMinutes = 20;
        bool storageXML = true;
        bool storageСollage = false;
        bool storageVideo = false;

        bool restartingServicesReplicator = true;
        int restartingServicesReplicatorIntervalMinutes = 60;
        bool restartingServicesExport = true;
        int restartingServicesExportIntervalHours = 6;

        static string sqlSource = "(LOCAL)";
        static string sqlUser = "sa";
        static string sqlPassword = "1";

        byte replicator = 0;
        static string lastreplicator;
        static string replicatorSec;
        int export = 0;

        int Logindex = 0;

        void Load_Config()
        {
            if (ConfigurationManager.AppSettings.Count != 0)
            {
                sourceFolderPr = ConfigurationManager.AppSettings["SourceFolderPr"];
                sortingFolderPr = ConfigurationManager.AppSettings["SortingFolderPr"];

                sourceFolderSc = ConfigurationManager.AppSettings["SourceFolderSc"];
                sortingFolderSc = ConfigurationManager.AppSettings["SortingFolderSc"];

                storageDays = Convert.ToInt32(ConfigurationManager.AppSettings["StorageDays"]);
                storageSortingIntervalMinutes = Convert.ToInt32(ConfigurationManager.AppSettings["StorageSortingIntervalMinutes"]);
                storageXML = Convert.ToBoolean(ConfigurationManager.AppSettings["StorageXML"]);
                storageСollage = Convert.ToBoolean(ConfigurationManager.AppSettings["StorageСollage"]);
                storageVideo = Convert.ToBoolean(ConfigurationManager.AppSettings["StorageVideo"]);

                restartingServicesReplicator = Convert.ToBoolean(ConfigurationManager.AppSettings["RestartingServicesReplicator"]);
                restartingServicesReplicatorIntervalMinutes = Convert.ToInt32(ConfigurationManager.AppSettings["RestartingServicesReplicatorIntervalMinutes"]);

                restartingServicesExport = Convert.ToBoolean(ConfigurationManager.AppSettings["RestartingServicesExport"]);
                restartingServicesExportIntervalHours = Convert.ToInt32(ConfigurationManager.AppSettings["RestartingServicesExportIntervalHours"]);

                sqlSource = ConfigurationManager.AppSettings["SQLDataSource"];
                sqlUser = ConfigurationManager.AppSettings["SQLUser"];
                sqlPassword = ConfigurationManager.AppSettings["SQLPassword"];
            }

            var statusReplicatorTimer = new System.Timers.Timer(2 * 60000);
            statusReplicatorTimer.Elapsed += OnStatusReplicatorTimeout;
            statusReplicatorTimer.AutoReset = true;
            statusReplicatorTimer.Enabled = true;

            var storageTimer = new System.Timers.Timer(storageSortingIntervalMinutes * 60000);
            storageTimer.Elapsed += OnStorageTimeout;
            storageTimer.AutoReset = true;
            storageTimer.Enabled = true;

            var replicatorRestartTimer = new System.Timers.Timer(restartingServicesReplicatorIntervalMinutes * 60000);
            replicatorRestartTimer.Elapsed += OnReplicatorRestartTimeout;
            replicatorRestartTimer.AutoReset = true;
            replicatorRestartTimer.Enabled = true;

            var exportRestartTimer = new System.Timers.Timer(restartingServicesExportIntervalHours * 3600000);
            exportRestartTimer.Elapsed += OnExportRestartTimeout;
            exportRestartTimer.AutoReset = true;
            exportRestartTimer.Enabled = true;

            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Vocord\VTTrafficReplicator"))
            {
                if (key != null)
                {
                    foreach (string ch in key.GetSubKeyNames())
                    {
                        using (RegistryKey key_ch = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Vocord\VTTrafficReplicator\" + ch))
                        {
                            if (key_ch != null)
                            {
                                ReplicatorCh replicatorCh = new ReplicatorCh();
                                replicatorCh.host = key_ch.GetValue("Host").ToString();
                                replicatorCh.LastReplicationTime = key_ch.GetValue("LastReplicationTime").ToString();
                                replicatorCh.LastReplicationTimeFt = Convert.ToInt64(key_ch.GetValue("LastReplicationTimeFt"));
                                Replicator.Add(ch, replicatorCh);

                                DateTime replicatorTime = DateTime.ParseExact(replicatorCh.LastReplicationTime, "dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).Add(+localZone);
                                string interval = DateTime.Now.Subtract(replicatorTime).TotalSeconds.ToString();
                                replicatorSec = interval.Remove(interval.LastIndexOf(','));
                                lastreplicator = replicatorTime.ToString();
                            }
                        }
                    }
                }
            }
        }

        void ReadIndex()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\VT_Sorting", true))
            {
                if (key.GetValue("FailureActions") == null)
                {
                    key.SetValue("FailureActions", new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x60, 0xea, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x60, 0xea, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x60, 0xea, 0x00, 0x00 });
                }
            }

            if (!(Directory.Exists(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\log")))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\log");
            }
            string[] files = Directory.GetFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\log");
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                Regex regex = new Regex(@"\d{4}-");
                if (regex.IsMatch(name))
                {
                    int number = (int.Parse(name.Remove(name.IndexOf("-"))));
                    if(number > Logindex)
                    {
                        Logindex = number;
                    }
                }
            }
        }

        void LogWriteLine(string message)
        {
            string name = Logindex.ToString("0000");
            string logDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\log";
            FileInfo fileInfo = new FileInfo(logDir + $"\\{name}-log.txt");
            using (StreamWriter sw = fileInfo.AppendText())
            {
                sw.WriteLine(String.Format("{0:yyMMdd hh:mm:ss} - {1}", DateTime.Now.ToString(), message));
                sw.Close();
                if(fileInfo.Length > 20480)
                {
                    Logindex++;
                }

                string[] delTimefiles = Directory.GetFiles(logDir, "*", SearchOption.AllDirectories);
                foreach (string delTimefile in delTimefiles)
                {
                    FileInfo fi = new FileInfo(delTimefile);
                    if (fi.CreationTime < DateTime.Now.AddDays(-storageDays)) { fi.Delete(); }
                }
            }
        }

        void OnStatusReplicatorTimeout(Object source, ElapsedEventArgs e)
        {
            ICollection keys = Replicator.Keys;
            foreach (string ch in keys)
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Vocord\VTTrafficReplicator\" + ch))
                {
                    if (key != null)
                    {
                        Int64 timeHost = Convert.ToInt64(key.GetValue("LastReplicationTimeFt"));
                        ReplicatorCh chr = (ReplicatorCh)Replicator[ch];
                        chr.LastReplicationTime = key.GetValue("LastReplicationTime").ToString();
                        DateTime replicatorTime = DateTime.ParseExact(chr.LastReplicationTime, "dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).Add(+localZone);
                        string interval = DateTime.Now.Subtract(replicatorTime).TotalSeconds.ToString();
                        replicatorSec = interval.Remove(interval.LastIndexOf(','));
                        lastreplicator = replicatorTime.ToString();
                    }
                }
            }
        }

        void OnStorageTimeout(Object source, ElapsedEventArgs e)
        {
            SortingFiles(sourceFolderPr, sortingFolderPr);
            SortingFiles(sourceFolderSc, sortingFolderSc);
        }

        void OnReplicatorRestartTimeout(Object source, ElapsedEventArgs e)
        {
            if (restartingServicesReplicator)
            {
                ICollection keys = Replicator.Keys;
                foreach (string ch in keys)
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Vocord\VTTrafficReplicator\" + ch))
                    {
                        if (key != null)
                        {
                            Int64 timeHost = Convert.ToInt64(key.GetValue("LastReplicationTimeFt"));
                            ReplicatorCh chr = (ReplicatorCh)Replicator[ch];
                            chr.LastReplicationTime = key.GetValue("LastReplicationTime").ToString();
                            DateTime replicatorTime = DateTime.ParseExact(chr.LastReplicationTime, "dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).Add(+localZone);
                            string interval = DateTime.Now.Subtract(replicatorTime).TotalSeconds.ToString();
                            replicatorSec = interval.Remove(interval.LastIndexOf(','));
                            lastreplicator = replicatorTime.ToString();
                            if (timeHost > chr.LastReplicationTimeFt)
                            {
                                chr.LastReplicationTimeFt = timeHost;
                                replicator = 0;
                            }
                            else
                            {
                                replicator++;
                                LogWriteLine($"***** No replication from crossroad {chr.host}, last replication time {lastreplicator} *****");
                            }
                        }
                    }
                }

                if (replicator > 0)
                {
                    if (replicator > Replicator.Count)
                    {
                        LogWriteLine($"***** Reboot *****");
                        var cmd = new System.Diagnostics.ProcessStartInfo("shutdown.exe", "-r -t 0");
                        cmd.CreateNoWindow = true;
                        cmd.UseShellExecute = false;
                        cmd.ErrorDialog = false;
                        Process.Start(cmd);
                    }
                    else
                    {
                        StopService("VTTrafficReplicator");
                        StopService("VTViolations");
                        StartService("VTTrafficReplicator");
                        StartService("VTViolations");
                    }
                }
            }
        }

        void OnExportRestartTimeout(Object source, ElapsedEventArgs e)
        {
            if (restartingServicesExport)
            {
                string[] files;
                if (Directory.Exists(sourceFolderPr))
                {
                    files = Directory.GetFiles(sourceFolderPr, "*.xml", SearchOption.AllDirectories);
                    export += files.Length;
                }
                if (Directory.Exists(sourceFolderSc))
                {
                    files = Directory.GetFiles(sourceFolderSc, "*.xml", SearchOption.AllDirectories);
                    export += files.Length;
                }
                if (export == 0)
                {
                    LogWriteLine($"***** Export service, there were no unloading violations for {restartingServicesExportIntervalHours} hours. *****");
                    StopService("VTTrafficExport");
                    StopService("VTViolations");
                    StartService("VTTrafficExport");
                    StartService("VTViolations");
                }
                export = 0;
            }
        }

        void StartService(string serviceName)
        {
            ServiceController service = new ServiceController(serviceName);
            if (service.Status != ServiceControllerStatus.Running)
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMinutes(2));
                LogWriteLine($"---------- Service {serviceName} started ----------");
            }
            LogWriteLine($">>>> Service {serviceName} status >>>> {service.Status} <<<<");
        }

        void StopService(string serviceName)
        {
            ServiceController service = new ServiceController(serviceName);
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(2));
                LogWriteLine($"---------- Service {serviceName} stopped ----------");
                if (service.Status != ServiceControllerStatus.StopPending)
                {
                    foreach (var process in Process.GetProcessesByName(serviceName))
                    {
                        process.Kill();
                        LogWriteLine($"---------- Service {serviceName} Kill ----------");
                    }
                }
            }
            LogWriteLine($">>>> Service {serviceName} status >>>> {service.Status} <<<<");
        }

        void processDirectory(string startLocation)
        {
            foreach (var directory in Directory.GetDirectories(startLocation))
            {
                processDirectory(directory);
                if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
                {
                    Directory.Delete(directory, false);
                }
            }
        }

        void SortingFiles(string sourcePath, string outPath)
        {
            if (Directory.Exists(sourcePath))
            {
                XmlDocument xFile = new XmlDocument();
                string[] files = Directory.GetFiles(sourcePath, "*.xml", SearchOption.AllDirectories);
                int countFiles = files.Length;
                export += countFiles;
                foreach (var file in files)
                {
                    string name = Path.GetFileName(file);
                    string PathSour = file.Remove(file.LastIndexOf("\\"));
                    string nameRemote = name.Remove(name.LastIndexOf("_"));
                    xFile.Load(file);
                    if (xFile.SelectSingleNode("//v_photo_ts") != null)
                    {
                        XmlNodeList violation_time = xFile.GetElementsByTagName("v_time_check");
                        string data = violation_time[0].InnerText.Remove(violation_time[0].InnerText.IndexOf("T"));
                        XmlNodeList violation_camera = xFile.GetElementsByTagName("v_camera");
                        XmlNodeList violation_pr_viol = xFile.GetElementsByTagName("v_pr_viol");

                        string Path = outPath + "\\" + data + "\\" + (string)ViolationCode[violation_pr_viol[0].InnerText] + "\\" + violation_camera[0].InnerText + "\\";

                        Console.WriteLine(PathSour);

                        if (!(Directory.Exists(Path)))
                        {
                            Directory.CreateDirectory(Path);
                        }

                        if (storageXML)
                        {
                            File.Copy(file, (Path + name), true);
                        }

                        if (storageСollage && File.Exists(PathSour + "\\" + nameRemote + "_car.jpg"))
                        {
                            File.Copy((PathSour + "\\" + nameRemote + "_car.jpg"), (Path + nameRemote + "_car.jpg"), true);
                        }

                        if (storageVideo && File.Exists(PathSour + "\\" + nameRemote + "__1video.mp4"))
                        {
                            File.Copy((PathSour + "\\" + nameRemote + "__1video.mp4"), (Path + nameRemote + "__1video.mp4"), true);
                        }

                        if (storageVideo && File.Exists(PathSour + "\\" + nameRemote + "__2video.mp4"))
                        {
                            File.Copy((PathSour + "\\" + nameRemote + "__2video.mp4"), (Path + nameRemote + "__2video.mp4"), true);
                        }

                        string[] delFiles = Directory.GetFiles(sourcePath, (nameRemote + "*"), SearchOption.AllDirectories);
                        foreach (string delFile in delFiles)
                        {
                            File.Delete(delFile);
                        }

                        processDirectory(sourcePath);

                        string[] delTimefiles = Directory.GetFiles(outPath, "*", SearchOption.AllDirectories);
                        foreach (string delTimefile in delTimefiles)
                        {
                            FileInfo fi = new FileInfo(delTimefile);
                            if (fi.CreationTime < DateTime.Now.AddDays(-storageDays)) { fi.Delete(); }
                        }
                        processDirectory(outPath);
                    }
                }
                LogWriteLine($"Sorted {countFiles} violations");
            }
        }

        static async void GetSQLViolations()
        {
            string connectionString = $@"Data Source={sqlSource};Initial Catalog=AVTO;User Id={sqlUser};Password={sqlPassword};Connection Timeout=60";
            HttpListener server = new HttpListener();
            server.Prefixes.Add(@"http://+:8090/");
            server.Start();
            while (server.IsListening)
            {
                var context = server.GetContext();
                int deltamin = Convert.ToInt32(context.Request.QueryString["minutes"]);
                DateTime endDateTime = DateTime.UtcNow;
                DateTime sqlDateTime = endDateTime.AddMinutes(-deltamin);
                int number = 0;
                string sqlAlarm = $"SELECT COUNT_BIG(CARS_ID) FROM[AVTO].[dbo].[CARS_VIOLATIONS] WHERE CHECKTIME > '{sqlDateTime:s}'";
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    SqlCommand command = new SqlCommand(sqlAlarm, connection);
                    SqlDataReader reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        number = Convert.ToInt32(reader.GetValue(0));
                    }
                    reader.Close();
                }

                int statusNumber = 0;
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://192.168.88.21/onvif/device_service");
                try
                {
                    HttpResponseMessage response = await httpClient.SendAsync(request);
                    statusNumber = (int)response.StatusCode;
                }
                catch (HttpRequestException e)
                {
                    statusNumber = 404;
                }

                var HttpResponse = context.Response;
                HttpResponse.Headers.Add("Content-Type", "application/json");
                HttpResponse.StatusCode = 200;
                string responseText = "{\"violations\": " + number + ", \"reviewCamera\": " + statusNumber + ", \"lastReplicator\": \"" + lastreplicator + "\", \"lastReplicatorSec\": " + replicatorSec + "}";
                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                HttpResponse.ContentLength64 = buffer.Length;
                HttpResponse.OutputStream.Write(buffer, 0, buffer.Length);
                HttpResponse.Close();
            }
            server.Stop();
            server.Close();
        }

        protected override void OnStart(string[] args)
        {
            ReadIndex();
            LogWriteLine($"---------- Service VT_Sorting START ----------");
            Load_Config();
            HashVuolation();
            GetSQL.Start();
        }

        protected override void OnStop()
        {
            GetSQL.Interrupt();
            LogWriteLine($"---------- Service VT_Sorting STOP ----------");
        }
    }
}

