using System;
using System.Collections;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Timers;
using System.Xml;
using System.Xml.Linq;

namespace VT_Sorting
{
    public partial class Service1 : ServiceBase
    {
        public Service1()
        {
            InitializeComponent();
        }

        Hashtable ViolationCode = new Hashtable();

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

        int storageDays = 10;
        int storageSortingIntervalMinutes = 20;
        bool storageXML = true;
        bool storageСollage = false;
        bool storageVideo = false;
        bool restartingServices = true;
        int serviceRestartIntervalHours = 6;
        int Logindex = 0;

        string sourceFolderPr = "D:\\Duplo";
        string sourceFolderSc = "D:\\Doris";
        string sortingFolderPr = "D:\\!Duplo";
        string sortingFolderSc = "D:\\!Doris";

        void Load_Config()
        {
            if (ConfigurationManager.AppSettings.Count != 0)
            {
                storageDays = Convert.ToInt32(ConfigurationManager.AppSettings["StorageDays"]);
                storageSortingIntervalMinutes = Convert.ToInt32(ConfigurationManager.AppSettings["StorageSortingIntervalMinutes"]);
                storageXML = Convert.ToBoolean(ConfigurationManager.AppSettings["StorageXML"]);
                storageСollage = Convert.ToBoolean(ConfigurationManager.AppSettings["StorageСollage"]);
                storageVideo = Convert.ToBoolean(ConfigurationManager.AppSettings["StorageVideo"]);
                restartingServices = Convert.ToBoolean(ConfigurationManager.AppSettings["RestartingServices"]);
                serviceRestartIntervalHours = Convert.ToInt32(ConfigurationManager.AppSettings["ServiceRestartIntervalHours"]);

                sourceFolderPr = ConfigurationManager.AppSettings["SourceFolderPr"];
                sortingFolderPr = ConfigurationManager.AppSettings["SortingFolderPr"];

                sourceFolderSc = ConfigurationManager.AppSettings["SourceFolderSc"];
                sortingFolderSc = ConfigurationManager.AppSettings["SortingFolderSc"];
            }

            var storageTimer = new System.Timers.Timer(storageSortingIntervalMinutes * 60000);
            storageTimer.Elapsed += OnStorageTimeout;
            storageTimer.AutoReset = true;
            storageTimer.Enabled = true;

            var serviceRestartTimer = new System.Timers.Timer(serviceRestartIntervalHours * 3600000);
            serviceRestartTimer.Elapsed += OnServiceRestartTimeout;
            serviceRestartTimer.AutoReset = true;
            serviceRestartTimer.Enabled = true;
        }

        void ReadIndex()
        {
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
            using (StreamWriter sw = new StreamWriter(logDir + $"\\{name}-log.txt", true))
            {
                sw.WriteLine(String.Format("{0:yyMMdd hh:mm:ss} {1}", DateTime.Now.ToString() + " -", message));
                sw.Close();
                if((logDir + $"\\{name}-log.txt").Length > 10250000) // 10250000
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

        void OnStorageTimeout(Object source, ElapsedEventArgs e)
        {
            SortingFiles(sourceFolderPr, sortingFolderPr);
            SortingFiles(sourceFolderSc, sortingFolderSc);
        }

        void OnServiceRestartTimeout(Object source, ElapsedEventArgs e)
        {
            if (restartingServices)
            {
                StopService("VTTrafficExport");
                StopService("VTTrafficReplicator");
                StopService("VTViolations");
                StopService("VTObjectBusSrv");
                StopService("VTLPRService");

                StartService("VTObjectBusSrv");
                StartService("VTLPRService");
                StartService("VTTrafficReplicator");
                StartService("VTViolations");
                StartService("VTTrafficExport");
            }
        }

        void StartService(string serviceName)
        {
            ServiceController service = new ServiceController(serviceName);
            if (service.Status != ServiceControllerStatus.Running)
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMinutes(1));
                LogWriteLine($"---------- Service {serviceName} started ----------");
            }
        }

        void StopService(string serviceName)
        {
            ServiceController service = new ServiceController(serviceName);
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(1));
                LogWriteLine($"---------- Service {serviceName} stopped ----------");
            }
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
                        //string datatime = violation_time[0].InnerText.Remove(violation_time[0].InnerText.IndexOf(".") - 3);
                        //datatime = datatime.Substring(datatime.IndexOf("T") + 1);
                        //XmlNodeList violation_regno = xFile.GetElementsByTagName("v_regno");

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

        protected override void OnStart(string[] args)
        {
            ReadIndex();
            LogWriteLine($"---------- Service VT_Sorting START ----------");
            Load_Config();
            HashVuolation();
            SortingFiles(sourceFolderPr, sortingFolderPr);
            SortingFiles(sourceFolderSc, sortingFolderSc);
        }

        protected override void OnStop()
        {
            LogWriteLine($"---------- Service VT_Sorting STOP ----------");
        }
    }
}

