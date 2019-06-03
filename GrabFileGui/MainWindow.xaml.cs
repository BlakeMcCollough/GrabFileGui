using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using ZetaIpc.Runtime.Client;

namespace GrabFileGui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string path;
        private bool taskRunning; //makes sure that when TaskList is loaded again by mistake, it won't start a new work thread
        private bool stopped;
        private int delay;
        private CancellationTokenSource killDelay;

        public MainWindow()
        {
            path = "";
            taskRunning = false;
            stopped = false;
            delay = 1000;
            killDelay = new CancellationTokenSource(); //kills the delay when next is pressed
            InitializeComponent();
        }

        //ESSENTIALLY works as main, responsible for waiting a second and updating the tasks
        private async void TaskList_Loaded() //I've added async here so that this runs asynchronously, meaning I can use Task.Delay without shutting down the UI
        {
            if (taskRunning == true || string.IsNullOrEmpty(path))
            {
                return;
            }
            taskRunning = true;
            Tabs.IsEnabled = true;

            const int LENGTH_DSK = 10;
            const int SEC = 1;
            const int TSK = 0, CLNT = 1, APP = 2, VER = 3, IAR = 4, CK = 5, SVC = 6, CPU = 7, FILE = 8, KEY = 9, DA = 10, RD = 11, WR = 12; //corresponds to index in task list
            int[] LENGTH_TSK = { 3, 12, 8, 8, 4, 2, 2, 12, 7, 8, 8, 8, 8 }; //list of sizes corresponding to task consts
            const int DTSK = 2, SEIZE = 4, QUEUE = 6, DAPP = 7, DIAR = 8, TIME = 9; //corresponds to index in disklog list

            //checks for three numbers, 12 of any characters, any word (including .) and the rest of the data entry in the format of the grab file
            //the purpose is to check if the line read is a data entry
            Regex tskAcceptRegex = new Regex(@"^\d{3} .{12} .{8} .{8} \w{4} \w\w \w\w \d\d:\d\d:\d\d:\d\d\d .{7} \w{8} \w{8} \w{8} \w{8} *$");
            //similar to tskAccept, but it checks if it's a header to a new second
            Regex stepAcceptRegex = new Regex(@"^\[\d\d Task info \d+]$");
            //similar to tskAccept, but checks for disk seize header
            Regex diskAcceptRegex = new Regex(@"^\[\d\d Disk seize]$");
            

            StreamReader infile = new StreamReader(path);
            string line = infile.ReadLine();
            int secondCounter = 1;
            double netUsage = 0;

            List<Task> runningTasks = new List<Task>(); //list of task objects to be displayed by DataGrid
            List<DiskUsageLog> diskLogs = new List<DiskUsageLog>();
            List<string> usageQuery = new List<string>(); //if CPU changed for a task, its TSK index is stored here in an attempt to reduce time complexity
            TaskList.ItemsSource = runningTasks;
            DiskList.ItemsSource = diskLogs;
            string startupString = line;

            while (line != null && taskRunning == true)
            {
                if (tskAcceptRegex.IsMatch(line) == true) //is an acceptable entry
                {
                    //since client should contain any character, it cannot be tokenized by whitespace. Hardcoding instead
                    string tskLine = line.Substring(0, LENGTH_TSK[TSK]);
                    string clientLine = line.Substring(LENGTH_TSK[TSK] + 1, LENGTH_TSK[CLNT]);
                    line = line.Substring(LENGTH_TSK[TSK] + LENGTH_TSK[CLNT] + 1 + 1);
                    string[] returnedSplitList = Regex.Split(line, @" +");
                    
                    List<string> taskElements = returnedSplitList.OfType<string>().ToList(); //fancy way of transforming string array to list
                    taskElements.Insert(0, clientLine);
                    taskElements.Insert(0, tskLine);

                    if (taskElements.Count != LENGTH_TSK.Length) //list is not the correct size, meaning there is a blank entry somewhere
                    {
                        int rdHead = 0;
                        for(int i = 2; i < 13; i++)
                        {
                            if(string.IsNullOrWhiteSpace(line.Substring(rdHead, LENGTH_TSK[i])))
                            {
                                taskElements.Insert(i, " ");
                            }
                            rdHead = rdHead + LENGTH_TSK[i] + 1;
                        }
                    }
                    if (runningTasks.Exists(x => x.TSK == taskElements[TSK]))
                    {
                        Task foundOccurance = runningTasks.Find(x => x.TSK == taskElements[TSK]);
                        foundOccurance.TSK = taskElements[TSK];
                        foundOccurance.Client = taskElements[CLNT];
                        foundOccurance.App = taskElements[APP];
                        foundOccurance.Version = taskElements[VER];
                        foundOccurance.IAR = taskElements[IAR];
                        foundOccurance.CK = taskElements[CK];
                        foundOccurance.SVC = taskElements[SVC];
                        foundOccurance.CPU = "0%";
                        foundOccurance.CPUTime = TimeSpan.Parse(taskElements[CPU].Remove(8) + "." + taskElements[CPU].Substring(9));
                        foundOccurance.File = taskElements[FILE];
                        foundOccurance.KeyCalls = int.Parse(taskElements[KEY], System.Globalization.NumberStyles.HexNumber);
                        foundOccurance.DACalls = int.Parse(taskElements[DA], System.Globalization.NumberStyles.HexNumber);
                        foundOccurance.DskReads = int.Parse(taskElements[RD], System.Globalization.NumberStyles.HexNumber);
                        foundOccurance.DskWrite = int.Parse(taskElements[WR], System.Globalization.NumberStyles.HexNumber);

                        double differenceCPU = 1 - (new TimeSpan(0, 0, SEC) - foundOccurance.CPUTime).TotalSeconds;
                        if (differenceCPU > 0)
                        {
                            netUsage = netUsage + differenceCPU;
                            usageQuery.Insert(0, taskElements[TSK]); //even though TSK index acts like an int, it's really a string
                        }
                    }
                    else
                    {
                        runningTasks.Add(new Task()
                        {
                            TSK = taskElements[TSK],
                            Client = taskElements[CLNT],
                            App = taskElements[APP],
                            Version = taskElements[VER],
                            IAR = taskElements[IAR],
                            CK = taskElements[CK],
                            SVC = taskElements[SVC],
                            CPU = "0%",
                            CPUTime = TimeSpan.Parse(taskElements[CPU].Remove(8) + "." + taskElements[CPU].Substring(9)),
                            File = taskElements[FILE],
                            KeyCalls = int.Parse(taskElements[KEY], System.Globalization.NumberStyles.HexNumber),
                            DACalls = int.Parse(taskElements[DA], System.Globalization.NumberStyles.HexNumber),
                            DskReads = int.Parse(taskElements[RD], System.Globalization.NumberStyles.HexNumber),
                            DskWrite = int.Parse(taskElements[WR], System.Globalization.NumberStyles.HexNumber)
                        });
                        double differenceCPU = 1 - (new TimeSpan(0, 0, SEC) - runningTasks.Last().CPUTime).TotalSeconds;
                        if (differenceCPU > 0)
                        {
                            netUsage = netUsage + differenceCPU;
                            usageQuery.Insert(0, taskElements[TSK]);
                        }
                    }
                }
                else if(stepAcceptRegex.IsMatch(line) == true && runningTasks.Count != 0) //assumes that each second runs in order
                {
                    //this nested loop takes all the indexes in usageQuery, sorts them by seconds, then moves them to the top of runningTasks
                    foreach(string index in usageQuery)
                    {
                        Task currentTask = runningTasks.Find(x => x.TSK == index);
                        if(currentTask != null)
                        {
                            currentTask.CPU = String.Concat(Math.Round(((currentTask.CPUTime).TotalSeconds * 100), 2).ToString(), "%"); //doesn't calculate anything anymore, just a % rep of seconds difference
                            runningTasks.Remove(currentTask);
                            int i = 0;
                            while(runningTasks[i].CPUTime.TotalSeconds > currentTask.CPUTime.TotalSeconds)
                            {
                                i = i + 1;
                            }
                            runningTasks.Insert(i, currentTask);
                        }
                    }

                    TaskList.Items.Refresh();
                    DiskList.Items.Refresh();

                    do
                    {
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(delay, killDelay.Token);
                        }
                        catch
                        {
                            //next has been pressed
                            killDelay.Dispose();
                            killDelay = new CancellationTokenSource();
                            break;
                        }
                        
                    } while (stopped == true);

                    usageQuery.Clear();
                    netUsage = 0;
                    secondCounter = secondCounter + 1;
                }
                else if(stepAcceptRegex.IsMatch(line) == true)
                {
                    StartupLog.Text = startupString;
                }
                else if(diskAcceptRegex.IsMatch(line) == true)
                {
                    line = infile.ReadLine(); //we're doing a readline here since the next line will (hopefully) be disk stuff
                    string[] returnedSplitList = Regex.Split(line, @" +|,|=");
                    List<string> diskElements = returnedSplitList.OfType<string>().ToList(); //fancy way of transforming string array to list
                    if(diskElements.Count == LENGTH_DSK)
                    {
                        diskLogs.Add(new DiskUsageLog()
                        {
                            Sec = secondCounter,
                            TSK = diskElements[DTSK],
                            Seize = diskElements[SEIZE],
                            Queue = diskElements[QUEUE],
                            App = diskElements[DAPP],
                            IAR = diskElements[DIAR],
                            TimeStamp = diskElements[TIME]
                        });
                    }
                }

                if(StartupLog.Text == "")
                {
                    startupString = startupString + line + "\n";
                }
                line = infile.ReadLine();
            }

            if(runningTasks.Any() == true && taskRunning == true) //read through the entire grab file
            {
                MessageBox.Show("End of file");
            }
            else if(runningTasks.Any() == true) //reading through grab file, but was interrupted
            {
                Tabs.IsEnabled = false;
            }
            else //didn't read anything
            {
                Tabs.IsEnabled = false;
                MessageBox.Show("There appears to be no data\nAre you using the correct file format?");
            }

            taskRunning = false;
            infile.Close();
        }


        private void DiskList_Loaded(object sender, RoutedEventArgs e)
        {
            //Console.WriteLine("Loaded disklist gui");
        }

        private void OpenGrab_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog newFileWindow = new OpenFileDialog();
            if(newFileWindow.ShowDialog() == true)
            {
                taskRunning = false;
                killDelay.Cancel();
                path = newFileWindow.FileName;
                TaskList_Loaded();
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            killDelay.Cancel();
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            if (stopped == true)
            {
                stopped = false;
            }
            else
            {
                stopped = true;
            }
        }

        private void highSpeed_Click(object sender, RoutedEventArgs e)
        {
            delay = 500;
            highSpeed.IsChecked = true;
            medSpeed.IsChecked = false;
            lowSpeed.IsChecked = false;
        }

        private void medSpeed_Click(object sender, RoutedEventArgs e)
        {
            delay = 1000;
            highSpeed.IsChecked = false;
            medSpeed.IsChecked = true;
            lowSpeed.IsChecked = false;
        }

        private void lowSpeed_Click(object sender, RoutedEventArgs e)
        {
            delay = 5000;
            highSpeed.IsChecked = false;
            medSpeed.IsChecked = false;
            lowSpeed.IsChecked = true;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
