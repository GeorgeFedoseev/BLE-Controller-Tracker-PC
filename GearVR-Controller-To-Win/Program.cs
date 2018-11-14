using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;

namespace gearvr_controller_to_win
{
    class Program
    {
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private delegate bool EventHandler(CtrlType sig);
        static EventHandler _handler;

        enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        

        static void Main(string[] args)
        {
            _handler += new EventHandler(ConsoleExitHandler);
            SetConsoleCtrlHandler(_handler, true);

            // CONFIG
            // init config
            var config = Config.Main;
            if(config == null) {
                Console.ReadKey(); return;
            }
            else {
                Console.WriteLine("Config loaded.");
            }




            new ControllersTracker().Run();
            while (true) { Thread.Sleep(100); }
        }

     


        private static bool ConsoleExitHandler(CtrlType sig)
        {
            Console.WriteLine("Exiting system due to external CTRL-C, or process kill, or shutdown");
            

            //do your cleanup here
            //Thread.Sleep(5000); //simulate some cleanup delay

            Console.WriteLine("Cleanup complete");

            //allow main to run off
            //exitSystem = true;

            //shutdown right away so there are no lingering threads
            Environment.Exit(-1);

            return true;
        }



    }
}
