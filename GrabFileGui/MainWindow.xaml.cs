using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using System.Windows.Controls.Primitives;
using System.IO.Pipes;
using System.IO.Compression;

namespace GrabFileGui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Constants Consts;

        private string Path;
        private string CopyrightBox;
        private string HistoryRoot;
        private string HistoryFolder;
        private bool TaskRunning; //makes sure that when TaskList is loaded again by mistake, it won't start a new work thread
        private bool Stopped;
        private bool FileOpened;
        private bool WindowIsLoaded;
        private bool WaitingOnFeedback; //is true when readNextLine() is awaiting input
        private int Delay;
        private int SortBy;
        private StreamReader infile;
        private StreamWriter outfile;
        private ClientPipe ReadPipe;
        private CancellationTokenSource KillDelay;

        //stuff below WERE things that used to be local but are now global to help divide and conquer
        UsageGraph CPUgraph;
        private string line;
        private List<Task> RunningTasks;
        private List<DiskUsageLog> DiskLogs;
        private List<string> UsageQuery;
        private double NetUsage;
        int SecondCounter;

        public MainWindow()
        {
            Consts = new Constants();

            Path = "live";
            HistoryRoot = "GrabHistory";
            HistoryFolder = HistoryRoot + @"\" + DateTime.Now.ToString("MMddyyyy");
            FileOpened = false;
            TaskRunning = false;
            Stopped = false;
            WindowIsLoaded = false;
            Delay = 1000;
            SortBy = 1;
            KillDelay = new CancellationTokenSource(); //kills the Delay when next is pressed
            InitializeComponent();
        }

        //when live data is being read, this is called first to prepare a file to write to; also responsible for zipping/deleting files
        private void InitializeGrabHistory() //creator of a new file to log grab history
        {
            if (Directory.Exists(HistoryFolder) == false)
            {
                Directory.CreateDirectory(HistoryFolder);
            }

            foreach(string dir in Directory.GetDirectories(HistoryRoot))
            {
                if(String.Compare(dir, HistoryFolder) != 0)
                {
                    ZipFile.CreateFromDirectory(dir, dir + ".zip");
                    Directory.Delete(dir, true);
                }
            }
            foreach (string file in Directory.GetFiles(HistoryRoot, "*.zip"))
            {
                DateTime then = Directory.GetLastAccessTimeUtc(file);
                DateTime now = DateTime.UtcNow;
                if(now.Subtract(then).TotalDays >= 14)
                {
                    File.Delete(file);
                    //Directory.Delete(file, true);
                }
            }

            string newFile = HistoryFolder + @"\" + DateTime.Now.ToString("HHmmss") + ".txt";
            outfile = new StreamWriter(newFile);
        }

        //writes things to grabhistory file
        private void UpdateGrabHistory(string newInput)
        {
            try //input is lost if an exception is thrown
            {
                outfile.WriteLine(newInput);
            }
            catch
            {
                InitializeGrabHistory();
            }
        }

        //should be called when we want more information, is async so it can wait however long it may take the server
        private async System.Threading.Tasks.Task<string> ReadNextLine()
        {
            if(FileOpened == true)
            {
                return infile.ReadLine();
            }

            WaitingOnFeedback = true;
            System.Threading.Tasks.Task<string> t1 = System.Threading.Tasks.Task.Run(() => ReadPipe.Read());
            await t1;
            WaitingOnFeedback = false;
            UpdateGrabHistory(t1.Result);
            return t1.Result;
        }

        //adds or updates a task in RunningTasks using data passed as a list of strings
        private Task UpdateTasks(List<string> taskElements)
        {
            Task foundOccurance = RunningTasks.Find(x => x.TSK == taskElements[Consts.TSK]);
            if(foundOccurance == null)
            {
                foundOccurance = new Task();
            }
            foundOccurance.TSK = taskElements[Consts.TSK];
            foundOccurance.Client = taskElements[Consts.CLNT];
            foundOccurance.App = taskElements[Consts.APP];
            foundOccurance.Version = taskElements[Consts.VER];
            foundOccurance.IAR = taskElements[Consts.IAR];
            foundOccurance.CK = taskElements[Consts.CK];
            foundOccurance.SVC = taskElements[Consts.SVC];
            foundOccurance.CPU = "0%";
            foundOccurance.CPUTime = TimeSpan.Parse(taskElements[Consts.CPU].Remove(Consts.TIME_DOT) + "." + taskElements[Consts.CPU].Substring(Consts.TIME_DOT + 1));
            foundOccurance.File = taskElements[Consts.FILE];
            foundOccurance.KeyCalls = int.Parse(taskElements[Consts.KEY], System.Globalization.NumberStyles.HexNumber);
            foundOccurance.DACalls = int.Parse(taskElements[Consts.DA], System.Globalization.NumberStyles.HexNumber);
            foundOccurance.DskReads = int.Parse(taskElements[Consts.RD], System.Globalization.NumberStyles.HexNumber);
            foundOccurance.DskWrite = int.Parse(taskElements[Consts.WR], System.Globalization.NumberStyles.HexNumber);

            double differenceCPU = 1 - (new TimeSpan(0, 0, Consts.SEC) - foundOccurance.CPUTime).TotalSeconds;
            if (differenceCPU > 0)
            {
                NetUsage = NetUsage + differenceCPU;
                UsageQuery.Insert(0, taskElements[Consts.TSK]);
            }

            return foundOccurance;
        }

        private void AddToDiskQuery(List<string> diskElements)
        {
            if (diskElements.Count == Consts.LENGTH_DSK)
            {
                if (DiskLogs.Count >= Consts.MAX_DSK_LOGS)
                {
                    DiskLogs.RemoveAt(0);
                }
                DiskLogs.Add(new DiskUsageLog()
                {
                    Sec = SecondCounter,
                    TSK = diskElements[Consts.DTSK],
                    Seize = diskElements[Consts.SEIZE],
                    Queue = diskElements[Consts.QUEUE],
                    App = diskElements[Consts.DAPP],
                    IAR = diskElements[Consts.DIAR],
                    TimeStamp = diskElements[Consts.TIME]
                });
            }
        }


        //the read line is a task element, this method parses it correctly and stores it to a Task object
        private void LineIsTaskline()
        {
            //since client should contain any character, it cannot be tokenized by whitespace. Hardcoding instead
            string tskLine = line.Substring(0, Consts.LENGTH_TSK[Consts.TSK]);
            string clientLine = line.Substring(Consts.LENGTH_TSK[Consts.TSK] + 1, Consts.LENGTH_TSK[Consts.CLNT]);
            line = line.Substring(Consts.LENGTH_TSK[Consts.TSK] + Consts.LENGTH_TSK[Consts.CLNT] + 1 + 1);

            List<string> taskElements = Regex.Split(line, @" +").OfType<string>().ToList(); //fancy way of transforming string array to list
            taskElements.Insert(0, clientLine);
            taskElements.Insert(0, tskLine);

            if (taskElements.Count != Consts.LENGTH_TSK.Length) //list is not the correct size, meaning there is a blank entry somewhere
            {
                int rdHead = 0;
                for (int i = 2; i < 13; i++)
                {
                    if (string.IsNullOrWhiteSpace(line.Substring(rdHead, Consts.LENGTH_TSK[i])))
                    {
                        taskElements.Insert(i, " ");
                    }
                    rdHead = rdHead + Consts.LENGTH_TSK[i] + 1;
                }
            }
            if (RunningTasks.Exists(x => x.TSK == taskElements[Consts.TSK]))
            {
                UpdateTasks(taskElements);
            }
            else
            {
                RunningTasks.Add(UpdateTasks(taskElements));
            }
        }

        //this nested loop takes all the indexes in UsageQuery, sorts them by seconds, then moves them to the top of RunningTasks
        private void SortUsageQuery()
        {
            foreach (string index in UsageQuery)
            {
                Task currentTask = RunningTasks.Find(x => x.TSK == index);
                if (currentTask != null)
                {
                    currentTask.CPU = String.Concat(Math.Round(((currentTask.CPUTime).TotalSeconds * 100), 2).ToString(), "%"); //doesn't calculate anything anymore, just a % rep of seconds difference
                    RunningTasks.Remove(currentTask);
                    int i = 0;
                    while (RunningTasks[i].CPUTime.TotalSeconds > currentTask.CPUTime.TotalSeconds)
                    {
                        i = i + 1;
                    }
                    RunningTasks.Insert(i, currentTask);
                }
            }
            if (SortBy == Consts.SORT_DA) //case statements don't like const.SORT, using if ladder instead
            {
                RunningTasks.Sort((x, y) => y.DACalls.CompareTo(x.DACalls));
            }
            else if (SortBy == Consts.SORT_KEY)
            {
                RunningTasks.Sort((x, y) => y.KeyCalls.CompareTo(x.KeyCalls));
            }
            else if (SortBy == Consts.SORT_RD)
            {
                RunningTasks.Sort((x, y) => y.DskReads.CompareTo(x.DskReads));
            }
            else if (SortBy == Consts.SORT_WR)
            {
                RunningTasks.Sort((x, y) => y.DskWrite.CompareTo(x.DskWrite));
            }
            else if (SortBy == Consts.SORT_TSK)
            {
                RunningTasks.Sort((x, y) => x.TSK.CompareTo(y.TSK));
            }
        }

        //the read line says a new second is starting, prepares everything for new data to be read in
        private async System.Threading.Tasks.Task LineIsNewSecond()
        {
            SortUsageQuery();

            if (Stopped == false || FileOpened == true)
            {
                TaskList.Items.Refresh();
                CPUgraph.addNewPoint(NetUsage);
            }
            DiskList.Items.Refresh(); //throws an exception if this is paused

            //if the Delay needs to stop, this catches the exception thrown; this whole block is encapsulated in an if because it should only pause if reading a file
            if (FileOpened == true)
            {
                WaitingOnFeedback = true;
                do
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(Delay, KillDelay.Token);
                    }
                    catch
                    {
                        //next has been pressed
                        KillDelay.Dispose();
                        KillDelay = new CancellationTokenSource();
                        break;
                    }

                } while (Stopped == true);
                WaitingOnFeedback = false;
            }

            UsageQuery.Clear();
            NetUsage = 0;
            SecondCounter = SecondCounter + 1;
            Console.WriteLine(SecondCounter);
        }

        private async System.Threading.Tasks.Task ChangeCopyrightBox()
        {
            CopyrightBox = "";
            while (Consts.STARTUP_REGEX.IsMatch(line) != true)
            {
                CopyrightBox = CopyrightBox + "\n" + line;
                line = await ReadNextLine();
            }
            CopyrightBox = Regex.Replace(CopyrightBox, @"�", ""); //it works?
        }

        //acts as main loop: is responsible for instantiation/closing objects, determining if reading file or live data, navigating through file, and catching wonky behavior
        private async void TaskList_Loaded() //I've added async here so that this runs asynchronously, meaning I can use Task.Delay without shutting down the UI
        {
            if (TaskRunning == true || string.IsNullOrEmpty(Path))
            {
                return;
            }
            TaskRunning = true;
            Tabs.IsEnabled = true;

            if(FileOpened == true)
            {
                infile = new StreamReader(Path);
            }
            else
            {
                InitializeGrabHistory();
                ReadPipe = new ClientPipe();
                if(ReadPipe.Open(".") == false) //keep server name as . for now
                {
                    MessageBox.Show("Failed to connect to server");
                    TaskRunning = false;
                    Tabs.IsEnabled = false;
                    return;
                }
            }
            line = await ReadNextLine();



            //StreamReader infile = new StreamReader(Path);
            //string line = infile.ReadLine();
            SecondCounter = 1;
            NetUsage = 0;
            CopyrightBox = string.Empty;

            RunningTasks = new List<Task>(); //list of task objects to be displayed by DataGrid
            DiskLogs = new List<DiskUsageLog>();
            UsageQuery = new List<string>(); //if CPU changed for a task, its TSK index is stored here in an attempt to reduce time complexity
            TaskList.ItemsSource = RunningTasks;
            DiskList.ItemsSource = DiskLogs;
            StartupLog.Text = "";
            string startupString = "";

            canGraph.Children.Clear(); //makes sure that when a new file opens, the old graph goes away
            CPUgraph = new UsageGraph(Consts.GRAPH_MARGIN, canGraph.Width - Consts.GRAPH_MARGIN, Consts.GRAPH_MARGIN, canGraph.Height - Consts.GRAPH_MARGIN, canGraph.Width, canGraph.Height, Consts.GRAPH_MARGIN); //makes a new graph object
            canGraph.Children.Add(CPUgraph.getXaxis()); //draw x-axis
            canGraph.Children.Add(CPUgraph.getYaxis()); //draw y-axis
            canGraph.Children.Add(CPUgraph.getLine()); //draws the changing line

            while (line != null && TaskRunning == true)
            {
                if (Consts.TASK_REGEX.IsMatch(line) == true) //is an acceptable entry
                {
                    LineIsTaskline();
                }
                else if(Consts.STEP_REGEX.IsMatch(line) == true && RunningTasks.Count != 0) //assumes that each second runs in order
                {
                    await LineIsNewSecond();
                }
                else if(Consts.STEP_REGEX.IsMatch(line) == true)
                {
                    StartupLog.Text = startupString;
                }
                else if(Consts.DISK_REGEX.IsMatch(line) == true)
                {
                    line = await ReadNextLine(); //we're doing a readline here since the next line will (hopefully) be disk stuff
                    AddToDiskQuery(Regex.Split(line, @" +|,|=").OfType<string>().ToList()); //this giant argument being passed is a fancy way of transforming important disk line to a list of strings
                }
                else if(Consts.COPYRIGHT_REGEX.IsMatch(line) == true)
                {
                    //copyright section found, read through until startup messages
                    line = await ReadNextLine();
                    await ChangeCopyrightBox();
                }


                if(StartupLog.Text == "")
                {
                    startupString = startupString + line + "\n";
                }
                line = await ReadNextLine();
            }

            if(RunningTasks.Any() == true && TaskRunning == true) //read through the entire grab file
            {
                MessageBox.Show("End of file");
            }
            else if(RunningTasks.Any() == true) //reading through grab file, but was interrupted
            {
                Tabs.IsEnabled = false;
            }
            else //didn't read anything
            {
                Tabs.IsEnabled = false;
                MessageBox.Show("There appears to be no data\nAre you using the correct file format?");
            }

            TaskRunning = false;
            if (FileOpened == true)
            {
                infile.Close();
            }
            else
            {
                outfile.Close();
                ReadPipe.Close();
            }
        }


        private void DiskList_Loaded(object sender, RoutedEventArgs e)
        {
            //Console.WriteLine("Loaded disklist gui");
        }

        //when file is opened; if this is called while data is still being read, it siginals a global and waits every 1000ms for it to finish
        private async void OpenGrab_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog newFileWindow = new OpenFileDialog();
            if(newFileWindow.ShowDialog() == true)
            {
                TaskRunning = false;
                KillDelay.Cancel();
                while (WaitingOnFeedback == true)
                {
                    await System.Threading.Tasks.Task.Delay(1000);
                }
                Path = newFileWindow.FileName;
                FileOpened = true;
                TaskList_Loaded();
            }
        }

        //"next" is clicked
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            KillDelay.Cancel();
            if(Stopped == true && FileOpened == false)
            {
                TaskList.Items.Refresh();
            }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            if (Stopped == true)
            {
                Stopped = false;
            }
            else
            {
                Stopped = true;
            }
        }

        private void highSpeed_Click(object sender, RoutedEventArgs e)
        {
            Delay = 500;
            highSpeed.IsChecked = true;
            medSpeed.IsChecked = false;
            lowSpeed.IsChecked = false;
        }

        private void medSpeed_Click(object sender, RoutedEventArgs e)
        {
            Delay = 1000;
            highSpeed.IsChecked = false;
            medSpeed.IsChecked = true;
            lowSpeed.IsChecked = false;
        }

        private void lowSpeed_Click(object sender, RoutedEventArgs e)
        {
            Delay = 5000;
            highSpeed.IsChecked = false;
            medSpeed.IsChecked = false;
            lowSpeed.IsChecked = true;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CanGraph_Loaded(object sender, RoutedEventArgs e)
        {
            //do nothing
        }

        private void Copyright_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrEmpty(CopyrightBox) == false)
            {
                MessageBox.Show(CopyrightBox);
            }
        }

        private void TaskList_Loaded_1(object sender, RoutedEventArgs e)
        {
            if(WindowIsLoaded == false)
            {
                WindowIsLoaded = true;
                TaskList_Loaded();
            }
        }

        //arranges data in specified order so every update keeps that order
        private void Column_Click(object sender, RoutedEventArgs e)
        {
            DataGridColumnHeader columnHeader = sender as DataGridColumnHeader;
            if (String.Compare(columnHeader.Content.ToString(), "KeyCalls") == 0)
            {
                SortBy = 2;
            }
            else if (String.Compare(columnHeader.Content.ToString(), "DACalls") == 0)
            {
                SortBy = 3;
            }
            else if (String.Compare(columnHeader.Content.ToString(), "DskReads") == 0)
            {
                SortBy = 4;
            }
            else if (String.Compare(columnHeader.Content.ToString(), "DskWrite") == 0)
            {
                SortBy = 5;
            }
            else if (String.Compare(columnHeader.Content.ToString(), "TSK") == 0)
            {
                SortBy = 6;
            }
            else
            {
                SortBy = 1;
            }
        }

        private void ViewHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(HistoryRoot);
            }
            catch
            {
                MessageBox.Show("History folder not found");
            }
        }
    }
}
