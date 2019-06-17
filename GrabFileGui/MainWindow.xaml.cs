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
        private Constants consts;

        private string path;
        private string copyrightBox;
        private string historyRoot;
        private string historyFolder;
        private bool taskRunning; //makes sure that when TaskList is loaded again by mistake, it won't start a new work thread
        private bool stopped;
        private bool fileOpened;
        private bool windowIsLoaded;
        private bool waitingOnFeedback; //is true when readNextLine() is awaiting input
        private int delay;
        private int sortBy;
        private StreamReader infile;
        private StreamWriter outfile;
        private ClientPipe readPipe;
        private CancellationTokenSource killDelay;

        //stuff below WERE things that used to be local but are now global to help divide and conquer
        UsageGraph CPUgraph;
        private string line;
        private List<Task> runningTasks;
        private List<DiskUsageLog> diskLogs;
        private List<string> usageQuery;
        private double netUsage;
        int secondCounter;

        public MainWindow()
        {
            consts = new Constants();

            path = "live";
            historyRoot = "GrabHistory";
            historyFolder = historyRoot + @"\" + DateTime.Now.ToString("MMddyyyy");
            fileOpened = false;
            taskRunning = false;
            stopped = false;
            windowIsLoaded = false;
            delay = 1000;
            sortBy = 1;
            killDelay = new CancellationTokenSource(); //kills the delay when next is pressed
            InitializeComponent();
        }

        //when live data is being read, this is called first to prepare a file to write to; also responsible for zipping/deleting files
        private void InitializeGrabHistory() //creator of a new file to log grab history
        {
            if (Directory.Exists(historyFolder) == false)
            {
                Directory.CreateDirectory(historyFolder);
            }

            foreach(string dir in Directory.GetDirectories(historyRoot))
            {
                if(String.Compare(dir, historyFolder) != 0)
                {
                    ZipFile.CreateFromDirectory(dir, dir + ".zip");
                    Directory.Delete(dir, true);
                }
            }
            foreach (string file in Directory.GetFiles(historyRoot, "*.zip"))
            {
                DateTime then = Directory.GetLastAccessTimeUtc(file);
                DateTime now = DateTime.UtcNow;
                if(now.Subtract(then).TotalDays >= 14)
                {
                    Directory.Delete(file, true);
                }
            }

            string newFile = historyFolder + @"\" + DateTime.Now.ToString("HHmmss") + ".txt";
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
            if(fileOpened == true)
            {
                return infile.ReadLine();
            }

            waitingOnFeedback = true;
            System.Threading.Tasks.Task<string> t1 = System.Threading.Tasks.Task.Run(() => readPipe.Read());
            await t1;
            waitingOnFeedback = false;
            UpdateGrabHistory(t1.Result);
            return t1.Result;
        }

        //adds or updates a task in runningTasks using data passed as a list of strings
        private Task UpdateTasks(List<string> taskElements)
        {
            Task foundOccurance = runningTasks.Find(x => x.TSK == taskElements[consts.TSK]);
            if(foundOccurance == null)
            {
                foundOccurance = new Task();
            }
            foundOccurance.TSK = taskElements[consts.TSK];
            foundOccurance.Client = taskElements[consts.CLNT];
            foundOccurance.App = taskElements[consts.APP];
            foundOccurance.Version = taskElements[consts.VER];
            foundOccurance.IAR = taskElements[consts.IAR];
            foundOccurance.CK = taskElements[consts.CK];
            foundOccurance.SVC = taskElements[consts.SVC];
            foundOccurance.CPU = "0%";
            foundOccurance.CPUTime = TimeSpan.Parse(taskElements[consts.CPU].Remove(consts.TIME_DOT) + "." + taskElements[consts.CPU].Substring(consts.TIME_DOT + 1));
            foundOccurance.File = taskElements[consts.FILE];
            foundOccurance.KeyCalls = int.Parse(taskElements[consts.KEY], System.Globalization.NumberStyles.HexNumber);
            foundOccurance.DACalls = int.Parse(taskElements[consts.DA], System.Globalization.NumberStyles.HexNumber);
            foundOccurance.DskReads = int.Parse(taskElements[consts.RD], System.Globalization.NumberStyles.HexNumber);
            foundOccurance.DskWrite = int.Parse(taskElements[consts.WR], System.Globalization.NumberStyles.HexNumber);

            double differenceCPU = 1 - (new TimeSpan(0, 0, consts.SEC) - foundOccurance.CPUTime).TotalSeconds;
            if (differenceCPU > 0)
            {
                netUsage = netUsage + differenceCPU;
                usageQuery.Insert(0, taskElements[consts.TSK]);
            }

            return foundOccurance;
        }

        private void AddToDiskQuery(List<string> diskElements)
        {
            if (diskElements.Count == consts.LENGTH_DSK)
            {
                if (diskLogs.Count >= consts.MAX_DSK_LOGS)
                {
                    diskLogs.RemoveAt(0);
                }
                diskLogs.Add(new DiskUsageLog()
                {
                    Sec = secondCounter,
                    TSK = diskElements[consts.DTSK],
                    Seize = diskElements[consts.SEIZE],
                    Queue = diskElements[consts.QUEUE],
                    App = diskElements[consts.DAPP],
                    IAR = diskElements[consts.DIAR],
                    TimeStamp = diskElements[consts.TIME]
                });
            }
        }


        //the read line is a task element, this method parses it correctly and stores it to a Task object
        private void LineIsTaskline()
        {
            //since client should contain any character, it cannot be tokenized by whitespace. Hardcoding instead
            string tskLine = line.Substring(0, consts.LENGTH_TSK[consts.TSK]);
            string clientLine = line.Substring(consts.LENGTH_TSK[consts.TSK] + 1, consts.LENGTH_TSK[consts.CLNT]);
            line = line.Substring(consts.LENGTH_TSK[consts.TSK] + consts.LENGTH_TSK[consts.CLNT] + 1 + 1);

            List<string> taskElements = Regex.Split(line, @" +").OfType<string>().ToList(); //fancy way of transforming string array to list
            taskElements.Insert(0, clientLine);
            taskElements.Insert(0, tskLine);

            if (taskElements.Count != consts.LENGTH_TSK.Length) //list is not the correct size, meaning there is a blank entry somewhere
            {
                int rdHead = 0;
                for (int i = 2; i < 13; i++)
                {
                    if (string.IsNullOrWhiteSpace(line.Substring(rdHead, consts.LENGTH_TSK[i])))
                    {
                        taskElements.Insert(i, " ");
                    }
                    rdHead = rdHead + consts.LENGTH_TSK[i] + 1;
                }
            }
            if (runningTasks.Exists(x => x.TSK == taskElements[consts.TSK]))
            {
                UpdateTasks(taskElements);
            }
            else
            {
                runningTasks.Add(UpdateTasks(taskElements));
            }
        }

        //this nested loop takes all the indexes in usageQuery, sorts them by seconds, then moves them to the top of runningTasks
        private void SortUsageQuery()
        {
            foreach (string index in usageQuery)
            {
                Task currentTask = runningTasks.Find(x => x.TSK == index);
                if (currentTask != null)
                {
                    currentTask.CPU = String.Concat(Math.Round(((currentTask.CPUTime).TotalSeconds * 100), 2).ToString(), "%"); //doesn't calculate anything anymore, just a % rep of seconds difference
                    runningTasks.Remove(currentTask);
                    int i = 0;
                    while (runningTasks[i].CPUTime.TotalSeconds > currentTask.CPUTime.TotalSeconds)
                    {
                        i = i + 1;
                    }
                    runningTasks.Insert(i, currentTask);
                }
            }
            if (sortBy == consts.SORT_DA) //case statements don't like const.SORT, using if ladder instead
            {
                runningTasks.Sort((x, y) => y.DACalls.CompareTo(x.DACalls));
            }
            else if (sortBy == consts.SORT_KEY)
            {
                runningTasks.Sort((x, y) => y.KeyCalls.CompareTo(x.KeyCalls));
            }
            else if (sortBy == consts.SORT_RD)
            {
                runningTasks.Sort((x, y) => y.DskReads.CompareTo(x.DskReads));
            }
            else if (sortBy == consts.SORT_WR)
            {
                runningTasks.Sort((x, y) => y.DskWrite.CompareTo(x.DskWrite));
            }
            else if (sortBy == consts.SORT_TSK)
            {
                runningTasks.Sort((x, y) => x.TSK.CompareTo(y.TSK));
            }
        }

        //the read line says a new second is starting, prepares everything for new data to be read in
        private async System.Threading.Tasks.Task LineIsNewSecond()
        {
            SortUsageQuery();

            if (stopped == false || fileOpened == true)
            {
                TaskList.Items.Refresh();
                CPUgraph.addNewPoint(netUsage);
            }
            DiskList.Items.Refresh(); //throws an exception if this is paused

            //if the delay needs to stop, this catches the exception thrown; this whole block is encapsulated in an if because it should only pause if reading a file
            if (fileOpened == true)
            {
                waitingOnFeedback = true;
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
                waitingOnFeedback = false;
            }

            usageQuery.Clear();
            netUsage = 0;
            secondCounter = secondCounter + 1;
            Console.WriteLine(secondCounter);
        }

        private async void ChangeCopyrightBox()
        {
            copyrightBox = "";
            Regex startupRegex = new Regex(@"^\[\d\d Startup messages]$");
            while (startupRegex.IsMatch(line) != true)
            {
                copyrightBox = copyrightBox + "\n" + line;
                line = await ReadNextLine();
            }
            copyrightBox = Regex.Replace(copyrightBox, @"�", ""); //it works?
        }

        //acts as main loop: is responsible for instantiation/closing objects, determining if reading file or live data, navigating through file, and catching wonky behavior
        private async void TaskList_Loaded() //I've added async here so that this runs asynchronously, meaning I can use Task.Delay without shutting down the UI
        {
            if (taskRunning == true || string.IsNullOrEmpty(path))
            {
                return;
            }
            taskRunning = true;
            Tabs.IsEnabled = true;

            if(fileOpened == true)
            {
                infile = new StreamReader(path);
            }
            else
            {
                InitializeGrabHistory();
                readPipe = new ClientPipe();
                if(readPipe.Open(".") == false) //keep server name as . for now
                {
                    MessageBox.Show("Failed to connect to server");
                    taskRunning = false;
                    Tabs.IsEnabled = false;
                    return;
                }
            }
            line = await ReadNextLine();



            //StreamReader infile = new StreamReader(path);
            //string line = infile.ReadLine();
            secondCounter = 1;
            netUsage = 0;
            copyrightBox = string.Empty;

            runningTasks = new List<Task>(); //list of task objects to be displayed by DataGrid
            diskLogs = new List<DiskUsageLog>();
            usageQuery = new List<string>(); //if CPU changed for a task, its TSK index is stored here in an attempt to reduce time complexity
            TaskList.ItemsSource = runningTasks;
            DiskList.ItemsSource = diskLogs;
            StartupLog.Text = "";
            string startupString = line;

            canGraph.Children.Clear(); //makes sure that when a new file opens, the old graph goes away
            CPUgraph = new UsageGraph(consts.GRAPH_MARGIN, canGraph.Width - consts.GRAPH_MARGIN, consts.GRAPH_MARGIN, canGraph.Height - consts.GRAPH_MARGIN, canGraph.Width, canGraph.Height, consts.GRAPH_MARGIN); //makes a new graph object
            canGraph.Children.Add(CPUgraph.getXaxis()); //draw x-axis
            canGraph.Children.Add(CPUgraph.getYaxis()); //draw y-axis
            canGraph.Children.Add(CPUgraph.getLine()); //draws the changing line

            while (line != null && taskRunning == true)
            {
                if (consts.TASK_REGEX.IsMatch(line) == true) //is an acceptable entry
                {
                    LineIsTaskline();
                }
                else if(consts.STEP_REGEX.IsMatch(line) == true && runningTasks.Count != 0) //assumes that each second runs in order
                {
                    await LineIsNewSecond();
                }
                else if(consts.STEP_REGEX.IsMatch(line) == true)
                {
                    StartupLog.Text = startupString;
                }
                else if(consts.DISK_REGEX.IsMatch(line) == true)
                {
                    line = await ReadNextLine(); //we're doing a readline here since the next line will (hopefully) be disk stuff
                    AddToDiskQuery(Regex.Split(line, @" +|,|=").OfType<string>().ToList()); //this giant argument being passed is a fancy way of transforming important disk line to a list of strings
                }
                else if(consts.COPYRIGHT_REGEX.IsMatch(line) == true)
                {
                    //copyright section found, read through until startup messages
                    line = await ReadNextLine();
                    ChangeCopyrightBox();
                }


                if(StartupLog.Text == "")
                {
                    startupString = startupString + line + "\n";
                }
                line = await ReadNextLine();
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
            if (fileOpened == true)
            {
                infile.Close();
            }
            else
            {
                outfile.Close();
                readPipe.Close();
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
                taskRunning = false;
                killDelay.Cancel();
                while (waitingOnFeedback == true)
                {
                    await System.Threading.Tasks.Task.Delay(1000);
                }
                path = newFileWindow.FileName;
                fileOpened = true;
                TaskList_Loaded();
            }
        }

        //"next" is clicked
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            killDelay.Cancel();
            if(stopped == true && fileOpened == false)
            {
                TaskList.Items.Refresh();
            }
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

        private void CanGraph_Loaded(object sender, RoutedEventArgs e)
        {
            //do nothing
        }

        private void Copyright_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrEmpty(copyrightBox) == false)
            {
                MessageBox.Show(copyrightBox);
            }
        }

        private void TaskList_Loaded_1(object sender, RoutedEventArgs e)
        {
            if(windowIsLoaded == false)
            {
                windowIsLoaded = true;
                TaskList_Loaded();
            }
        }

        //arranges data in specified order so every update keeps that order
        private void Column_Click(object sender, RoutedEventArgs e)
        {
            DataGridColumnHeader columnHeader = sender as DataGridColumnHeader;
            if (String.Compare(columnHeader.Content.ToString(), "KeyCalls") == 0)
            {
                sortBy = 2;
            }
            else if (String.Compare(columnHeader.Content.ToString(), "DACalls") == 0)
            {
                sortBy = 3;
            }
            else if (String.Compare(columnHeader.Content.ToString(), "DskReads") == 0)
            {
                sortBy = 4;
            }
            else if (String.Compare(columnHeader.Content.ToString(), "DskWrite") == 0)
            {
                sortBy = 5;
            }
            else if (String.Compare(columnHeader.Content.ToString(), "TSK") == 0)
            {
                sortBy = 6;
            }
            else
            {
                sortBy = 1;
            }
        }

        private void ViewHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(historyRoot);
            }
            catch
            {
                MessageBox.Show("History folder not found");
            }
        }
    }
}
