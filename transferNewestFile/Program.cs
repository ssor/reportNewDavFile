using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Timers;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Net;
using System.Threading;
/*
/**********************************************************************************
 * 
 * 如果文件的名称变小，将可能将其认为是最新的，默认文件名称只能越来越大
 * 超过指定数目将会删除名称最小的文件
 * 
**********************************************************************************
 */
namespace transferNewestFile
{
    class Program
    {
        static int MAX_FILE_COUNT = 5;
        static string src_file_path = @"C:\davs";
        static List<string> list_transfered_names = new List<string>();//已经处理过的文件的名称列表
        static Socket udp_client;
        //static IPEndPoint ipep;
        //static int udp_des_port = 10001;
        static Dictionary<string, Action<string>> udp_client_fun_list = null;//保存发送dav文件名到目的UDP的函数列表


        static void Main(string[] args)
        {
            //exportData();
            importData(@"./config/config_dav_reporter.txt");

            udp_client_fun_list = initial_udp_client_fun(new List<int> { 10001, 10002, 10003 }, null);

            Console.WriteLine("系统启动...");
            Console.WriteLine("Dav文件夹： " + src_file_path);
            Console.WriteLine("最大缓存文件数： " + MAX_FILE_COUNT.ToString());
            //Console.WriteLine("目标UDP端口： " + udp_des_port.ToString());


            System.Timers.Timer timer = new System.Timers.Timer(3000);
            timer.Elapsed += (sender, e) =>
            {
                start_loop(src_file_path);

            };
            timer.Enabled = true;

            //start_loop();
            //start_loop();
            Console.ReadLine();
        }
        static Dictionary<string, Action<string>> initial_udp_client_fun(List<int> port_list, Dictionary<string, Action<string>> _udp_client_fun_list)
        {
            if (_udp_client_fun_list == null)
            {
                _udp_client_fun_list = new Dictionary<string, Action<string>>();
            }
            if (port_list.Count <= 0) return _udp_client_fun_list;

            Socket _udp_client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            int count = port_list.Count;
            int port = port_list[count - 1];
            IPEndPoint local_ipep = new IPEndPoint(IPAddress.Parse(GetLocalIP4()), port_list[count - 1]);

            string name = "Player" + port.ToString();
            string full_path = "./videoApp/Player/" + name + ".exe";

            ProcessStartInfo info;
            info = new ProcessStartInfo(full_path);
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.CreateNoWindow = false;
            info.RedirectStandardOutput = true;
            info.UseShellExecute = false;


            _udp_client_fun_list.Add(port.ToString(), (_data) =>
            {
                System.Threading.Thread thread = new Thread(new ThreadStart(() =>
{

    byte[] data = Encoding.ASCII.GetBytes(_data);
    //关闭当前的player，启动一个新的player

    try
    {

        Process[] finded_processes = Process.GetProcessesByName(name);
        if (finded_processes.Length > 0)
        {
            for (int i = 0; i < finded_processes.Length; i++)
            {
                finded_processes[i].Kill();
            }
        }

        Console.WriteLine(name + " 已经退出，即将启动...");
        Process process = Process.Start(info);

        Console.WriteLine(name + " 已经启动...");

        using (StreamReader reader = process.StandardOutput)
        {
            string line = reader.ReadLine();
            while (line != null)
            {
                Console.WriteLine(line);
                line = reader.ReadLine();
            }
        }

        Thread.Sleep(3000);
        _udp_client.SendTo(data, data.Length, SocketFlags.None, local_ipep);
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }

}));
                thread.Start();
            });

            return initial_udp_client_fun(port_list.GetRange(0, count - 1), _udp_client_fun_list);
        }
        static void start_loop(string _src_file_path)
        {
            DirectoryInfo TheFolder = new DirectoryInfo(_src_file_path);

            FileInfo[] all_files = TheFolder.GetFiles();

            //找出每类文件的最新的一个
            List<string> list_newest_file
                = Find_newest_file_list(all_files.Select(_file_info => _file_info.Name).ToList<string>(), null, _src_file_path);

            Debug.WriteLine("每类最新文件的列表如下：");
            Display(list_newest_file, false);

            //查找是否已经处理过
            List<string> list_no_transfered_file
                = Find_not_transfered_file(list_newest_file, list_transfered_names);

            if (list_no_transfered_file.Count > 0)
            {
                Console.WriteLine("未处理的文件列表如下：");
                Debug.WriteLine("未处理的文件列表如下：");
                Display(list_no_transfered_file, true);
            }
            else
            {
                Debug.WriteLine("未发现未处理的文件");
            }

            //处理新找到的未处理文件并更新列表
            list_transfered_names
                = Act_on_file(list_no_transfered_file, list_transfered_names, _src_file_path);
        }
        static List<string> Act_on_file(List<string> _list_no_transfered_dav,
                                               List<string> _list_transfered_names,
                                               string _src_file_path)
        {
            int len = _list_no_transfered_dav.Count;
            if (len <= 0) return _list_transfered_names;

            string file_name = _list_no_transfered_dav[0];
            // do act on file

            //将文件名通过UDP协议发送给处理程序
            report_file_name_by_udp(file_name);

            List<string> list_refreshed_transfered_names
                = Refresh_transfered_names_list(file_name, _list_transfered_names);

            List<string> list_next_loop = _list_no_transfered_dav.GetRange(1, len - 1);

            return Act_on_file(list_next_loop, list_refreshed_transfered_names, _src_file_path);
        }

        private static void report_file_name_by_udp(string file_name)
        {
            Console.WriteLine("Report => " + file_name);
            try
            {
                udp_client_fun_list[Get_group_id(file_name)](file_name);
            }
            catch { }
            //byte[] data = Encoding.ASCII.GetBytes(file_name);
            //udp_client.SendTo(data, data.Length, SocketFlags.None, ipep);
        }

        static string Get_des_folder_existed_file(string _file_name, string _file_path)
        {
            DirectoryInfo TheFolder = new DirectoryInfo(_file_path);
            FileInfo[] all_files = TheFolder.GetFiles();

            var list_file_with_same_group = all_files.Where(_file_info => { return Get_group_id(_file_info.Name) == Get_group_id(_file_name); })
                                                   .Select(_file_info => _file_info.Name).ToList<string>();

            if (list_file_with_same_group.Count > 0)
            {
                return list_file_with_same_group[0];
            }
            else
            {
                return null;
            }
        }
        //刷新已经处理过的文件名列表  首先删除同类的名称，再添加新处理的文件的名称
        private static List<string> Refresh_transfered_names_list(string file_name,
                                                                  List<string> _list_transfered_names)
        {

            var list_new = _list_transfered_names.Where(_name => Get_group_id(_name) != Get_group_id(file_name)).ToList<string>();
            list_new.Add(file_name);
            return list_new;
        }

        private static string Get_group_id(string file_name)
        {
            if (file_name.IndexOf("-") >= 0)
            {
                return file_name.Substring(0, file_name.IndexOf("-"));
            }
            else
                return string.Empty;
        }

        /// <summary>
        /// 查找未处理过的文件（,如果文件的名称变小，将可能将其认为是最新的）
        /// </summary>
        /// <param name="list_newest_file"></param>
        /// <param name="list_transfered_file_name"></param>
        /// <returns></returns>
        static List<string> Find_not_transfered_file(List<string> list_newest_file,
                                                     List<string> list_transfered_file_name)
        {
            return list_newest_file.Except(list_transfered_file_name).ToList<string>();
        }

        //找到不同类里面最新的文件
        static List<string> Find_newest_file_list(List<string> files,
                                                  List<string> _list_finded_newest_file,
                                                  string _file_path)
        {
            if (_list_finded_newest_file == null)
            {
                _list_finded_newest_file = new List<string>();
            }
            if (files == null) return _list_finded_newest_file;



            int totalCount = files.Count;
            if (totalCount > 0)
            {
                List<string> list_files = Get_group_file_list(files, _list_finded_newest_file);

                if (list_files != null)
                {
                    string newest_file = Get_newest_file_from_list(list_files, _file_path);
                    if (newest_file != string.Empty)
                    {
                        List<string> list_finded_newest_file = new List<string>(_list_finded_newest_file);
                        list_finded_newest_file.Add(newest_file);

                        return Find_newest_file_list(files.Except(list_files).ToList<string>(), list_finded_newest_file, _file_path);
                    }
                    else
                        return Find_newest_file_list(files.Except(list_files).ToList<string>(), _list_finded_newest_file, _file_path);

                }
                else
                {
                    return Find_newest_file_list(null, _list_finded_newest_file, _file_path);
                }
            }
            else
            {
                return _list_finded_newest_file;
            }

        }

        //最新文件出现时，可能还处于被创建程序写锁定，所以应该返回最靠近最新的一个文件名
        private static string Get_newest_file_from_list(List<string> _list_files, string _file_path)
        {
            if (_list_files == null) return null;
            if (_list_files.Count == 0) return string.Empty;
            //if (_list_files.Count == 1) return _list_files[0];

            List<string> list_files = new List<string>(_list_files);
            list_files.Sort((_first, _second) =>
             {
                 return string.Compare(_second, _first);
             });

            List<string> deleted_file_list = Delete_files(list_files, _file_path);
            if (deleted_file_list.Count > 1)
            {
                return deleted_file_list[1];
            }
            else
                return string.Empty;

        }

        //删除存储过多的文件，返回经过排序的文件列表
        static List<string> Delete_files(List<string> _list_files, string _file_path)
        {

            //对数量过多进行处理
            if (_list_files.Count > MAX_FILE_COUNT)
            {
                string file_name = _list_files[_list_files.Count - 1];
                try
                {
                    File.Delete(_file_path + "\\" + file_name);
                }
                catch { }
                return Delete_files(_list_files.GetRange(0, _list_files.Count - 1), _file_path);
            }
            else
            {
                return _list_files;
            }

        }

        /// <summary>
        /// 返回参数中文件名的group_name与参数列表中第一个文件的group_name相同的文件列表
        /// </summary>
        /// <param name="files">文件列表</param>
        /// <returns></returns>
        private static List<string> Get_group_file_list(List<string> files, List<string> _finded_new_files)
        {
            if (files.Count <= 0) return null;

            string file_name = files[0];
            string group_name = Get_group_id(file_name);

            return (from _file in files
                    where Get_group_id(_file) == group_name
                    select _file).ToList<string>();
        }//获取该组图片的文件名列表
        private static void Display(List<string> list, bool onConsole)
        {
            foreach (string s in list)
            {
                if (s == null)
                {
                    if (onConsole)
                    {
                        Console.WriteLine("(null)");
                    }
                    Debug.WriteLine("(null)");
                }
                else
                {
                    if (onConsole)
                    {
                        Console.WriteLine("\"{0}\"", s);
                    }
                    Debug.WriteLine("\"{0}\"", s);
                }
            }
            if (onConsole)
            {
                Console.WriteLine();
            }
        }
        static void importData(string config_path)
        {
            StreamReader srReadFile1 = new StreamReader(config_path);
            string strConfig = srReadFile1.ReadToEnd();
            srReadFile1.Close();
            // eg. {"src_file_path":"C:\\Users\\ssor\\Desktop\\pics","dest_file_path":"C:\\Users\\ssor\\Desktop\\picpng","max_file_count":5}
            Debug.WriteLine(strConfig);
            Config cfg = (Config)JsonConvert.DeserializeObject<Config>(strConfig);
            if (cfg != null)
            {
                src_file_path = cfg.src_file_path;
                MAX_FILE_COUNT = cfg.max_file_count;
            }
        }
        static string exportData()
        {
            Config cfg = new Config(src_file_path, MAX_FILE_COUNT);
            string output = JsonConvert.SerializeObject(cfg);

            return output;
        }
        static string GetLocalIP4()
        {
            IPAddress ipAddress = null;
            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            for (int i = 0; i < ipHostInfo.AddressList.Length; i++)
            {
                ipAddress = ipHostInfo.AddressList[i];
                if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    break;
                }
                else
                {
                    ipAddress = null;
                }
            }
            if (null == ipAddress)
            {
                return null;
            }
            return ipAddress.ToString();
        }
    }
    public class Config
    {
        public string src_file_path;
        public int max_file_count;

        public Config(string _src_file_path, int _max_file_count)
        {
            this.src_file_path = _src_file_path;
            this.max_file_count = _max_file_count;
        }


    }
}
