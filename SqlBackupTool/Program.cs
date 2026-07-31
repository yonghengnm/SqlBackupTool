using System;
using System.Collections.Generic;
using System.IO;
using System.Data.SqlClient;
using System.Linq;

namespace SqlBackupTool
{
    class Program
    {
        private static readonly string NameConfigFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LastDbNames.txt");
        private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BackupLog.txt");
        private static string ConnString = "";
        private static string CurrentBackupFolder = "";
        private static List<string> GlobalDbList = new List<string>();
        private static string AutoBackupTipText = "";
        //缓存数据库服务器版本号
        private static int ServerMajorVersion = 0;

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.GetEncoding("GBK");
            Console.Title = "SQL Server备份工具(兼容2000/2005/2008R2/2016)";

            string startLog = "======================================" + Environment.NewLine + "程序启动 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine(startLog);
            WriteLog(startLog);

            bool connectOk = TryConnectSqlServer();
            if (!connectOk)
            {
                string errMsg = "数据库连接失败，程序即将退出";
                Console.WriteLine(errMsg);
                WriteLog(errMsg);
                Console.WriteLine("按回车键关闭窗口...");
                Console.ReadLine();
                return;
            }

            //连接成功后读取SQL Server主版本号
            ReadSqlServerVersion();

            GlobalDbList = GetUserDatabaseList();
            RefreshAutoBackupTip();

            int waitSeconds = 30;
            bool autoRunMode = true;

            for (int i = waitSeconds; i > 0; i--)
            {
                string lineText;
                List<string> savedNames = LoadSavedDatabaseNames();
                if (savedNames.Any())
                {
                    string dbNamesInline = string.Join("、", savedNames);
                    lineText = $"倒计时{i}秒，按下任意键进入手动模式；超时将自动备份数据库：{dbNamesInline}";
                }
                else
                {
                    lineText = $"倒计时{i}秒，按下任意键进入手动模式；暂无保存的待备份数据库，自动备份无法执行";
                }
                Console.Write("\r" + lineText);

                if (Console.KeyAvailable)
                {
                    Console.ReadKey(true);
                    autoRunMode = false;
                    break;
                }
                System.Threading.Thread.Sleep(1000);
            }
            Console.WriteLine();
            Console.WriteLine();

            if (autoRunMode)
            {
                Console.WriteLine("==========【自动运行模式】==========");
                WriteLog("进入自动运行模式");

                List<string> savedDbNames = LoadSavedDatabaseNames();
                if (savedDbNames.Count == 0)
                {
                    string errMsg = "未找到保存的数据库名称，无法自动备份！";
                    Console.WriteLine(errMsg);
                    WriteLog(errMsg);
                }
                else
                {
                    Console.WriteLine("待自动备份数据库列表：");
                    savedDbNames.ForEach(x => Console.WriteLine("  " + x));
                    bool backupResult = BackupSelectDatabaseByName(savedDbNames);
                    if (backupResult)
                    {
                        CopyBackupToRemovableDisk();
                    }
                }

                WriteLog("自动模式所有任务执行完毕，程序退出");
                Console.WriteLine("自动任务全部完成，程序退出。");
                return;
            }
            else
            {
                ShowMenu();
                Console.WriteLine(Environment.NewLine + "所有任务执行完毕，按回车键关闭窗口...");
                Console.ReadLine();
            }
        }

        /// <summary>
        /// 获取SQL Server版本主版本号
        /// 8 = 2000；9=2005；10=2008/2008R2；13=2016
        /// </summary>
        private static void ReadSqlServerVersion()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(ConnString))
                {
                    conn.Open();
                    string versionStr = conn.ServerVersion; //类似 8.0.760 /9.0.5000 /10.50.6000 /13.0.5081
                    string major = versionStr.Split('.')[0];
                    int.TryParse(major, out ServerMajorVersion);
                    WriteLog($"检测到SQL Server主版本号：{ServerMajorVersion}");
                }
            }
            catch (Exception ex)
            {
                WriteLog("读取数据库版本失败:" + ex.Message);
            }
        }

        private static void RefreshAutoBackupTip()
        {
            AutoBackupTipText = "";
            List<string> names = LoadSavedDatabaseNames();
            if (names.Any())
            {
                AutoBackupTipText = string.Join(Environment.NewLine, names.Select(n => "  " + n));
            }
        }

        #region 数据库连接
        private static bool TryConnectSqlServer()
        {
            string windowsConn = "Data Source=.;Integrated Security=True;Connect Timeout=8";
            if (TestSqlConnect(windowsConn))
            {
                ConnString = windowsConn;
                string msg = "Windows身份验证连接数据库成功";
                Console.WriteLine(msg);
                WriteLog(msg);
                return true;
            }

            Console.WriteLine("Windows身份验证失败，请手动输入数据库登录信息");
            Console.Write("数据库地址(IP/实例名):");
            string ip = Console.ReadLine().Trim();

            Console.Write("端口(默认1433):");
            string portStr = Console.ReadLine().Trim();
            int port = 1433;
            int.TryParse(portStr, out port);
            if (port <= 0) port = 1433;

            Console.Write("登录用户名:");
            string uid = Console.ReadLine().Trim();
            Console.Write("登录密码:");
            string pwd = Console.ReadLine().Trim();

            string loginConn = string.Format("Data Source={0},{1};User ID={2};Password={3};Connect Timeout=8", ip, port, uid, pwd);
            if (TestSqlConnect(loginConn))
            {
                ConnString = loginConn;
                string msg = "账号密码连接数据库成功";
                Console.WriteLine(msg);
                WriteLog(msg);
                return true;
            }
            return false;
        }

        private static bool TestSqlConnect(string connStr)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region 配置文件
        private static List<string> LoadSavedDatabaseNames()
        {
            List<string> result = new List<string>();
            try
            {
                if (File.Exists(NameConfigFile))
                {
                    var lines = File.ReadAllLines(NameConfigFile)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrEmpty(x));
                    result.AddRange(lines);
                }
            }
            catch (Exception ex)
            {
                WriteLog("读取配置文件异常:" + ex.Message);
            }
            return result;
        }

        private static void SaveDatabaseNames(List<string> dbNames)
        {
            try
            {
                File.WriteAllLines(NameConfigFile, dbNames);
                Console.WriteLine("已保存选中数据库清单，下次自动模式直接使用");
                WriteLog("保存待备份数据库清单：" + string.Join(",", dbNames));
                RefreshAutoBackupTip();
            }
            catch (Exception ex)
            {
                string err = "保存配置失败：" + ex.Message;
                Console.WriteLine(err);
                WriteLog(err);
            }
        }
        #endregion

        #region 日志
        private static void WriteLog(string msg)
        {
            string logLine = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg;
            try
            {
                File.AppendAllText(LogFile, logLine + Environment.NewLine);
            }
            catch { }
        }
        #endregion

        #region 菜单
        private static void ShowMenu()
        {
            while (true)
            {
                Console.WriteLine(Environment.NewLine + "============功能菜单============");
                Console.WriteLine("【1】备份所有用户数据库");
                Console.WriteLine("【2】备份指定数据库");
                Console.WriteLine("【0】退出程序");
                Console.Write("请输入选项：");
                string select = Console.ReadLine().Trim();
                if (select == "1")
                {
                    BackupAllDatabase();
                    CopyBackupToRemovableDisk();
                }
                else if (select == "2")
                {
                    GlobalDbList = GetUserDatabaseList();
                    Console.WriteLine("当前可用用户数据库列表：");
                    for (int i = 0; i < GlobalDbList.Count; i++)
                    {
                        Console.WriteLine("[" + (i + 1) + "] " + GlobalDbList[i]);
                    }
                    Console.Write("输入序号(多个英文逗号分隔，例如1,3):");
                    string inputIndexText = Console.ReadLine().Trim();

                    List<string> targetDbNames = new List<string>();
                    string[] inputArr = inputIndexText.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string s in inputArr)
                    {
                        int num;
                        if (int.TryParse(s.Trim(), out num))
                        {
                            int idx = num - 1;
                            if (idx >= 0 && idx < GlobalDbList.Count)
                            {
                                targetDbNames.Add(GlobalDbList[idx]);
                            }
                        }
                    }

                    if (targetDbNames.Count > 0)
                    {
                        SaveDatabaseNames(targetDbNames);
                        BackupSelectDatabaseByName(targetDbNames);
                        CopyBackupToRemovableDisk();
                    }
                    else
                    {
                        Console.WriteLine("未识别到有效序号，不执行备份");
                    }
                }
                else if (select == "0")
                {
                    break;
                }
                else
                {
                    Console.WriteLine("输入无效，请重新选择");
                }
            }
        }
        #endregion

        #region 备份核心逻辑
        /// <summary>
        /// 自适应获取数据库列表（兼容SQL2000 sysdatabases / 高版本sys.databases）
        /// </summary>
        private static List<string> GetUserDatabaseList()
        {
            List<string> list = new List<string>();
            string sql;
            //ServerMajorVersion ==8 → SQL Server2000
            if (ServerMajorVersion == 8)
            {
                //SQL2000: dbid>4排除系统库，status &32=0 代表数据库online
                sql = "SELECT name FROM master.dbo.sysdatabases WHERE dbid>4 AND (status & 32)=0";
            }
            else
            {
                //2005~2016
                sql = "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') AND state=0";
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(ConnString))
                {
                    conn.Open();
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    SqlDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(reader["name"].ToString());
                    }
                    reader.Close();
                }
            }
            catch (Exception ex)
            {
                WriteLog("获取数据库列表失败:" + ex.Message);
                Console.WriteLine("读取数据库列表出错！" + ex.Message);
            }
            return list;
        }

        private static void CreateBackupDir()
        {
            string folderName = "SqlBackup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            CurrentBackupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);
            if (!Directory.Exists(CurrentBackupFolder))
            {
                Directory.CreateDirectory(CurrentBackupFolder);
            }
            string msg = "备份目录：" + CurrentBackupFolder;
            Console.WriteLine(msg);
            WriteLog(msg);
        }

        /// <summary>
        /// 自适应备份语句，SQL2000移除COMPRESSION
        /// </summary>
        private static bool SingleBackup(string dbName)
        {
            try
            {
                string bakFile = Path.Combine(CurrentBackupFolder, dbName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
                string bakSql;
                if (ServerMajorVersion >= 9)
                {
                    //2005及以上，启用压缩
                    bakSql = $"BACKUP DATABASE [{dbName}] TO DISK = N'{bakFile}' WITH COMPRESSION;";
                }
                else
                {
                    //SQL2000 禁止COMPRESSION
                    bakSql = $"BACKUP DATABASE [{dbName}] TO DISK = N'{bakFile}';";
                }

                Console.WriteLine("正在备份：" + dbName);
                using (SqlConnection conn = new SqlConnection(ConnString))
                {
                    conn.Open();
                    SqlCommand cmd = new SqlCommand(bakSql, conn);
                    cmd.ExecuteNonQuery();
                }
                string okMsg = dbName + " 备份成功";
                Console.WriteLine(okMsg);
                WriteLog(okMsg);
                return true;
            }
            catch (Exception ex)
            {
                string errMsg = dbName + " 备份失败:" + ex.Message;
                Console.WriteLine(errMsg);
                WriteLog(errMsg);
                return false;
            }
        }

        private static void BackupAllDatabase()
        {
            Console.WriteLine(Environment.NewLine + "==========开始备份所有用户数据库==========");
            WriteLog("开始执行【全部数据库备份】");
            CreateBackupDir();
            List<string> dbList = GetUserDatabaseList();
            foreach (string db in dbList)
            {
                SingleBackup(db);
            }
            Console.WriteLine("==========全部数据库备份执行结束==========");
            WriteLog("全部数据库备份任务执行结束");
        }

        private static bool BackupSelectDatabaseByName(List<string> dbNameList)
        {
            Console.WriteLine(Environment.NewLine + "==========开始备份选定数据库==========");
            WriteLog("开始执行选定数据库备份，清单：" + string.Join(",", dbNameList));

            CreateBackupDir();
            bool anySuccess = false;
            List<string> currentAllDb = GetUserDatabaseList();

            foreach (string dbName in dbNameList)
            {
                if (!currentAllDb.Contains(dbName))
                {
                    string warn = "警告：数据库[" + dbName + "]不存在，跳过备份";
                    Console.WriteLine(warn);
                    WriteLog(warn);
                    continue;
                }
                bool res = SingleBackup(dbName);
                if (res) anySuccess = true;
            }

            Console.WriteLine("==========选定数据库备份执行结束==========");
            WriteLog("选定数据库备份任务执行结束");
            return anySuccess;
        }
        #endregion

        #region U盘复制
        private static void CopyBackupToRemovableDisk()
        {
            if (string.IsNullOrEmpty(CurrentBackupFolder) || !Directory.Exists(CurrentBackupFolder))
            {
                WriteLog("无备份目录，跳过U盘复制");
                return;
            }
            Console.WriteLine(Environment.NewLine + "==========扫描移动存储设备==========");
            WriteLog("开始扫描U盘/移动硬盘进行文件复制");
            DriveInfo[] allDrives = DriveInfo.GetDrives();
            bool foundUsb = false;
            foreach (DriveInfo drive in allDrives)
            {
                if (drive.DriveType == DriveType.Removable && drive.IsReady)
                {
                    foundUsb = true;
                    string destFolder = Path.Combine(drive.RootDirectory.FullName, Path.GetFileName(CurrentBackupFolder));
                    Console.WriteLine("检测到移动盘 " + drive.Name + "，复制至 " + destFolder);
                    try
                    {
                        CopyDirectory(CurrentBackupFolder, destFolder, true);
                        string okMsg = drive.Name + " 文件复制完成";
                        Console.WriteLine(okMsg);
                        WriteLog(okMsg);
                    }
                    catch (Exception ex)
                    {
                        string errMsg = drive.Name + " 复制失败:" + ex.Message;
                        Console.WriteLine(errMsg);
                        WriteLog(errMsg);
                    }
                }
            }
            if (!foundUsb)
            {
                string msg = "未找到就绪的U盘/移动硬盘，跳过复制";
                Console.WriteLine(msg);
                WriteLog(msg);
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
        {
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }
            string[] files = Directory.GetFiles(sourceDir);
            foreach (string file in files)
            {
                string targetFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, targetFile, overwrite);
            }
            string[] subDirs = Directory.GetDirectories(sourceDir);
            foreach (string sub in subDirs)
            {
                string subDest = Path.Combine(destDir, Path.GetFileName(sub));
                CopyDirectory(sub, subDest, overwrite);
            }
        }
        #endregion
    }
}