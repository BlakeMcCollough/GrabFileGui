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

namespace GrabFileGui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool stopped;
        private int delay;
        private CancellationTokenSource killDelay;

        public MainWindow()
        {
            stopped = false;
            delay = 1000;
            killDelay = new CancellationTokenSource(); //kills the delay when next is pressed
            InitializeComponent();
        }

        private async void TaskList_Loaded(object sender, RoutedEventArgs e) //I've added async here so that this runs asynchronously, meaning I can use Task.Delay without shutting down the UI
        {
            const string PATH = "../../GRAB.190509.123741";
            const int LENGTH = 13;
            const int SEC = 1;
            const int TSK = 0, CLNT = 1, APP = 2, VER = 3, IAR = 4, CK = 5, SVC = 6, CPU = 7, FILE = 8, KEY = 9, DA = 10, RD = 11, WR = 12; //corresponds to index in task list

            //checks for three numbers, 12 of any characters, any word (including .) and the rest of the data entry in the format of the grab file
            //the purpose is to check if the line read is a data entry
            Regex tskAcceptRegex = new Regex(@"^\d{3} .{12} \w* +(\w|\\.)* +\w{4} +\w\w +\w\w +\d\d:\d\d:\d\d:\d\d\d +\w* +\w{8} +\w{8} \w{8} +\w{8} *$");
            //similar to tskAccept, but it checks if it's a header to a new second
            Regex stepAcceptRegex = new Regex(@"^[\d\d Task info \d+]");


            StreamReader infile = new StreamReader(PATH);
            string line = infile.ReadLine();
            int secondCounter = 0;
            double netUsage = 0;

            List<Task> runningTasks = new List<Task>(); //list of task objects to be displayed by DataGrid
            List<string> usageQuery = new List<string>(); //if CPU changed for a task, its TSK index is stored here in an attempt to reduce time complexity
            TaskList.ItemsSource = runningTasks;
            while (line != null)
            {
                if(tskAcceptRegex.IsMatch(line) == true) //is an acceptable entry
                {
                    //since client should contain any character, it cannot be tokenized by whitespace. Hardcoding instead
                    string tskLine = line.Substring(0, 3);
                    string clientLine = line.Substring(4, 12);
                    line = line.Substring(17);
                    string[] returnedSplitList = Regex.Split(line, @" +");
                    
                    List<string> taskElements = returnedSplitList.OfType<string>().ToList(); //fancy way of transforming string array to list
                    taskElements.Insert(0, clientLine);
                    taskElements.Insert(0, tskLine);
                    if (taskElements.Count != LENGTH)
                    {
                        taskElements.Insert(8, " ");
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
                    Console.WriteLine(netUsage);
                    foreach(string index in usageQuery)
                    {
                        Task currentTask = runningTasks.Find(x => x.TSK == index);
                        if(currentTask != null)
                        {
                            currentTask.CPU = String.Concat(Math.Round(((currentTask.CPUTime).TotalSeconds / netUsage * 100), 2).ToString(), "%"); //calculates each % of total CPU usages: divides the current task's CPU use by sum of all CPU uses
                            runningTasks.Remove(currentTask);
                            runningTasks.Insert(0, currentTask);
                        }
                    }
                    TaskList.Items.Refresh();

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


                line = infile.ReadLine();
            }

            MessageBox.Show("End of file");
            infile.Close();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if(stopped == true)
            {
                NextButton.IsEnabled = false;
                stopped = false;
                PauseButton.Content = "Pause";
            }
            else
            {
                NextButton.IsEnabled = true;
                stopped = true;
                PauseButton.Content = "Resume";
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            killDelay.Cancel();
        }

        private void SpeedBox_LostFocus(object sender, RoutedEventArgs e)
        {
            int newSpeed = 0;

            if(Int32.TryParse(SpeedBox.Text, out newSpeed) && newSpeed > 0 && newSpeed < 10000000)
            {
                delay = newSpeed;
            }
            else
            {
                SpeedBox.Text = delay.ToString();
            }
        }
        private void SpeedBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                if (e.Key == Key.Enter)
                {
                    SpeedBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
            }
        }
    }
}
